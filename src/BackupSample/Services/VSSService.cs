using System;
using System.Management;
using System.Threading.Tasks;

namespace BackupSample.Services
{
    public class VSSService
    {
        public async Task<(string snapshotPath, string snapshotId)> CreateSnapshotAsync(string volumePath)
        {
            string? snapshotId = null;

            await Task.Run(() =>
            {
                var scope = new ManagementScope(@"\\.\root\cimv2");
                var managementClass = new ManagementClass(scope, new ManagementPath("Win32_ShadowCopy"), null);
                var inParams = managementClass.GetMethodParameters("Create");
                inParams["Volume"] = System.IO.Path.GetPathRoot(volumePath) + "\\";
                inParams["Context"] = "ClientAccessible";

                var outParams = managementClass.InvokeMethod("Create", inParams, null);
                snapshotId = outParams["ShadowID"]?.ToString();
            });

            if (snapshotId == null)
                throw new Exception("Failed to create VSS snapshot.");

            string snapshotPath = $@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy{snapshotId}\";
            return (snapshotPath, snapshotId);
        }

        public async Task DeleteSnapshotAsync(string snapshotId)
        {
            if (string.IsNullOrEmpty(snapshotId)) return;

            await Task.Run(() =>
            {
                var scope = new ManagementScope(@"\\.\root\cimv2");
                var query = new ObjectQuery($"SELECT * FROM Win32_ShadowCopy WHERE ID='{snapshotId}'");
                using var searcher = new ManagementObjectSearcher(scope, query);
                foreach (ManagementObject snapshot in searcher.Get())
                {
                    snapshot.Delete();
                }
            });
        }
    }
}
