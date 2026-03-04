// ───────────────────────────────────────────────────────────────────────────
// TrayIconManager.cs — System tray icon for interactive / emulation mode
//
// When TADBridgeService runs interactively (--emulate / --demo), this shows
// a tray icon so the user knows the service is active.  In true Windows
// Service mode this class is not registered.
// ───────────────────────────────────────────────────────────────────────────

using System.Drawing;
using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TADBridge.Tray;

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
            _menu.Items.Add("Diagnostics", null, (_, _) => ShowEmulationDiagnostics());
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

    private void ShowEmulationDiagnostics()
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("═══ TAD.RV Service Diagnostics (Emulation) ═══");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var attr = asm.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>();
                sb.AppendLine($"Version: {attr?.InformationalVersion ?? "?"}");
            }
            catch { sb.AppendLine("Version: ?"); }

            sb.AppendLine();
            sb.AppendLine("── System ──");
            sb.AppendLine($"  Machine:    {Environment.MachineName}");
            sb.AppendLine($"  User:       {Environment.UserDomainName}\\{Environment.UserName}");
            sb.AppendLine($"  OS:         {Environment.OSVersion}");
            sb.AppendLine($"  .NET:       {Environment.Version}");
            sb.AppendLine($"  Processors: {Environment.ProcessorCount}");

            sb.AppendLine();
            sb.AppendLine("── Network ──");
            try
            {
                var props = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
                sb.AppendLine($"  Hostname: {props.HostName}");
                sb.AppendLine($"  Domain:   {(string.IsNullOrEmpty(props.DomainName) ? "(none)" : props.DomainName)}");

                var listeners = props.GetActiveTcpListeners();
                sb.AppendLine($"  TCP 17420: {(listeners.Any(ep => ep.Port == 17420) ? "LISTENING" : "not listening")}");
            }
            catch (Exception ex) { sb.AppendLine($"  Error: {ex.Message}"); }

            sb.AppendLine();
            sb.AppendLine("── Installation ──");
            sb.AppendLine($"  Base Dir: {AppContext.BaseDirectory}");
            sb.AppendLine($"  Process:  {Environment.ProcessPath}");

            System.Windows.Forms.MessageBox.Show(
                sb.ToString(),
                "TAD.RV — Service Diagnostics",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[TRAY] Failed to show diagnostics");
        }
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
