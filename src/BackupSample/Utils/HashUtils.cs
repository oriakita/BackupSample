using System.IO;
using System.Security.Cryptography;

namespace BackupSample.Utils
{
    public static class HashUtils
    {
        public static string CalculateSha256(byte[] data)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(data);
            return Convert.ToHexString(hashBytes);
        }

        public static async Task<string> CalculateFileHashAsync(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(stream);
            return Convert.ToHexString(hashBytes);
        }
    }
}
