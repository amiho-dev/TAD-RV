// ───────────────────────────────────────────────────────────────────────────
// App.xaml.cs — Teacher Controller entry point
//
// (C) 2026 TAD Europe — https://tad-it.eu
// TAD.RV — The Greater Brother of the mighty te.comp NET.FX
//
// Shows a branded splash screen, then launches MainWindow.
// Supports --demo flag for full operation without kernel drivers.
// ───────────────────────────────────────────────────────────────────────────

using System.Windows;
using System.Windows.Threading;
using TadBridge.Shared;

namespace TadTeacher;

public partial class App : Application
{
    public static bool IsDemoMode { get; private set; }
    public static UpdateManager? Updater { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // ── Logging must come first ───────────────────────────────────
        TadLogger.Init();
        TadLogger.Info("OnStartup entered");

        // ── Global crash traps ────────────────────────────────────────
        AppDomain.CurrentDomain.UnhandledException += (_, uea) =>
        {
            var ex = uea.ExceptionObject as Exception
                  ?? new Exception(uea.ExceptionObject?.ToString() ?? "unknown");
            TadLogger.Exception(ex, "AppDomain.UnhandledException (IsTerminating=" + uea.IsTerminating + ")");
            ShowCrashDialog(ex.ToString());
        };

        Current.DispatcherUnhandledException += (_, dea) =>
        {
            TadLogger.Exception(dea.Exception, "Dispatcher.UnhandledException");
            ShowCrashDialog(dea.Exception.ToString());
            dea.Handled = true;   // keep WPF pump alive so the dialog can be read
        };

        TaskScheduler.UnobservedTaskException += (_, tea) =>
        {
            TadLogger.Exception(tea.Exception, "TaskScheduler.UnobservedTaskException");
            tea.SetObserved();
        };

        base.OnStartup(e);

        // ── Parse --demo flag ─────────────────────────────────────────
        IsDemoMode = e.Args.Any(a =>
            a.Equals("--demo", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/demo", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--emulate", StringComparison.OrdinalIgnoreCase));

        TadLogger.Info($"IsDemoMode={IsDemoMode}  args=[{string.Join(", ", e.Args)}]");

        // ── Show Splash ───────────────────────────────────────────────
        TadLogger.Info("Creating SplashScreen");
        var splash = new SplashScreen();
        splash.Show();

        splash.SetStatus(IsDemoMode ? "Demo mode — no drivers required" : "Connecting to endpoints...");
        TadLogger.Info("Splash step 1");
        await Task.Delay(800);

        splash.SetStatus("Loading dashboard...");
        TadLogger.Info("Splash step 2");
        await Task.Delay(600);

        splash.SetStatus(IsDemoMode ? "Generating demo students..." : "Initializing WebView2...");
        TadLogger.Info("Splash step 3 — WebView2 init is deferred to Loaded event");
        await Task.Delay(600);

        // ── Launch Main Window ────────────────────────────────────────
        TadLogger.Info("Creating MainWindow");
        MainWindow mainWindow;
        try
        {
            mainWindow = new MainWindow(IsDemoMode);
        }
        catch (Exception ex)
        {
            TadLogger.Exception(ex, "MainWindow constructor FAILED");
            ShowCrashDialog($"MainWindow failed to construct:\n\n{ex}");
            Shutdown(1);
            return;
        }

        TadLogger.Info("Calling mainWindow.Show()");
        mainWindow.Show();
        TadLogger.Info("mainWindow.Show() returned — window should be visible");

        splash.SetStatus("Ready!");
        await Task.Delay(300);
        splash.Close();
        TadLogger.Info("Splash closed");

        // ── Check for updates (background, non-blocking) ──────────────
        _ = CheckForUpdatesAsync(mainWindow);
    }

    internal static void ShowCrashDialog(string details)
    {
        try
        {
            var logHint = TadLogger.LogPath.Length > 0
                ? $"\n\nDiagnostic log:\n  {TadLogger.LogPath}\n  %TEMP%\\TadTeacher_latest.log"
                : "";

            MessageBox.Show(
                $"TAD.RV Teacher encountered an unexpected error.\n\n{details}{logHint}",
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
            TadLogger.Info("Checking for updates");
            Updater = new UpdateManager("teacher");
            var update = await Updater.CheckForUpdateAsync();
            if (update != null)
            {
                TadLogger.Info($"Update available: v{update.Version}");
                await mainWindow.NotifyUpdateAvailable(update.Version, update.ReleaseNotes, update.HtmlUrl);
            }
            else
            {
                TadLogger.Info("Already up to date");
            }
        }
        catch (Exception ex)
        {
            TadLogger.Warn($"Update check failed: {ex.Message}");
        }
    }
}
