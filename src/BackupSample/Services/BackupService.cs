using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Runtime;
using System.Security.AccessControl;
using System.Security.Cryptography;
using Azure.Storage.Blobs;
using BackupSample.Models;
using BackupSample.Utils;

namespace BackupSample.Services
{
    public class BackupService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _containerName = "backup-container";
        private readonly int _chunkSize = 65536; // 64KB chunks
        private readonly MetadataManager _metadataManager;
        private readonly CompressionService _compressionService;
        private readonly IEncryptionService _encryptionService;
        private readonly VSSService _vssService;
        private string _backupRoot = string.Empty;
        public void SetBackupRoot(string path)
        {
            _backupRoot = path;
        }

        public string GetBackupRoot()
        {
            return _backupRoot;
        }
        public BackupService(string connectionString)
        {
            _blobServiceClient = new BlobServiceClient(connectionString);
            _metadataManager = new MetadataManager(connectionString, _containerName);
            _compressionService = new CompressionService();
            _encryptionService = new AesEncryptionService();
            _vssService = new VSSService();

            // Create container if not exists
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            containerClient.CreateIfNotExists();
        }

        public async Task StartBackupAsync(List<string> selectedPaths, BackupTargetType backupTargetType)
        {
            //try
            //{
            //    // Scan files
            //    var filesToBackup = await ScanFilesAsync(selectedPaths);

            //    // Determine backup type and filter files
            //    var lastValidBackup = await _metadataManager.GetLastBackupManifestAsync();
            //    var backupType = lastValidBackup == null ? BackupType.Full : BackupType.Incremental;

            //    // Process files with CDC
            //    var manifest = new BackupManifest
            //    {
            //        Type = backupType
            //    };

            //    await ProcessFilesAsync(filesToBackup, manifest);

            //    // Save manifest
            //    await _metadataManager.SaveBackupManifestAsync(manifest);
            //}
            //catch (Exception ex)
            //{
            //    System.Windows.MessageBox.Show(ex.Message, "CDC Backup Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            //}
            //var filesToBackup = new List<FileMetadata>();

            //foreach (var path in selectedPaths)
            //{
            //    if (Directory.Exists(path))
            //    {
            //        filesToBackup.AddRange(ScanFolder(path));
            //    }
            //    else if (File.Exists(path))
            //    {
            //        filesToBackup.Add(await CreateFileMetadataAsync(path));
            //    }
            //}

            var lastValidBackup = await _metadataManager.GetLastBackupManifestAsync();
            var backupType = lastValidBackup == null ? BackupType.Full : BackupType.Incremental;
            var manifest = new BackupManifest { Type = backupType, Target = backupTargetType };
            var filesToBackup = await ScanFilesAsync(selectedPaths, manifest);

            await ProcessFilesAsync(filesToBackup, manifest);

            // Save manifest
            await _metadataManager.SaveBackupManifestAsync(manifest);
        }

        private async Task<List<FileMetadata>> ScanFilesAsync(List<string> paths, BackupManifest manifest)
        {
            var files = new List<FileMetadata>();

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    files.Add(await CreateFileMetadataAsync(path));
                }
                else if (Directory.Exists(path))
                {
                    var folderFiles = await ScanFolderAsync(path, path, manifest); // folder root = path
                    files.AddRange(folderFiles);
                }
            }

            return files;
        }

        private static async Task<List<FileMetadata>> ScanFolderAsync(string rootFolder, string currentFolder, BackupManifest manifest)
        {
            var files = new List<FileMetadata>();
            var dirInfo = new DirectoryInfo(currentFolder);
            string? acl = null;

            try
            {
                var sec = dirInfo.GetAccessControl();
                acl = sec.GetSecurityDescriptorSddlForm(AccessControlSections.All);
            }
            catch { }

            var relative = Path.GetRelativePath(rootFolder, currentFolder);

            manifest.FolderAcls[relative] = acl;

            foreach (var file in Directory.EnumerateFiles(currentFolder))
            {
                files.Add(await CreateFileMetadataAsync(file));
            }

            foreach (var folder in Directory.EnumerateDirectories(currentFolder))
            {
                var folderFiles = await ScanFolderAsync(rootFolder, folder, manifest);
                files.AddRange(folderFiles);
            }

            return files;
        }

        public async Task StartVolumeBackupAsync(string volumeRoot)
        {
            var lastValidBackup = await _metadataManager.GetLastBackupManifestAsync();
            var backupType = lastValidBackup == null ? BackupType.Full : BackupType.Incremental;
            var manifest = new BackupManifest { Type = backupType, Target = BackupTargetType.Volume };
            var filesToBackup = await GetAllFilesInVolumeAsync(volumeRoot, manifest);

            await ProcessFilesAsync(filesToBackup, manifest);

            // Save manifest
            await _metadataManager.SaveBackupManifestAsync(manifest);
        }

        public async Task<List<FileMetadata>> GetAllFilesInVolumeAsync(string volumeRoot, BackupManifest manifest)
        {
            var allFiles = new List<FileMetadata>();
            var dirs = new Stack<string>();

            dirs.Push(volumeRoot);

            while (dirs.Count > 0)
            {
                string currentDir = dirs.Pop();
                DirectoryInfo dirInfo = new DirectoryInfo(currentDir);

                // Skip junctions
                if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;

                try
                {
                    string? acl = null;
                    try
                    {
                        var sec = dirInfo.GetAccessControl();
                        acl = sec.GetSecurityDescriptorSddlForm(AccessControlSections.All);
                    }
                    catch { }

                    var relative = Path.GetRelativePath(volumeRoot, currentDir);
                    manifest.FolderAcls[relative] = acl;
                }
                catch { }
                try
                {
                    // Files
                    foreach (var file in Directory.EnumerateFiles(currentDir))
                    {
                        allFiles.Add(await CreateFileMetadataAsync(file));
                    }

                    // Subdirectories
                    foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                    {
                        try
                        {
                            var info = new DirectoryInfo(subDir);

                            // Skip junctions
                            var skipAttributes = FileAttributes.ReparsePoint
                               | FileAttributes.Hidden
                               | FileAttributes.System;

                            if ((info.Attributes & skipAttributes) != 0)
                            {
                                continue;
                            }

                            dirs.Push(subDir);
                        }
                        catch { }
                    }
                }
                catch
                {
                    // Skip forbidden folders
                }
            }

            return allFiles;
        }

        private static async Task<FileMetadata> CreateFileMetadataAsync(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            string? acl = null;
            try
            {
                var sec = fileInfo.GetAccessControl();
                acl = sec.GetSecurityDescriptorSddlForm(AccessControlSections.All);
            }
            catch { }

            var metadata = new FileMetadata
            {
                FilePath = filePath,
                Size = fileInfo.Length,
                LastModified = fileInfo.LastWriteTimeUtc,
                Attributes = fileInfo.Attributes,
                Acl = acl
            };

            // Calculate file hash for change detection
            metadata.Hash = await HashUtils.CalculateFileHashAsync(filePath);

            return metadata;
        }

        private async Task ProcessFilesAsync(List<FileMetadata> files, BackupManifest manifest)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                try
                {
                    string backupRoot = GetBackupRoot();
                    await ProcessFileWithCdcAsync(file, containerClient, backupRoot);
                    manifest.Files.Add(file);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(ex.Message, "Backup Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }

            manifest.TotalSize = manifest.Files.Sum(f => f.Size);
            manifest.CompressedSize = manifest.Files.Sum(f => (f.Chunks?.Sum(c => c.Size) ?? 0L) + (f.Components?.Sum(c => c.Size) ?? 0L));
            manifest.BackupSize = manifest.Files.Sum(file =>
                // chunks level file
                (file.Chunks?.Where(c => c.IsUpload).Sum(c => c.Size) ?? 0L) +
                // component level
                (file.Components?.Sum(comp => comp.Chunks?.Where(c => c.IsUpload).Sum(c => c.Size) ?? 0L) ?? 0L)
            );
        }

        private static async Task<bool> HasFileChangedAsync(FileMetadata file, FileMetadata previousMetadata)
        {
            if (previousMetadata == null) return true; // new file

            // 1. Compare size
            if (file.Size != previousMetadata.Size) return true;

            // 2. Compare timestamp
            if (file.LastModified != previousMetadata.LastModified) return true;

            // 3. Compare checksum
            if (!string.IsNullOrEmpty(previousMetadata.Hash))
            {
                byte[] data = await File.ReadAllBytesAsync(file.FilePath);
                string currentHash = HashUtils.CalculateSha256(data);
                file.Hash = currentHash;

                if (currentHash != previousMetadata.Hash)
                    return true;
            }

            return false; // file not changed
        }

        private async Task ProcessFileWithCdcAsync(FileMetadata fileMetadata, BlobContainerClient containerClient, string backupRoot)
        {
            if (backupRoot != "")
            {
                fileMetadata.RelativePath = Path.GetRelativePath(backupRoot, fileMetadata.FilePath);
            }

            var previousManifest = await _metadataManager.GetLastBackupManifestAsync();
            FileMetadata? previousFileMetadata = previousManifest?.Files
                .FirstOrDefault(f => f.FilePath == fileMetadata.FilePath);
            // Check if file has changed since last backup
            if (previousFileMetadata != null)
            {
                bool changed = await HasFileChangedAsync(fileMetadata, previousFileMetadata);
                if (!changed)
                {
                    fileMetadata.Chunks = previousFileMetadata.Chunks;
                    fileMetadata.StructureType = previousFileMetadata.StructureType;
                    fileMetadata.Components = previousFileMetadata.Components;
                    fileMetadata.LastBackup = DateTime.UtcNow;
                    return;
                }
            }

            if (CompressedFileParser.IsCompressedFile(fileMetadata.FilePath))
            {
                await ProcessCompressedFileWithCdcAsync(fileMetadata, containerClient);
            }
            else
            {
                // --- Normal file CDC processing ---
                var fileData = await File.ReadAllBytesAsync(fileMetadata.FilePath);
                //var chunks = await ProcessDataWithCdcAsync(fileData, containerClient, "chunks/");
                string passPhrase = "BACKUP-TEST-2025";
                var chunks = await ProcessDataWithCdcEncrypAsync(fileData, containerClient, "chunks/", passPhrase);

                fileMetadata.Chunks = chunks;
                fileMetadata.StructureType = FileStructureType.ChunkBased;
            }

            fileMetadata.LastBackup = DateTime.UtcNow;
        }

        /// <summary>
        /// Processes a compressed file by recursively decompressing it and its components until reaching
        /// non-compressed content, then applies CDC chunking to the final content.
        /// </summary>
        private async Task ProcessCompressedFileWithCdcAsync(FileMetadata fileMetadata, BlobContainerClient containerClient)
        {
            var files = new HashSet<string>();
            var componentInfos = new List<ComponentDetail>();

            var quietProgress = new Progress<int>(_ => { }); // Empty progress handler
            await ProcessCompressedComponentRecursivelyAsync(
                fileMetadata.FilePath,
                Path.GetFileName(fileMetadata.FilePath),
                containerClient,
                componentInfos,
                parentPath: "",
                null);

            fileMetadata.Components = componentInfos;
            fileMetadata.StructureType = FileStructureType.StructureBased;
        }

        /// <summary>
        /// Recursively processes a compressed component, handling arbitrary levels of nested compression.
        /// </summary>
        private async Task ProcessCompressedComponentRecursivelyAsync(
            string filePath,
            string componentName,
            BlobContainerClient containerClient,
            List<ComponentDetail> componentInfos,
            string parentPath,
            byte[]? data = null)
        {
            // If data is provided, write it to a temp file first
            string tempPath = "";
            bool usingTempFile = false;

            try
            {
                if (data != null)
                {
                    string extension = Path.GetExtension(componentName);
                    if (string.IsNullOrEmpty(extension))
                    {
                        // Write to temp file without extension to detect type
                        var detectPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                        try
                        {
                            await File.WriteAllBytesAsync(detectPath, data);
                            var type = await CompressedFileParser.DetectCompressionTypeAsync(detectPath);
                            extension = type switch
                            {
                                CompressionType.Zip => ".zip",
                                CompressionType.GZip => ".gz",
                                CompressionType.SqlServerBak => ".bak",
                                _ => ""
                            };
                        }
                        finally
                        {
                            if (File.Exists(detectPath))
                                File.Delete(detectPath);
                        }
                    }
                    tempPath = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}{extension}");
                    await File.WriteAllBytesAsync(tempPath, data);
                    filePath = tempPath;
                    usingTempFile = true;
                }

                // Check if this component is compressed
                bool isCompressed = CompressedFileParser.IsCompressedFile(filePath) &&
                                  await CompressedFileParser.DetectCompressionTypeAsync(filePath) != CompressionType.Unknown;

                if (isCompressed)
                {
                    // Extract and process nested components
                    var components = await CompressedFileParser.ParseComponentsAsync(filePath);
                    foreach (var component in components)
                    {
                        string nestedName = component.Item1;
                        byte[] nestedData = component.Item2;
                        
                        // Build the full path for this component
                        string nestedPath = string.IsNullOrEmpty(parentPath) 
                            ? nestedName 
                            : $"{parentPath}/{nestedName}";

                        // Recursively process this component
                        await ProcessCompressedComponentRecursivelyAsync(
                            filePath: "", // We'll pass data instead
                            componentName: nestedName,
                            containerClient,
                            componentInfos,
                            parentPath: nestedPath,
                            data: nestedData);
                    }
                }
                else
                {
                    // We've reached uncompressed content - apply CDC
                    byte[] finalData = data ?? await File.ReadAllBytesAsync(filePath);
                    List<ChunkInfo> chunks;
                    string passPhrase = "BACKUP-TEST-2025";
                    chunks = await ProcessDataWithCdcEncrypAsync(finalData, containerClient, "components/", passPhrase);

                    var combinedHash = HashUtils.CalculateSha256(
                        System.Text.Encoding.UTF8.GetBytes(string.Join("", chunks.Select(c => c.Hash)))
                    );

                    string fullPath = string.IsNullOrEmpty(parentPath) ? componentName : $"{parentPath}/{componentName}";
                    componentInfos.Add(new ComponentDetail
                    {
                        Name = fullPath,
                        Hash = combinedHash,
                        Size = chunks.Sum(c => c.Size),
                        IsCompressed = true,
                        Chunks = chunks
                    });
                }
            }
            finally
            {
                if (usingTempFile && File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private async Task<List<ChunkInfo>> ProcessDataWithCdcEncrypAsync(
            byte[] data,
            BlobContainerClient containerClient,
            string blobPrefix,
            string passphrase)
        {
            var fastCdc = new FastCdc();
            var chunks = new List<ChunkInfo>();
            var chunkEnds = fastCdc.Chunk(data);
            int start = 0;

            foreach (var end in chunkEnds)
            {
                int chunkSize = end - start;
                var chunkBytes = data.AsSpan(start, chunkSize).ToArray();

                // Original hash for dedupe
                var chunkHash = HashUtils.CalculateSha256(chunkBytes);
                var blobName = $"{blobPrefix}{chunkHash}";
                var blobClient = containerClient.GetBlobClient(blobName);

                long finalSize;

                if (!await blobClient.ExistsAsync())
                {
                    // COMPRESS
                    var compressedData = await _compressionService.CompressAsync(chunkBytes);

                    // ENCRYPT with passphrase
                    var encryptedData = await _encryptionService.EncryptAsync(compressedData, passphrase);
                    finalSize = encryptedData.Length;

                    using var encryptedStream = new MemoryStream(encryptedData);
                    await blobClient.UploadAsync(encryptedStream, overwrite: true);

                    // Metadata to restore
                    await blobClient.SetMetadataAsync(new Dictionary<string, string>
                    {
                        ["encoding"] = "gzip",
                        ["encryption"] = "aes256",
                        ["originalSize"] = chunkSize.ToString()
                    });
                }
                else
                {
                    var props = await blobClient.GetPropertiesAsync();
                    finalSize = props.Value.ContentLength;
                }

                chunks.Add(new ChunkInfo
                {
                    Hash = chunkHash,
                    Offset = start,
                    Size = finalSize,
                    BlobName = blobName,
                    IsCompressed = true,
                    IsEncrypted = true
                });

                start = end;
            }

            return chunks;
        }
        //public async Task StartVolumeBackupAsync(string volumeLetter)
        //{
        //    var (snapshotPath, snapshotId) = await _vssService.CreateSnapshotAsync(volumeLetter);

        //    try
        //    {
        //        var files = await ScanFilesRecursiveAsync(snapshotPath);

        //        var manifest = new BackupManifest();

        //        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

        //        foreach (var file in files)
        //        {
        //            await ProcessFileWithCdcAsync(file, containerClient, snapshotPath);
        //            manifest.Files.Add(file);
        //        }

        //        await _metadataManager.SaveBackupManifestAsync(manifest);
        //    }
        //    finally
        //    {
        //        await _vssService.DeleteSnapshotAsync(snapshotId);
        //    }
        //}

        private async Task<List<FileMetadata>> ScanFilesRecursiveAsync(string rootPath)
        {
            var files = new List<FileMetadata>();
            foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                var fileInfo = new FileInfo(file);

                var metadata = new FileMetadata
                {
                    FilePath = file,
                    RelativePath = Path.GetRelativePath(rootPath, file),
                    Size = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    Attributes = fileInfo.Attributes
                };

                // Hash calculation
                metadata.Hash = await HashUtils.CalculateFileHashAsync(file);

                files.Add(metadata);
            }

            // Save empty folders
            foreach (var dir in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
            {
                if (Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length == 0)
                {
                    var relativePath = Path.GetRelativePath(rootPath, dir);
                    files.Add(new FileMetadata
                    {
                        FilePath = dir,
                        RelativePath = relativePath,
                        StructureType = FileStructureType.FolderOnly
                    });
                }
            }

            return files;
        }

    }
}
