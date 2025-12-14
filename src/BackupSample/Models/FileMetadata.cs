using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BackupSample.Models
{

    public partial class FileMetadata : ObservableObject
    {
        public string FilePath { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public string Hash { get; set; } = string.Empty;
        public List<ChunkInfo> Chunks { get; set; } = new();
        public List<ComponentDetail> Components { get; set; } = new();
        public FileAttributes Attributes { get; set; }
        public DateTime LastBackup { get; set; }
        public BackupType BackupType { get; set; }
        public FileStructureType StructureType { get; set; } = FileStructureType.ChunkBased;
        public string RelativePath { get; set; } = string.Empty;
        public string? Acl { get; set; } = null;

        [ObservableProperty]
        private bool isSelected;
    }

    public class ChunkInfo
    {
        public string Hash { get; set; } = string.Empty;
        public long Offset { get; set; }
        public long Size { get; set; }
        public bool IsCompressed { get; set; }
        public string BlobName { get; set; } = string.Empty;
        public bool IsEncrypted { get; set; }
        public bool IsUpload { get; set; }

    }

    public class ComponentInfo
    {
        public string Name { get; set; } = string.Empty; // e.g., "word/document.xml"
        public string Hash { get; set; } = string.Empty;
        public long Size { get; set; }
        public bool IsCompressed { get; set; }
        public string BlobName { get; set; } = string.Empty;
    }

    public class ComponentDetail
    {
        public string Name { get; set; } = string.Empty; // e.g., "word/document.xml"
        public string Hash { get; set; } = string.Empty;
        public long Size { get; set; }
        public bool IsCompressed { get; set; }
        public List<ChunkInfo> Chunks { get; set; } = [];
    }

    public class BackupManifest
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public BackupType Type { get; set; }
        public BackupTargetType Target { get; set; }
        public List<FileMetadata> Files { get; set; } = new();
        public long TotalSize { get; set; }
        public long CompressedSize { get; set; }
        public long BackupSize { get; set; }
        public Dictionary<string, string> FolderAcls { get; set; } = new();
    }

    public class VolumeInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }

    public enum BackupType
    {
        Full,
        Incremental
    }

    public enum FileStructureType
    {
        ChunkBased,
        StructureBased,
        FolderOnly
    }
    public enum RecoveryOption
    {
        Overwrite,
        Skip,
        Rename
    }
    public enum BackupTargetType
    {
        File,
        Folder,
        Volume
    }
}
