// ───────────────────────────────────────────────────────────────────────────
// App.xaml.cs — Application startup with elevation check
// ───────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using TADBridge.Shared;
using TADBridge.Shared.Licensing;

namespace TADDomainController;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        while (true)
        {
            var license = TADLicenseManager.EnsureLicense("dc");
            if (license.IsLicensed)
            {
                if (license.IsTrial)
                {
                    TADLicenseDialogs.ShowInfo(
                        $"Free trial active. {license.TrialDaysRemaining} day(s) remaining.\n\nDevice serial:\n{license.DeviceSerial}",
                        "TAD.RV Management Console - Trial");
                }
                break;
            }

            string? key = TADLicenseDialogs.PromptForActivationKey(
                license.DeviceSerial,
                "TAD.RV Management Console - Activation Required");

            if (string.IsNullOrWhiteSpace(key))
            {
                Shutdown();
                return;
            }

            if (TADLicenseManager.TryActivate(key, "dc", out string activationError))
            {
                TADLicenseDialogs.ShowInfo("Activation successful.", "TAD.RV Management Console");
                continue;
            }

            TADLicenseDialogs.ShowError("Activation failed: " + activationError, "TAD.RV Management Console");
        }

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
                    FileName        = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "TADDomainController.exe",
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
            return;
        }

        try
        {
            var updater = new UpdateManager("dc");
            var update = await updater.CheckForUpdateAsync();
            if (update is { IsForceUpdate: true })
            {
                bool launched = await updater.DownloadAndRunSetupAsync(update);
                if (launched)
                {
                    MessageBox.Show(
                        "A critical TAD.RV update is required and will now be installed.",
                        "TAD.RV Critical Update",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    Shutdown();
                    return;
                }
            }
        }
        catch
        {
            // Ignore update errors on startup; normal UI can continue.
        }
    }
}
