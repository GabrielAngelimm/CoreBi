using CoreBi.Engine.Infrastructure;
using System.IO;
using CoreBi.Engine.Tools.Apps;
using CoreBi.Engine.Tools.Drivers;
using CoreBi.Engine.Monitoring.Sensors;
using System.Security.AccessControl;
using System.Security.Principal;

internal static partial class Program
{
    private static void Integrations()
    {
        Check("winget: read-only version query", () =>
        {
            string executable = AppInstaller.WingetPath;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var result = ProcessRunner.RunAsync(executable, new[] { "--version" }, timeout.Token).GetAwaiter().GetResult();
            Require(result.ExitCode == 0 && result.Output.Trim().StartsWith('v'), "Winget did not return a version");
            return new { version = result.Output.Trim(), scope = "no app installation" };
        });
        Check("GPU driver: official read-only metadata", () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var result = DriverManager.ReadGpus().First();
            if (result.Vendor == "NVIDIA") DriverManager.CheckNvidiaAsync(result, false, timeout.Token).GetAwaiter().GetResult();
            if (result.Vendor == "AMD") DriverManager.CheckAmdAsync(result, timeout.Token).GetAwaiter().GetResult();
            Require(result.Vendor.Length > 0, "GPU vendor missing");
            if (result.Vendor is "NVIDIA" or "AMD")
            {
                Require(result.DownloadUrl.Length > 0, "Official metadata did not provide a download URL: " + result.Message);
                DriverDownloadPolicy.Validate(result.DownloadUrl, result.Vendor);
            }
            return new { result.Vendor, result.Installed, result.Latest, result.Message, scope = "metadata only; no installer download or execution" };
        });
        Check("Windows Update: read-only driver search", () =>
        {
            var result = WindowsUpdateService.SearchAsync().WaitAsync(TimeSpan.FromSeconds(90)).GetAwaiter().GetResult();
            Require(result.All(x => Guid.TryParse(x.UpdateId, out _) && x.Revision >= 0), "Invalid update identity");
            return new { available = result.Count, scope = "no download, EULA acceptance or installation" };
        });
        if (SensorMonitor.IsElevated)
            Check("privileged workspace: protected ACL and owned-file roundtrip", () =>
            {
                string folder = ProtectedWorkspace.GetFolder("Startup");
                var acl = new DirectoryInfo(folder).GetAccessControl();
                var allowed = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
                Require(acl.AreAccessRulesProtected && allowed.Length == 2, "Unexpected ACL inheritance or entries");
                Require(allowed.All(x => x.IdentityReference is SecurityIdentifier sid &&
                    (sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) || sid.IsWellKnown(WellKnownSidType.LocalSystemSid))), "Unexpected ACL principal");
                string probe = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".probe");
                try { File.WriteAllText(probe, "CoreBi ACL test"); Require(File.ReadAllText(probe) == "CoreBi ACL test", "Protected write failed"); }
                finally { File.Delete(probe); }
                return new { protectedAcl = true, scope = "own temporary file only; no startup entry changed" };
            });
    }
}
