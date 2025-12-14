namespace BackupSample.Services
{
    public interface IEncryptionService
    {
        Task<byte[]> EncryptAsync(byte[] data, string passphrase);
        Task<byte[]> DecryptAsync(byte[] encryptedData, string passphrase);
    }
}
