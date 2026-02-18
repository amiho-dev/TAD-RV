// ───────────────────────────────────────────────────────────────────────────
// App.xaml.cs — Application startup with elevation check
// ───────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace TadConsole;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Check if running with administrator privileges
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            var result = MessageBox.Show(
                "TAD.RV Management Console requires Administrator privileges.\n\n" +
                "Click OK to restart elevated, or Cancel to exit.",
                "TAD.RV — Elevation Required",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.OK)
            {
                // Relaunch with elevation
                var proc = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName        = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "TadConsole.exe",
                    Verb            = "runas"
                };

                try
                {
                    Process.Start(proc);
                }
                catch
                {
                    // User declined UAC
                }
            }

            Shutdown();
        }
    }
}
