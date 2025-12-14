using System.Diagnostics;
using System.IO;

namespace BackupSample.Services
{
    /// <summary>
    /// Service for creating and managing virtual drives using Windows SUBST command
    /// </summary>
    public class VirtualDriveService
    {
        /// <summary>
        /// Create a virtual drive from a folder path
        /// </summary>
        public bool CreateVirtualDrive(string driveLetter, string folderPath)
        {
            try
            {
                // Ensure folder exists
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                // Delete existing drive if present
                DeleteVirtualDrive(driveLetter);

                // Create virtual drive: subst Z: C:\MyFolder
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "subst",
                        Arguments = $"{driveLetter}: \"{folderPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                process.WaitForExit();

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating virtual drive: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Delete a virtual drive
        /// </summary>
        public bool DeleteVirtualDrive(string driveLetter)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "subst",
                        Arguments = $"{driveLetter}: /D",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                process.WaitForExit();

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if a drive letter is available
        /// </summary>
        public bool IsDriveAvailable(string driveLetter)
        {
            var drives = DriveInfo.GetDrives()
                .Select(d => d.Name[0].ToString().ToUpper())
                .ToHashSet();

            return !drives.Contains(driveLetter.ToUpper());
        }

        /// <summary>
        /// Find an available drive letter
        /// </summary>
        public string? GetAvailableDriveLetter()
        {
            var usedDrives = DriveInfo.GetDrives()
                .Select(d => d.Name[0])
                .ToHashSet();

            // Check from Z to D (avoid C and below)
            for (char letter = 'Z'; letter >= 'D'; letter--)
            {
                if (!usedDrives.Contains(letter))
                    return letter.ToString();
            }

            return null;
        }

        /// <summary>
        /// List all virtual drives created with SUBST
        /// </summary>
        public Dictionary<string, string> ListVirtualDrives()
        {
            var drives = new Dictionary<string, string>();

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "subst",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // Parse output: "Z:\: => C:\MyFolder"
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { "=>" }, StringSplitOptions.TrimEntries);
                    if (parts.Length == 2)
                    {
                        var driveLetter = parts[0].Replace("\\:", "").Replace(":", "").Trim();
                        drives[driveLetter] = parts[1].Trim();
                    }
                }
            }
            catch { }

            return drives;
        }

        /// <summary>
        /// Open Windows Explorer at the specified drive
        /// </summary>
        public void OpenExplorer(string driveLetter)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"{driveLetter}:\\",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening explorer: {ex.Message}");
            }
        }
    }
}
