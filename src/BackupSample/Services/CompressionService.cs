using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace BackupSample.Services
{
    public class CompressionService
    {
        public async Task<byte[]> CompressAsync(byte[] data)
        {
            using var output = new MemoryStream();
            using var gzip = new GZipStream(output, CompressionLevel.Optimal);
            await gzip.WriteAsync(data);
            await gzip.FlushAsync();
            return output.ToArray();
        }

        public async Task<byte[]> DecompressAsync(byte[] compressedData)
        {
            using var input = new MemoryStream(compressedData);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            await gzip.CopyToAsync(output);
            return output.ToArray();
        }
    }

    public class CalculateHashService
    {
        private string CalculateHash(byte[] data)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(data);
            return Convert.ToHexString(hashBytes);
        }
    }
}
