using Azure.Storage.Blobs;
using BackupSample.Models;
using BackupSample.Utils;
using System.IO;

namespace BackupSample.Services
{
    /// <summary>
    /// Handles recursive processing of compressed files, supporting arbitrary nesting levels
    /// </summary>
    internal class CompressedFileProcessor
    {
        private readonly BlobContainerClient _containerClient;
        private readonly IProgress<int>? _progress;
        private readonly CompressionService _compressionService;

        public CompressedFileProcessor(
            BlobContainerClient containerClient,
            CompressionService compressionService,
            IProgress<int>? progress = null)
        {
            _containerClient = containerClient;
            _compressionService = compressionService;
            _progress = progress;
        }

        /// <summary>
        /// Process a file that may contain multiple levels of compression
        /// </summary>
        public async Task<List<ComponentDetail>> ProcessFileAsync(string filePath)
        {
            var componentInfos = new List<ComponentDetail>();
            await ProcessCompressedComponentAsync(
                filePath,
                Path.GetFileName(filePath),
                componentInfos,
                parentPath: "");
            return componentInfos;
        }

        private async Task ProcessCompressedComponentAsync(
            string filePath,
            string componentName,
            List<ComponentDetail> componentInfos,
            string parentPath,
            byte[]? data = null,
            int depth = 0)
        {
            if (depth > 10) // Safety limit for recursion
            {
                throw new InvalidOperationException($"Maximum compression nesting depth (10) exceeded for {componentName}");
            }

            string? tempPath = null;
            try
            {
                // If we have raw data, write it to a temp file with proper extension
                if (data != null)
                {
                    string extension = Path.GetExtension(componentName);
                    if (string.IsNullOrEmpty(extension))
                    {
                        // Try to detect type from header
                        var detectPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                        try
                        {
                            await File.WriteAllBytesAsync(detectPath, data);
                            var type = await CompressedFileParser.DetectCompressionTypeAsync(detectPath);
                            extension = GetExtensionForType(type);
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
                }

                // Check if this component is itself compressed
                bool isCompressed = CompressedFileParser.IsCompressedFile(filePath) &&
                                  await CompressedFileParser.DetectCompressionTypeAsync(filePath) != CompressionType.Unknown;

                if (isCompressed)
                {
                    // Extract and recursively process nested components
                    var components = await CompressedFileParser.ParseComponentsAsync(filePath);
                    foreach (var component in components)
                    {
                        string nestedName = component.Item1;
                        byte[] nestedData = component.Item2;
                        
                        string nestedPath = string.IsNullOrEmpty(parentPath)
                            ? nestedName
                            : $"{parentPath}/{nestedName}";

                        await ProcessCompressedComponentAsync(
                            filePath: "", // We'll pass data instead
                            componentName: nestedName,
                            componentInfos,
                            parentPath: nestedPath,
                            data: nestedData,
                            depth: depth + 1);
                    }
                }
                else
                {
                    // Reached uncompressed content - apply CDC chunking
                    byte[] finalData = data ?? await File.ReadAllBytesAsync(filePath);
                    var chunks = await ChunkDataAsync(finalData);
                    
                    var combinedHash = HashUtils.CalculateSha256(
                        System.Text.Encoding.UTF8.GetBytes(string.Join("", chunks.Select(c => c.Hash)))
                    );

                    string fullPath = string.IsNullOrEmpty(parentPath)
                        ? componentName
                        : $"{parentPath}/{componentName}";

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
                if (tempPath != null && File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private async Task<List<ChunkInfo>> ChunkDataAsync(byte[] data)
        {
            var fastCdc = new FastCdc();
            var chunks = new List<ChunkInfo>();
            var chunkEnds = fastCdc.Chunk(data);
            int start = 0;

            foreach (var end in chunkEnds)
            {
                int chunkSize = end - start;
                var chunkData = data.AsSpan(start, chunkSize).ToArray();
                var chunkHash = HashUtils.CalculateSha256(chunkData);
                var blobName = $"components/{chunkHash}";

                var blobClient = _containerClient.GetBlobClient(blobName);
                long compressedSize;

                if (!await blobClient.ExistsAsync())
                {
                    var compressedData = await _compressionService.CompressAsync(chunkData);
                    using var compressedStream = new MemoryStream(compressedData);
                    await blobClient.UploadAsync(compressedStream, overwrite: true);
                    compressedSize = compressedData.Length;
                }
                else
                {
                    var props = await blobClient.GetPropertiesAsync();
                    compressedSize = props.Value.ContentLength;
                }

                chunks.Add(new ChunkInfo
                {
                    Hash = chunkHash,
                    Offset = start,
                    Size = compressedSize,
                    BlobName = blobName,
                    IsCompressed = true
                });

                start = end;
            }

            return chunks;
        }

        private static string GetExtensionForType(CompressionType type) => type switch
        {
            CompressionType.Zip => ".zip",
            CompressionType.GZip => ".gz",
            CompressionType.SqlServerBak => ".bak",
            _ => ""
        };
    }
}