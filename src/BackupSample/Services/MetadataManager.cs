using System.IO;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BackupSample.Models;

public class MetadataManager
{
    private readonly BlobContainerClient _containerClient;

    public MetadataManager(string connectionString, string containerName)
    {
        _containerClient = new BlobContainerClient(connectionString, containerName);
        _containerClient.CreateIfNotExists();
    }

    private string GetBlobName(BackupManifest manifest)
        => $"manifests/backup_{manifest.Timestamp:yyyyMMdd_HHmms}_{manifest.Id}.json";

    public async Task SaveBackupManifestAsync(BackupManifest manifest)
    {
        var blobName = GetBlobName(manifest);
        var blobClient = _containerClient.GetBlobClient(blobName);

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(manifest, options);

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await blobClient.UploadAsync(stream, overwrite: true);
    }

    public async Task<BackupManifest?> GetLastBackupManifestAsync()
    {
        BlobItem? latestBlob = null;

        await foreach (var blobItem in _containerClient.GetBlobsAsync(prefix: "manifests/"))
        {
            if (latestBlob == null ||
                (blobItem.Properties.CreatedOn.HasValue && 
                 latestBlob.Properties.CreatedOn.HasValue &&
                 blobItem.Properties.CreatedOn.Value > latestBlob.Properties.CreatedOn.Value))
            {
                latestBlob = blobItem;
            }
        }

        if (latestBlob != null)
        {
            var blobClient = _containerClient.GetBlobClient(latestBlob.Name);
            var download = await blobClient.DownloadContentAsync();
            var json = download.Value.Content.ToString();
            return JsonSerializer.Deserialize<BackupManifest>(json);
        }

        return null;
    }

    public async Task<List<BackupManifest>> GetAllBackupManifestsAsync()
    {
        var manifests = new List<BackupManifest>();

        await foreach (var blobItem in _containerClient.GetBlobsAsync(prefix: "manifests/"))
        {
            var blobClient = _containerClient.GetBlobClient(blobItem.Name);
            try
            {
                var download = await blobClient.DownloadContentAsync();
                var json = download.Value.Content.ToString();
                var manifest = JsonSerializer.Deserialize<BackupManifest>(json);
                if (manifest != null) manifests.Add(manifest);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading blob {blobItem.Name}: {ex.Message}");
            }
        }

        return manifests.OrderByDescending(m => m.Timestamp).ToList();
    }
}

