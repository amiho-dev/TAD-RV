// ───────────────────────────────────────────────────────────────────────────
// ClientWindow.cs — WPF-based client info window for the TAD.RV tray helper
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Built programmatically (no XAML) so it can live in the Worker SDK project.
// Dark-themed to match the Admin panel aesthetic. Compatible with Win10+.
// ───────────────────────────────────────────────────────────────────────────

using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TADBridge.Tray;

/// <summary>
/// Provides WPF-based dialogs for the client tray helper.
/// Call methods from an STA thread with a WPF Dispatcher running.
/// </summary>
internal static class ClientWindow
{
    // ── Color Palette ────────────────────────────────────────────────────
    private static readonly SolidColorBrush BgPrimary   = Freeze(new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17)));
    private static readonly SolidColorBrush BgSecondary = Freeze(new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x22)));
    private static readonly SolidColorBrush BgTertiary  = Freeze(new SolidColorBrush(Color.FromRgb(0x21, 0x26, 0x2D)));
    private static readonly SolidColorBrush BorderClr   = Freeze(new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3D)));
    private static readonly SolidColorBrush TextPrimary = Freeze(new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xD9)));
    private static readonly SolidColorBrush TextSecondary = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)));
    private static readonly SolidColorBrush AccentBlue  = Freeze(new SolidColorBrush(Color.FromRgb(0x58, 0xA6, 0xFF)));
    private static readonly SolidColorBrush AccentGreen = Freeze(new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50)));
    private static readonly SolidColorBrush AccentRed   = Freeze(new SolidColorBrush(Color.FromRgb(0xF8, 0x51, 0x49)));
    private static readonly SolidColorBrush White       = Freeze(new SolidColorBrush(Colors.White));

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    // ── Show Client Info ─────────────────────────────────────────────────
    public static void ShowInfo()
    {
        var win = CreateWindow("TAD.RV — Client Info", 480, 520);

        var root = new StackPanel { Margin = new Thickness(24) };

        // Title
        root.Children.Add(MakeHeader("TAD.RV Client"));

        // Version
        string version = GetVersion();
        root.Children.Add(MakeBadge($"v{version}"));

        root.Children.Add(MakeSpacer(16));

        // Service status
        string svcStatus = GetServiceStatus();
        root.Children.Add(MakeSectionTitle("Service"));
        root.Children.Add(MakeRow("Status", svcStatus, svcStatus == "Running" ? AccentGreen : AccentRed));
        root.Children.Add(MakeRow("TCP 17420", IsPortListening(17420) ? "Listening" : "Not listening"));

        root.Children.Add(MakeSpacer(12));

        // System
        root.Children.Add(MakeSectionTitle("System"));
        root.Children.Add(MakeRow("Machine", Environment.MachineName));
        root.Children.Add(MakeRow("User", $"{Environment.UserDomainName}\\{Environment.UserName}"));
        root.Children.Add(MakeRow("OS", Environment.OSVersion.ToString()));
        root.Children.Add(MakeRow(".NET", Environment.Version.ToString()));

        root.Children.Add(MakeSpacer(12));

        // Network
        root.Children.Add(MakeSectionTitle("Network"));
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                var addrs = iface.GetIPProperties().UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString());
                string ips = string.Join(", ", addrs);
                if (!string.IsNullOrEmpty(ips))
                    root.Children.Add(MakeRow(iface.Name, ips));
            }
        }
        catch { root.Children.Add(MakeRow("Error", "Could not enumerate")); }

        root.Children.Add(MakeSpacer(20));

        // Close button
        var btn = MakeButton("Close", AccentBlue);
        btn.Click += (_, _) => win.Close();
        root.Children.Add(btn);

        var scroll = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        win.Content = scroll;
        win.ShowDialog();
    }

    // ── Show Diagnostics ─────────────────────────────────────────────────
    public static void ShowDiagnostics()
    {
        var win = CreateWindow("TAD.RV — Diagnostics", 560, 500);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══ TAD.RV Client Diagnostics ═══");
        sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Version:   {GetVersion()}");
        sb.AppendLine();
        sb.AppendLine("── Service ──");
        sb.AppendLine($"  Status:     {GetServiceStatus()}");
        sb.AppendLine($"  TCP 17420:  {(IsPortListening(17420) ? "LISTENING" : "not listening")}");
        sb.AppendLine();
        sb.AppendLine("── System ──");
        sb.AppendLine($"  Machine:    {Environment.MachineName}");
        sb.AppendLine($"  User:       {Environment.UserDomainName}\\{Environment.UserName}");
        sb.AppendLine($"  OS:         {Environment.OSVersion}");
        sb.AppendLine($"  .NET:       {Environment.Version}");
        sb.AppendLine($"  Processors: {Environment.ProcessorCount}");
        sb.AppendLine($"  Base Dir:   {AppContext.BaseDirectory}");
        sb.AppendLine();
        sb.AppendLine("── Network ──");
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                var addrs = iface.GetIPProperties().UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString());
                sb.AppendLine($"  {iface.Name}: {string.Join(", ", addrs)}");
            }
        }
        catch (Exception ex) { sb.AppendLine($"  Error: {ex.Message}"); }

        // Recent log entries
        sb.AppendLine();
        sb.AppendLine("── Recent Log ──");
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "TAD-RV", "logs");
            if (Directory.Exists(logDir))
            {
                var latestLog = Directory.GetFiles(logDir, "*.log")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (latestLog != null)
                {
                    sb.AppendLine($"  File: {latestLog}");
                    foreach (var line in File.ReadLines(latestLog).TakeLast(15))
                        sb.AppendLine($"  {line}");
                }
                else sb.AppendLine("  No log files found");
            }
            else sb.AppendLine($"  Log directory not found");
        }
        catch (Exception ex) { sb.AppendLine($"  Error: {ex.Message}"); }

        var root = new DockPanel();

        var textBox = new TextBox
        {
            Text = sb.ToString(),
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Background = BgSecondary,
            Foreground = TextPrimary,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16),
            TextWrapping = TextWrapping.NoWrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 8, 16, 12)
        };

        var copyBtn = MakeButton("Copy to Clipboard", AccentBlue);
        copyBtn.Click += (_, _) =>
        {
            Clipboard.SetText(sb.ToString());
            copyBtn.Content = "✓ Copied!";
        };
        btnPanel.Children.Add(copyBtn);

        var closeBtn = MakeButton("Close", BgTertiary);
        closeBtn.Margin = new Thickness(8, 0, 0, 0);
        closeBtn.Click += (_, _) => win.Close();
        btnPanel.Children.Add(closeBtn);

        DockPanel.SetDock(btnPanel, Dock.Bottom);
        root.Children.Add(btnPanel);
        root.Children.Add(textBox);

        win.Content = root;
        win.ShowDialog();
    }

    // ── Check for Updates ────────────────────────────────────────────────
    public static void ShowCheckForUpdates()
    {
        var win = CreateWindow("TAD.RV — Check for Updates", 420, 280);

        var root = new StackPanel { Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Center };

        root.Children.Add(MakeHeader("Check for Updates"));
        root.Children.Add(MakeSpacer(8));

        var statusText = new TextBlock
        {
            Text = "Checking for updates…",
            Foreground = TextSecondary,
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 16)
        };
        root.Children.Add(statusText);

        var resultPanel = new StackPanel();
        root.Children.Add(resultPanel);

        var closeBtn = MakeButton("Close", BgTertiary);
        closeBtn.Margin = new Thickness(0, 16, 0, 0);
        closeBtn.HorizontalAlignment = HorizontalAlignment.Center;
        closeBtn.Click += (_, _) => win.Close();
        root.Children.Add(closeBtn);

        win.Content = root;

        // Fire update check async after window loads
        win.Loaded += async (_, _) =>
        {
            try
            {
                string currentVersion = GetVersion();
                var updateManager = new TADBridge.Shared.UpdateManager("TADBridgeService");
                var update = await updateManager.CheckForUpdateAsync();

                if (update == null || !update.IsNewer)
                {
                    statusText.Text = $"You're up to date!";
                    statusText.Foreground = AccentGreen;
                    resultPanel.Children.Add(MakeRow("Installed", $"v{currentVersion}"));
                }
                else
                {
                    statusText.Text = $"Update available: v{update.Version}";
                    statusText.Foreground = AccentBlue;
                    resultPanel.Children.Add(MakeRow("Installed", $"v{currentVersion}"));
                    resultPanel.Children.Add(MakeRow("Available", $"v{update.Version}"));
                    if (!string.IsNullOrEmpty(update.HtmlUrl))
                    {
                        var linkBtn = MakeButton("View on GitHub", AccentBlue);
                        linkBtn.Margin = new Thickness(0, 12, 0, 0);
                        linkBtn.HorizontalAlignment = HorizontalAlignment.Center;
                        linkBtn.Click += (_, _) =>
                        {
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(update.HtmlUrl) { UseShellExecute = true }); }
                            catch { }
                        };
                        resultPanel.Children.Add(linkBtn);
                    }
                }
            }
            catch (Exception ex)
            {
                statusText.Text = "Update check failed";
                statusText.Foreground = AccentRed;
                resultPanel.Children.Add(MakeRow("Error", ex.Message));
            }
        };

        win.ShowDialog();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static Window CreateWindow(string title, double width, double height)
    {
        return new Window
        {
            Title = title,
            Width = width,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.CanResize,
            Background = BgPrimary,
            Foreground = TextPrimary,
            Topmost = true,
            WindowStyle = WindowStyle.SingleBorderWindow
        };
    }

    private static TextBlock MakeHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = AccentBlue,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        };
    }

    private static Border MakeBadge(string text)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x20, 0x58, 0xA6, 0xFF)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 3, 12, 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Consolas"),
                Foreground = AccentBlue
            }
        };
    }

    private static TextBlock MakeSectionTitle(string text)
    {
        return new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextSecondary,
            Margin = new Thickness(0, 0, 0, 6)
        };
    }

    private static Grid MakeRow(string label, string value, SolidColorBrush? valueBrush = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lbl = new TextBlock { Text = label, FontSize = 12, Foreground = TextSecondary };
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        var val = new TextBlock
        {
            Text = value,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = valueBrush ?? TextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(val, 1);
        grid.Children.Add(val);

        return grid;
    }

    private static FrameworkElement MakeSpacer(double height)
    {
        return new Border { Height = height };
    }

    private static Button MakeButton(string text, SolidColorBrush bg)
    {
        return new Button
        {
            Content = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(20, 8, 20, 8),
            Background = bg,
            Foreground = White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
    }

    private static string GetVersion()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var attr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            return attr?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "Unknown";
        }
        catch { return "Unknown"; }
    }

    private static string GetServiceStatus()
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController("TADBridgeService");
            return sc.Status switch
            {
                System.ServiceProcess.ServiceControllerStatus.Running      => "Running",
                System.ServiceProcess.ServiceControllerStatus.StartPending => "Starting…",
                System.ServiceProcess.ServiceControllerStatus.StopPending  => "Stopping…",
                System.ServiceProcess.ServiceControllerStatus.Stopped      => "Stopped",
                _ => sc.Status.ToString()
            };
        }
        catch { return "Unknown"; }
    }

    private static bool IsPortListening(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(ep => ep.Port == port);
        }
        catch { return false; }
    }
}
