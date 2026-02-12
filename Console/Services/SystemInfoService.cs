// ───────────────────────────────────────────────────────────────────────────
// SystemInfoService.cs — System information for diagnostics & testing
// ───────────────────────────────────────────────────────────────────────────

using System.Security.Principal;

namespace TadConsole.Services;

public sealed class SystemInfoService
{
    public string Hostname => Environment.MachineName;

    public string OSVersion => Environment.OSVersion.VersionString;

    public string UserDomain => Environment.UserDomainName;

    public string CurrentUser => $"{Environment.UserDomainName}\\{Environment.UserName}";

    public string DotNetVersion => $".NET {Environment.Version}";

    public int ProcessorCount => Environment.ProcessorCount;

    public bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    public string SystemUptime
    {
        get
        {
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            return uptime.Days > 0
                ? $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m"
                : $"{uptime.Hours}h {uptime.Minutes}m";
        }
    }

    public string MemoryUsage
    {
        get
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            long mb = proc.WorkingSet64 / (1024 * 1024);
            return $"{mb} MB";
        }
    }
}
