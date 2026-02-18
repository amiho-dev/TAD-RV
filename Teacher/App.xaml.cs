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
        base.OnStartup(e);

        // Parse --demo flag
        IsDemoMode = e.Args.Any(a =>
            a.Equals("--demo", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/demo", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--emulate", StringComparison.OrdinalIgnoreCase));

        // ── Show Splash ──────────────────────────────────────────────
        var splash = new SplashScreen();
        splash.Show();

        splash.SetStatus(IsDemoMode ? "Demo mode — no drivers required" : "Connecting to endpoints...");
        await Task.Delay(800);

        splash.SetStatus("Loading dashboard...");
        await Task.Delay(600);

        splash.SetStatus(IsDemoMode ? "Generating demo students..." : "Initializing WebView2...");
        await Task.Delay(600);

        // ── Launch Main Window ───────────────────────────────────────
        var mainWindow = new MainWindow(IsDemoMode);
        mainWindow.Show();

        splash.SetStatus("Ready!");
        await Task.Delay(300);
        splash.Close();

        // ── Check for updates (background, non-blocking) ────────────
        _ = CheckForUpdatesAsync(mainWindow);
    }

    private static async Task CheckForUpdatesAsync(MainWindow mainWindow)
    {
        try
        {
            Updater = new UpdateManager("teacher");
            var update = await Updater.CheckForUpdateAsync();
            if (update != null)
            {
                await mainWindow.NotifyUpdateAvailable(update.Version, update.ReleaseNotes, update.HtmlUrl);
            }
        }
        catch { /* Best effort — don't crash the app over update checks */ }
    }
}
