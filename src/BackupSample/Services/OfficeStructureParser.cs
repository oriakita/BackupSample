using System.IO;
using System.IO.Compression;

namespace BackupSample.Services
{
    public class StructuredFileParser
    {
        private static readonly string[] StructuredExtensions = { 
            ".docx", ".dotx", ".docm", // Word
            ".xlsx", ".xltx", ".xlsm", // Excel
            ".pptx", ".potx", ".pptm", // PowerPoint
            ".zip"
        };

        public static bool IsStructuredFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLower();
            return StructuredExtensions.Contains(extension);
        }

        public static async Task<List<(string Name, byte[] Data, string Hash, long Size)>> ParseComponentsAsync(string filePath)
        {
            var components = new List<(string Name, byte[] Data, string Hash, long Size)>();

            try
            {
                using var zip = ZipFile.OpenRead(filePath);

                foreach (var entry in zip.Entries)
                {
                    if (entry.Length == 0) continue; // Skip directories

                    using var stream = entry.Open();
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms);
                    var data = ms.ToArray();

                    //var hash = HashUtils.CalculateSha256(data);

                    components.Add((entry.FullName, data, "", 0));
                }

                return components;
            }
            catch (InvalidDataException)
            {
                // Not a valid ZIP file
                return components;
            }
        }
    }
}
