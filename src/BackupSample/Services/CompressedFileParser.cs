using System.IO;
using System.IO.Compression;
using BackupSample.Utils;

namespace BackupSample.Services
{
    public class CompressedFileParser
    {
        private const int BufferSize = 1024 * 1024; // 1MB read buffer
        private static readonly string[] CompressedExtensions = {
            ".bak",  // SQL Server backup
            ".zip",  // ZIP archives
            ".gz",   // GZip
            ".7z",   // 7-Zip
            ".rar"   // RAR archives
        };

        /// <summary>
        /// Check if the file is a known compressed format based on extension
        /// </summary>
        public static bool IsCompressedFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLower();
            return CompressedExtensions.Contains(extension);
        }

        /// <summary>
        /// Try to detect the compression type by reading file headers
        /// </summary>
        public static async Task<CompressionType> DetectCompressionTypeAsync(string filePath)
        {
            // Read first few bytes to check signatures
            byte[] header = new byte[8];
            using (var file = File.OpenRead(filePath))
            {
                if (file.Length < 8)
                    return CompressionType.Unknown;

                await file.ReadAsync(header.AsMemory(0, 8));
            }

            // Check signatures
            if (header[0] == 0x50 && header[1] == 0x4B) // PK.. signature
                return CompressionType.Zip;
            if (header[0] == 0x1F && header[1] == 0x8B)
                return CompressionType.GZip;
            if (IsSqlServerBackup(header))
                return CompressionType.SqlServerBak;

            return CompressionType.Unknown;
        }

        private static bool IsSqlServerBackup(byte[] header)
        {
            // SQL Server backup files typically start with a specific header
            // This is a simplified check - in practice you'd want to verify more thoroughly
            return header[0] == 0x42 && header[1] == 0x41 && header[2] == 0x4B; // "BAK"
        }

        /// <summary>
        /// Parse a compressed file into components using streaming to handle large files
        /// </summary>
        public static async Task<List<(string Name, byte[] Data, string Hash, long Size)>> ParseComponentsAsync(
            string filePath)
        {
            var components = new List<(string Name, byte[] Data, string Hash, long Size)>();
            var compressionType = await DetectCompressionTypeAsync(filePath);

            switch (compressionType)
            {
                case CompressionType.Zip:
                    return await ParseZipFileAsync(filePath);
                case CompressionType.SqlServerBak:
                    return await ParseSqlBackupAsync(filePath);
                case CompressionType.GZip:
                    return await ParseGZipFileAsync(filePath);
                default:
                    // For unknown types, treat as a single compressed blob
                    return await ParseGenericCompressedFileAsync(filePath);
            }
        }

        private static async Task<List<(string Name, byte[] Data, string Hash, long Size)>> ParseZipFileAsync(
            string filePath)
        {
            var components = new List<(string Name, byte[] Data, string Hash, long Size)>();

            try
            {
                using var zip = ZipFile.OpenRead(filePath);
                int processed = 0;
                int total = zip.Entries.Count;

                foreach (var entry in zip.Entries)
                {
                    if (entry.Length == 0) continue; // Skip directories

                    using var stream = entry.Open();
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms);
                    var data = ms.ToArray();
                    var hash = HashUtils.CalculateSha256(data);

                    components.Add((entry.FullName, data, hash, entry.Length));

                    processed++;
                }
            }
            catch (InvalidDataException)
            {
                // Not a valid ZIP file - handle error
            }

            return components;
        }

        private static async Task<List<(string Name, byte[] Data, string Hash, long Size)>> ParseSqlBackupAsync(
            string filePath)
        {
            var components = new List<(string Name, byte[] Data, string Hash, long Size)>();
            
            try
            {
                using var fileStream = File.OpenRead(filePath);
                var fileInfo = new FileInfo(filePath);
                long totalSize = fileInfo.Length;
                long processed = 0;

                // For SQL Server .bak files, we'll use FastCDC for content-defined chunking
                var fastCdc = new FastCdc();
                byte[] buffer = new byte[BufferSize];
                using var ms = new MemoryStream();

                while (true)
                {
                    int bytesRead = await fileStream.ReadAsync(buffer);
                    if (bytesRead == 0) break;

                    await ms.WriteAsync(buffer.AsMemory(0, bytesRead));
                    processed += bytesRead;
                }

                // Get the full data and chunk it
                var data = ms.ToArray();
                var chunkEnds = fastCdc.Chunk(data);
                int start = 0;

                for (int i = 0; i < chunkEnds.Count; i++)
                {
                    int end = chunkEnds[i];
                    int length = end - start;
                    var chunk = new byte[length];
                    Array.Copy(data, start, chunk, 0, length);
                    
                    var hash = HashUtils.CalculateSha256(chunk);
                    var name = $"chunk_{i}";
                    
                    components.Add((name, chunk, hash, length));
                    start = end;
                }
            }
            catch (Exception)
            {
                // Handle specific SQL backup format errors
            }

            return components;
        }

        private static async Task<List<(string Name, byte[] Data, string Hash, long Size)>> ParseGZipFileAsync(
            string filePath)
        {
            var components = new List<(string Name, byte[] Data, string Hash, long Size)>();
            
            try
            {
                using var fileStream = File.OpenRead(filePath);
                using var gzip = new GZipStream(fileStream, CompressionMode.Decompress);
                using var ms = new MemoryStream();
                
                var buffer = new byte[BufferSize];
                var fileInfo = new FileInfo(filePath);
                long totalSize = fileInfo.Length;
                long processed = 0;

                while (true)
                {
                    int bytesRead = await gzip.ReadAsync(buffer);
                    if (bytesRead == 0) break;

                    await ms.WriteAsync(buffer.AsMemory(0, bytesRead));
                    processed += bytesRead;
                }

                var data = ms.ToArray();
                var hash = HashUtils.CalculateSha256(data);
                var name = Path.GetFileNameWithoutExtension(filePath);

                components.Add((name, data, hash, data.Length));
            }
            catch (Exception)
            {
                // Handle GZip-specific errors
            }

            return components;
        }

        private static async Task<List<(string Name, byte[] Data, string Hash, long Size)>> ParseGenericCompressedFileAsync(
            string filePath)
        {
            var components = new List<(string Name, byte[] Data, string Hash, long Size)>();
            
            try
            {
                using var fileStream = File.OpenRead(filePath);
                using var ms = new MemoryStream();
                
                var buffer = new byte[BufferSize];
                var fileInfo = new FileInfo(filePath);
                long totalSize = fileInfo.Length;
                long processed = 0;

                while (true)
                {
                    int bytesRead = await fileStream.ReadAsync(buffer);
                    if (bytesRead == 0) break;

                    await ms.WriteAsync(buffer.AsMemory(0, bytesRead));
                    processed += bytesRead;
                }

                var data = ms.ToArray();
                var hash = HashUtils.CalculateSha256(data);
                var name = Path.GetFileName(filePath);

                components.Add((name, data, hash, data.Length));
            }
            catch (Exception)
            {
                // Handle generic compression errors
            }

            return components;
        }
    }

    public enum CompressionType
    {
        Unknown,
        Zip,
        GZip,
        SqlServerBak,
        SevenZip,
        Rar
    }
}