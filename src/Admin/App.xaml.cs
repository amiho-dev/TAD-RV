// ───────────────────────────────────────────────────────────────────────────
// App.xaml.cs — Admin Controller entry point
//
// (C) 2026 TAD Europe — https://tad-it.eu
// TAD.RV — The Greater Brother of the mighty te.comp NET.FX
//
// Shows a branded splash screen, then launches MainWindow.
// Supports --demo flag for full operation without kernel drivers.
// ───────────────────────────────────────────────────────────────────────────

using System.Windows;
using System.Windows.Threading;
using TADBridge.Shared;
using TADBridge.Shared.Licensing;

namespace TADAdmin;

public partial class App : Application
{
    public static bool IsDemoMode { get; private set; }
    public static UpdateManager? Updater { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // ── Logging must come first ───────────────────────────────────
        TADLogger.Init();
        TADLogger.Info("OnStartup entered");

        // ── Global crash traps ────────────────────────────────────────
        AppDomain.CurrentDomain.UnhandledException += (_, uea) =>
        {
            var ex = uea.ExceptionObject as Exception
                  ?? new Exception(uea.ExceptionObject?.ToString() ?? "unknown");
            TADLogger.Exception(ex, "AppDomain.UnhandledException (IsTerminating=" + uea.IsTerminating + ")");
            ShowCrashDialog(ex.ToString());
        };

        Current.DispatcherUnhandledException += (_, dea) =>
        {
            TADLogger.Exception(dea.Exception, "Dispatcher.UnhandledException");
            ShowCrashDialog(dea.Exception.ToString());
            dea.Handled = true;   // keep WPF pump alive so the dialog can be read
        };

        TaskScheduler.UnobservedTaskException += (_, tea) =>
        {
            TADLogger.Exception(tea.Exception, "TaskScheduler.UnobservedTaskException");
            tea.SetObserved();
        };

        base.OnStartup(e);

        // ── Parse --demo flag ─────────────────────────────────────────
        IsDemoMode = e.Args.Any(a =>
            a.Equals("--demo", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/demo", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--emulate", StringComparison.OrdinalIgnoreCase));

        TADLogger.Info($"IsDemoMode={IsDemoMode}  args=[{string.Join(", ", e.Args)}]");

        // Product key / 40-day trial gate
        while (true)
        {
            var license = TADLicenseManager.EnsureLicense("admin");
            if (license.IsLicensed)
            {
                if (license.IsTrial)
                {
                    TADLicenseDialogs.ShowInfo(
                        $"Free trial active. {license.TrialDaysRemaining} day(s) remaining.\n\nDevice serial:\n{license.DeviceSerial}",
                        "TAD.RV Admin - Trial");
                }
                break;
            }

            string? key = TADLicenseDialogs.PromptForActivationKey(
                license.DeviceSerial,
                "TAD.RV Admin - Activation Required");

            if (string.IsNullOrWhiteSpace(key))
            {
                Shutdown(2);
                return;
            }

            if (TADLicenseManager.TryActivate(key, "admin", out string activationError))
            {
                TADLicenseDialogs.ShowInfo("Activation successful.", "TAD.RV Admin");
                continue;
            }

            TADLicenseDialogs.ShowError("Activation failed: " + activationError, "TAD.RV Admin");
        }

        // ── Show Splash ───────────────────────────────────────────────
        TADLogger.Info("Creating SplashScreen");
        var splash = new SplashScreen();
        splash.Show();

        splash.SetProgress(8);
        splash.SetStatus("Preparing secure startup",
            IsDemoMode ? "Demo profile enabled - endpoint emulation active" : "Production profile enabled - endpoint channels pending");
        TADLogger.Info("Splash step 1");
        await Task.Delay(650);

        splash.SetProgress(34);
        splash.SetStatus("Loading control surface",
            "Building dashboard shell and command bridges");
        TADLogger.Info("Splash step 2");
        await Task.Delay(550);

        splash.SetProgress(68);
        splash.SetStatus(IsDemoMode ? "Generating demo endpoints" : "Initializing WebView2 runtime",
            IsDemoMode ? "Synthesizing classroom telemetry and live thumbnails" : "Browser runtime warm-up for the teacher dashboard");
        TADLogger.Info("Splash step 3 — WebView2 init is deferred to Loaded event");
        await Task.Delay(550);

        // ── Launch Main Window ────────────────────────────────────────
        TADLogger.Info("Creating MainWindow");
        MainWindow mainWindow;
        try
        {
            mainWindow = new MainWindow(IsDemoMode);
        }
        catch (Exception ex)
        {
            TADLogger.Exception(ex, "MainWindow constructor FAILED");
            ShowCrashDialog($"MainWindow failed to construct:\n\n{ex}");
            Shutdown(1);
            return;
        }

        TADLogger.Info("Calling mainWindow.Show()");
        mainWindow.Show();
        TADLogger.Info("mainWindow.Show() returned — window should be visible");

        splash.SetProgress(100);
        splash.SetStatus("Launch complete", "Teacher workspace is ready");
        await Task.Delay(340);
        splash.Close();
        TADLogger.Info("Splash closed");

        // ── Check for updates (background, non-blocking) ──────────────
        _ = CheckForUpdatesAsync(mainWindow);
    }

    internal static void ShowCrashDialog(string details)
    {
        try
        {
            var logHint = TADLogger.LogPath.Length > 0
                ? $"\n\nDiagnostic log:\n  {TADLogger.LogPath}\n  %TEMP%\\TADAdmin_latest.log"
                : "";

            MessageBox.Show(
                $"TAD.RV Admin encountered an unexpected error.\n\n{details}{logHint}",
                "TAD.RV — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch { /* dialog may fail during very early crashes */ }
    }

    private static async Task CheckForUpdatesAsync(MainWindow mainWindow)
    {
        try
        {
            TADLogger.Info("Checking for updates");
            Updater = new UpdateManager("admin");
            var update = await Updater.CheckForUpdateAsync();
            if (update != null)
            {
                TADLogger.Info($"Update available: v{update.Version}");

                if (update.IsForceUpdate)
                {
                    TADLogger.Warn("Critical force update detected. Bypassing consent and installing now.");
                    bool launched = await Updater.DownloadAndRunSetupAsync(update);
                    if (launched)
                    {
                        await mainWindow.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show(
                                "A critical TAD.RV update will be installed now.",
                                "TAD.RV Critical Update",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            Application.Current.Shutdown();
                        });
                        return;
                    }
                }

                // Also notify the WebView2 dashboard (shows banner in the JS UI)
                await mainWindow.NotifyUpdateAvailable(update.Version, update.ReleaseNotes, update.HtmlUrl);

                // Show the visible WPF updater window (user can download from here)
                await mainWindow.Dispatcher.InvokeAsync(() =>
                {
                    var win = new UpdaterWindow(update, Updater!) { Owner = mainWindow };
                    win.Show();
                });
            }
            else
            {
                TADLogger.Info("Already up to date");
            }
        }
        catch (Exception ex)
        {
            TADLogger.Warn($"Update check failed: {ex.Message}");
        }
    }
}
