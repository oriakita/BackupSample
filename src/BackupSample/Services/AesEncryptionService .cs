using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BackupSample.Services
{
    public class AesEncryptionService : IEncryptionService
    {
        private const int KeySize = 32; // 256-bit
        private const int IvSize = 16;  // 128-bit
        private const int SaltSize = 16;
        private const int Iterations = 100_000;

        public async Task<byte[]> EncryptAsync(byte[] data, string passphrase)
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;

            // Generate random salt + IV each time
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var iv = RandomNumberGenerator.GetBytes(IvSize);

            // Derive key from passphrase
            var key = new Rfc2898DeriveBytes(passphrase, salt, Iterations, HashAlgorithmName.SHA256).GetBytes(KeySize);

            aes.Key = key;
            aes.IV = iv;

            using var memoryStream = new MemoryStream();
            using var cryptoStream = new CryptoStream(memoryStream, aes.CreateEncryptor(), CryptoStreamMode.Write);

            await cryptoStream.WriteAsync(data);
            await cryptoStream.FlushFinalBlockAsync();

            var cipher = memoryStream.ToArray();

            // Format: [salt][iv][ciphertext]
            return Combine(salt, iv, cipher);
        }

        public async Task<byte[]> DecryptAsync(byte[] encrypted, string passphrase)
        {
            var salt = encrypted[..SaltSize];
            var iv = encrypted[SaltSize..(SaltSize + IvSize)];
            var cipher = encrypted[(SaltSize + IvSize)..];

            var key = new Rfc2898DeriveBytes(passphrase, salt, Iterations, HashAlgorithmName.SHA256).GetBytes(KeySize);

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Key = key;
            aes.IV = iv;

            using var ms = new MemoryStream(cipher);
            using var cryptoStream = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var output = new MemoryStream();

            await cryptoStream.CopyToAsync(output);
            return output.ToArray();
        }

        private static byte[] Combine(params byte[][] arrays)
        {
            var length = arrays.Sum(a => a.Length);
            var result = new byte[length];
            int offset = 0;
            foreach (var arr in arrays)
            {
                Buffer.BlockCopy(arr, 0, result, offset, arr.Length);
                offset += arr.Length;
            }
            return result;
        }
    }

}
