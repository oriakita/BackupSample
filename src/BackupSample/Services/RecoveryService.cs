using System.IO;
using System.IO.Compression;
using System.Security.AccessControl;
using Azure.Storage.Blobs;
using BackupSample.Models;

namespace BackupSample.Services
{
    public class RecoveryService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _containerName = "backup-container";
        private readonly MetadataManager _metadataManager;
        private readonly CompressionService _compressionService;
        private readonly string _passphrase = "BACKUP-TEST-2025";
        private readonly IEncryptionService _encryptionService;


        public RecoveryService(string connectionString)
        {
            _blobServiceClient = new BlobServiceClient(connectionString);
            _metadataManager = new MetadataManager(connectionString, _containerName);
            _compressionService = new CompressionService();
            _encryptionService = new AesEncryptionService();
        }

        public async Task<List<BackupManifest>> GetAvailableBackupsAsync()
        {
            return await _metadataManager.GetAllBackupManifestsAsync();
        }

        public async Task RecoveryFilesAsync(BackupManifest manifest, string recoveryLocation, List<string>? specificFiles = null, RecoveryOption option = RecoveryOption.Overwrite)
        {
            RestoreFolderAcls(manifest, recoveryLocation);
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var filesToRecovery = manifest.Files.Where(f => specificFiles == null || specificFiles.Contains(f.FilePath)).ToList();

            for (int i = 0; i < filesToRecovery.Count; i++)
            {
                var file = filesToRecovery[i];

                try
                {
                    await RecoveryFileAsync(file, containerClient, recoveryLocation, option);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(ex.Message, "Recovery Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        private async Task RecoveryFileAsync(FileMetadata fileMetadata, BlobContainerClient containerClient, string recoveryLocation, RecoveryOption option)
        {
            string targetPath;
            if (fileMetadata.RelativePath != "" && fileMetadata.RelativePath != null)
            {
                targetPath = Path.Combine(recoveryLocation, fileMetadata.RelativePath);
            }
            else
            {
                var fileNameOnly = Path.GetFileName(fileMetadata.FilePath);
                targetPath = Path.Combine(recoveryLocation, fileNameOnly);
            }
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

            if (fileMetadata.StructureType == FileStructureType.FolderOnly)
            {
                return;
            }

            if (File.Exists(targetPath))
            {
                switch (option)
                {
                    case RecoveryOption.Skip:
                        return;
                    case RecoveryOption.Rename:
                        targetPath = GetUniqueFileName(targetPath);
                        break;
                    case RecoveryOption.Overwrite:
                    default:
                        File.Delete(targetPath);
                        break;
                }
            }

            if (fileMetadata.StructureType == FileStructureType.StructureBased)
            {
                using var zip = ZipFile.Open(targetPath, ZipArchiveMode.Create);
                foreach (var component in fileMetadata.Components)
                {
                    var componentData = await ReassembleChunksAsync(component.Chunks, containerClient);
                    var entry = zip.CreateEntry(component.Name, CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    await entryStream.WriteAsync(componentData);
                }
            }
            else
            {
                using var outputStream = File.Create(targetPath);
                var fileData = await ReassembleChunksAsync(fileMetadata.Chunks, containerClient);
                await outputStream.WriteAsync(fileData);
            }

            var fileInfo = new FileInfo(targetPath);
            fileInfo.Attributes = fileMetadata.Attributes;
            fileInfo.LastWriteTimeUtc = fileMetadata.LastModified;

            // ---- Restore ACL ----
            if (!string.IsNullOrEmpty(fileMetadata.Acl))
            {
                try
                {
                    var sec = new FileSecurity();
                    sec.SetSecurityDescriptorSddlForm(fileMetadata.Acl);

                    var fileInfo2 = new FileInfo(targetPath);
                    fileInfo2.SetAccessControl(sec);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to restore ACL for file: {targetPath} - {ex.Message}");
                }
            }

        }

        private static string GetUniqueFileName(string path)
        {
            string dir = Path.GetDirectoryName(path)!;
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);

            int i = 1;
            string newPath;
            do
            {
                newPath = Path.Combine(dir, $"{name}({i}){ext}");
                i++;
            } while (File.Exists(newPath));

            return newPath;
        }


        private async Task<byte[]> ReassembleChunksAsync(IEnumerable<ChunkInfo> chunks, BlobContainerClient containerClient)
        {
            using var combinedStream = new MemoryStream();

            foreach (var chunk in chunks.OrderBy(c => c.Offset))
            {
                var blobClient = containerClient.GetBlobClient(chunk.BlobName);

                // --- Download raw chunk ---
                using var chunkStream = new MemoryStream();
                await blobClient.DownloadToAsync(chunkStream);
                var chunkBytes = chunkStream.ToArray();

                // --- Read metadata ---
                var props = await blobClient.GetPropertiesAsync();
                var metadata = props.Value.Metadata;

                bool isEncrypted = metadata.TryGetValue("encryption", out var enc) && enc == "aes256";
                bool isCompressed = metadata.TryGetValue("encoding", out var enc2) && enc2 == "gzip";

                // --- Step 1: Decrypt ---
                if (isEncrypted)
                {
                    chunkBytes = await _encryptionService.DecryptAsync(chunkBytes, _passphrase);
                }

                // --- Step 2: Decompress ---
                if (isCompressed)
                {
                    chunkBytes = await _compressionService.DecompressAsync(chunkBytes);
                }

                // --- Append to output ---
                await combinedStream.WriteAsync(chunkBytes);
            }

            return combinedStream.ToArray();
        }

        private static void RestoreFolderAcls(BackupManifest manifest, string recoveryLocation)
        {
            foreach (var entry in manifest.FolderAcls)
            {
                var relative = entry.Key;
                var sddl = entry.Value;

                var targetDir = Path.Combine(recoveryLocation, relative);

                try
                {
                    Directory.CreateDirectory(targetDir);

                    if (!string.IsNullOrEmpty(sddl))
                    {
                        var dirInfo = new DirectoryInfo(targetDir);
                        var sec = new DirectorySecurity();

                        sec.SetSecurityDescriptorSddlForm(sddl);
                        dirInfo.SetAccessControl(sec);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to restore folder ACL for: {targetDir} - {ex.Message}");
                }
            }
        }

        //public async Task RecoveryFilesAsync(BackupManifest manifest, string recoveryLocation, List<string>? specificFiles = null)
        //{
        //    var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
        //    var filesToRecovery = manifest.Files.Where(f => specificFiles == null || specificFiles.Contains(f.FilePath)).ToList();

        //    for (int i = 0; i < filesToRecovery.Count; i++)
        //    {
        //        var file = filesToRecovery[i];

        //        try
        //        {
        //            await RecoveryFileAsync(file, containerClient, recoveryLocation);
        //        }
        //        catch (Exception ex)
        //        {
        //            System.Windows.MessageBox.Show(ex.Message, "Recovery Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        //        }
        //    }
        //}
        //private async Task RecoveryFileAsync(FileMetadata fileMetadata, BlobContainerClient containerClient, string recoveryLocation)
        //{
        //    //var relativePath = Path.GetRelativePath(Path.GetPathRoot(fileMetadata.FilePath)!, fileMetadata.FilePath);
        //    //var targetPath = Path.Combine(recoveryLocation, relativePath);
        //    var fileNameOnly = Path.GetFileName(fileMetadata.FilePath);
        //    var targetPath = Path.Combine(recoveryLocation, fileNameOnly);

        //    var targetDir = Path.GetDirectoryName(targetPath);
        //    if (!string.IsNullOrEmpty(targetDir))
        //        Directory.CreateDirectory(targetDir);

        //    if (fileMetadata.StructureType == FileStructureType.StructureBased)
        //    {
        //        // --- Reconstruct structured file (Office, ZIP) from components ---
        //        using var zip = ZipFile.Open(targetPath, ZipArchiveMode.Create);

        //        foreach (var component in fileMetadata.Components)
        //        {
        //            var componentData = await ReassembleChunksAsync(component.Chunks, containerClient);

        //            // Create entry in file ZIP
        //            var entry = zip.CreateEntry(component.Name, CompressionLevel.Optimal);
        //            using var entryStream = entry.Open();
        //            await entryStream.WriteAsync(componentData);
        //        }
        //    }
        //    else
        //    {
        //        // --- Recovery normal file ---
        //        using var outputStream = File.Create(targetPath);
        //        var fileData = await ReassembleChunksAsync(fileMetadata.Chunks, containerClient);
        //        await outputStream.WriteAsync(fileData);
        //    }

        //    // --- Restore file attributes ---
        //    var fileInfo = new FileInfo(targetPath);
        //    fileInfo.Attributes = fileMetadata.Attributes;
        //    fileInfo.LastWriteTimeUtc = fileMetadata.LastModified;
        //}
        //private async Task<byte[]> ReassembleChunksAsync(IEnumerable<ChunkInfo> chunks, BlobContainerClient containerClient)
        //{
        //    using var combinedStream = new MemoryStream();

        //    foreach (var chunk in chunks.OrderBy(c => c.Offset))
        //    {
        //        var blobClient = containerClient.GetBlobClient(chunk.BlobName);

        //        using var chunkStream = new MemoryStream();
        //        await blobClient.DownloadToAsync(chunkStream);

        //        var chunkData = chunkStream.ToArray();
        //        if (chunk.IsCompressed)
        //        {
        //            chunkData = await _compressionService.DecompressAsync(chunkData);
        //        }

        //        await combinedStream.WriteAsync(chunkData);
        //    }

        //    return combinedStream.ToArray();
        //}
        //private async Task RecoveryFileAsync(FileMetadata fileMetadata, BlobContainerClient containerClient, string recoveryLocation)
        //{
        //    var relativePath = Path.GetRelativePath(Path.GetPathRoot(fileMetadata.FilePath)!, fileMetadata.FilePath);
        //    var targetPath = Path.Combine(recoveryLocation, relativePath);

        //    var targetDir = Path.GetDirectoryName(targetPath);
        //    if (!string.IsNullOrEmpty(targetDir))
        //    {
        //        Directory.CreateDirectory(targetDir);
        //    }

        //    if (fileMetadata.StructureType == FileStructureType.StructureBased)
        //    {
        //        // Reconstruct structured file (Office, ZIP) from components
        //        using var zip = ZipFile.Open(targetPath, ZipArchiveMode.Create);

        //        foreach (var component in fileMetadata.Components)
        //        {
        //            var blobClient = containerClient.GetBlobClient(component.BlobName);

        //            using var componentStream = new MemoryStream();
        //            await blobClient.DownloadToAsync(componentStream);

        //            var componentData = componentStream.ToArray();
        //            if (component.IsCompressed)
        //            {
        //                componentData = await _compressionService.DecompressAsync(componentData);
        //            }

        //            var entry = zip.CreateEntry(component.Name);
        //            using var entryStream = entry.Open();
        //            await entryStream.WriteAsync(componentData);
        //        }
        //    }
        //    else
        //    {
        //        // Recovery file chunks
        //        using var outputStream = File.Create(targetPath);

        //        foreach (var chunk in fileMetadata.Chunks.OrderBy(c => c.Offset))
        //        {
        //            var blobClient = containerClient.GetBlobClient(chunk.BlobName);

        //            using var chunkStream = new MemoryStream();
        //            await blobClient.DownloadToAsync(chunkStream);

        //            var chunkData = chunkStream.ToArray();
        //            if (chunk.IsCompressed)
        //            {
        //                chunkData = await _compressionService.DecompressAsync(chunkData);
        //            }

        //            outputStream.Seek(chunk.Offset, SeekOrigin.Begin);
        //            await outputStream.WriteAsync(chunkData);
        //        }
        //    }

        //    // Recovery file attributes and timestamps
        //    var fileInfo = new FileInfo(targetPath);
        //    fileInfo.Attributes = fileMetadata.Attributes;
        //    fileInfo.LastWriteTimeUtc = fileMetadata.LastModified;
        //}
    }
}
