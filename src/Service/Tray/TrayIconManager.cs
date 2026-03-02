// ───────────────────────────────────────────────────────────────────────────
// TrayIconManager.cs — System tray icon for interactive / emulation mode
//
// When TadBridgeService runs interactively (--emulate / --demo), this shows
// a tray icon so the user knows the service is active.  In true Windows
// Service mode this class is not registered.
// ───────────────────────────────────────────────────────────────────────────

using System.Drawing;
using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TadBridge.Tray;

public sealed class TrayIconManager : IHostedService, IDisposable
{
    private readonly ILogger<TrayIconManager> _log;
    private readonly IHostApplicationLifetime _lifetime;

    private System.Windows.Forms.NotifyIcon?   _notifyIcon;
    private System.Windows.Forms.ContextMenuStrip? _menu;
    private Thread? _staThread;

    public TrayIconManager(ILogger<TrayIconManager> log, IHostApplicationLifetime lifetime)
    {
        _log      = log;
        _lifetime = lifetime;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // NotifyIcon requires an STA thread with a message pump
        _staThread = new Thread(RunTray) { IsBackground = true, Name = "TrayIcon" };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();

        return Task.CompletedTask;
    }

    private void RunTray()
    {
        try
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            _menu = new System.Windows.Forms.ContextMenuStrip();
            _menu.Items.Add("TAD.RV Bridge Service (Emulation)", null, null!).Enabled = false;
            _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            _menu.Items.Add("Exit", null, (_, _) =>
            {
                _log.LogInformation("[TRAY] User requested exit from tray icon");
                _lifetime.StopApplication();
                System.Windows.Forms.Application.ExitThread();
            });

            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Text             = "TAD.RV Bridge Service — Emulation Mode",
                ContextMenuStrip = _menu,
                Visible          = true,
                Icon             = LoadIcon()
            };

            _log.LogInformation("[TRAY] System tray icon active");

            // Run a message loop so the icon stays alive
            System.Windows.Forms.Application.Run();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[TRAY] Tray icon could not start (headless environment?)");
        }
    }

    private static Icon LoadIcon()
    {
        // Try to load the embedded icon resource; fall back to the default app icon
        var asm = Assembly.GetExecutingAssembly();
        var loc = Path.Combine(AppContext.BaseDirectory, "logo.ico");

        if (File.Exists(loc))
            return new Icon(loc);

        // Fallback: extract from the running exe
        try
        {
            return Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
                ?? SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _menu?.Dispose();
        _menu = null;

        if (_staThread?.IsAlive == true)
        {
            try { System.Windows.Forms.Application.ExitThread(); } catch { }
        }
    }
}
