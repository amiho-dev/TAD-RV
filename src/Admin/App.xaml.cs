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

        // ── Show Splash ───────────────────────────────────────────────
        TADLogger.Info("Creating SplashScreen");
        var splash = new SplashScreen();
        splash.Show();

        splash.SetStatus(IsDemoMode ? "Demo mode — no drivers required" : "Connecting to endpoints...");
        TADLogger.Info("Splash step 1");
        await Task.Delay(800);

        splash.SetStatus("Loading dashboard...");
        TADLogger.Info("Splash step 2");
        await Task.Delay(600);

        splash.SetStatus(IsDemoMode ? "Generating demo students..." : "Initializing WebView2...");
        TADLogger.Info("Splash step 3 — WebView2 init is deferred to Loaded event");
        await Task.Delay(600);

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

        splash.SetStatus("Ready!");
        await Task.Delay(300);
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
