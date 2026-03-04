// ───────────────────────────────────────────────────────────────────────────
// UpdaterWindow.xaml.cs — Software update dialog with Markdown release notes
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Renders GitHub release notes (Markdown) into WPF FlowDocument with proper
// heading, bold, bullet, and code formatting. Dark-themed to match the app.
// ───────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using TADBridge.Shared;

namespace TADAdmin;

public partial class UpdaterWindow : Window
{
    private readonly UpdateInfo _update;
    private readonly UpdateManager _manager;
    private string? _downloadedPath;
    private double _progressBarMaxWidth;

    // ── Dark palette brushes ──────────────────────────────────────────
    private static readonly Brush TextBrush     = new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xD9));
    private static readonly Brush HeadingBrush  = new SolidColorBrush(Color.FromRgb(0x58, 0xA6, 0xFF));
    private static readonly Brush SubHeadBrush  = new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xD9));
    private static readonly Brush BoldBrush     = new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3));
    private static readonly Brush DimBrush      = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E));
    private static readonly Brush CodeBrush     = new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3));
    private static readonly Brush CodeBgBrush   = new SolidColorBrush(Color.FromRgb(0x21, 0x26, 0x2D));
    private static readonly Brush BulletBrush   = new SolidColorBrush(Color.FromRgb(0x58, 0xA6, 0xFF));

    public UpdaterWindow(UpdateInfo update, UpdateManager manager)
    {
        InitializeComponent();
        _update  = update;
        _manager = manager;

        TxtHeadline.Text    = $"Version {update.Version} is available";
        TxtVersionLine.Text = $"Current: {manager.CurrentVersion}  →  New: {update.Version}  ·  {update.PublishedAt:yyyy-MM-dd}";

        var doc = ParseMarkdown(update.ReleaseNotes);
        ReleaseNotesViewer.Document = doc;
    }

    // ─── Title bar drag ──────────────────────────────────────────────

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    // ─── Buttons ─────────────────────────────────────────────────────

    private void BtnLater_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnOpenGithub_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_update.HtmlUrl))
            try { Process.Start(new ProcessStartInfo(_update.HtmlUrl) { UseShellExecute = true }); }
            catch { }
    }

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadedPath != null) { ApplyAndRestart(); return; }

        // Measure the progress bar container width once
        _progressBarMaxWidth = PrgFill.Parent is FrameworkElement parent
            ? parent.ActualWidth > 0 ? parent.ActualWidth : 600
            : 600;

        SetDownloadingState(true);
        _manager.OnDownloadProgress += OnProgress;

        try
        {
            TxtStatus.Text = $"Downloading {_update.AssetName}…";
            _downloadedPath = await _manager.DownloadUpdateAsync(_update);

            if (_downloadedPath == null)
            {
                TxtStatus.Text = "⚠  Download failed — please try again or download manually from GitHub.";
                SetDownloadingState(false);
                return;
            }

            PrgFill.Width = _progressBarMaxWidth;
            TxtProgressPct.Text = "100 %";
            TxtStatus.Text = $"Downloaded to: {_downloadedPath}";
            BtnDownload.Content = "✔  Install Update & Restart";
            SetDownloadingState(false);
        }
        finally { _manager.OnDownloadProgress -= OnProgress; }
    }

    private void ApplyAndRestart()
    {
        if (_downloadedPath == null) return;

        TxtStatus.Text = "Launching updater…";
        BtnDownload.IsEnabled = false;
        BtnLater.IsEnabled    = false;

        // Spawn the external TAD-Update.exe process which waits for us to exit,
        // then runs the downloaded Setup EXE with --install.
        // This avoids the "file in use" problem when overwriting running binaries.
        string updaterExe = System.IO.Path.Combine(AppContext.BaseDirectory, "TAD-Update.exe");

        if (!System.IO.File.Exists(updaterExe))
        {
            TxtStatus.Text = "⚠  TAD-Update.exe not found in install directory. Please update manually.";
            BtnDownload.IsEnabled = true;
            BtnLater.IsEnabled    = true;
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = updaterExe,
                Arguments       = $"--apply \"{_downloadedPath}\" {Environment.ProcessId}",
                UseShellExecute = false,
                CreateNoWindow  = false,
            });

            // Exit the app so the updater can replace our binaries
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"⚠  Could not launch updater: {ex.Message}";
            BtnDownload.IsEnabled = true;
            BtnLater.IsEnabled    = true;
        }
    }

    // ─── Progress ─────────────────────────────────────────────────────

    private void OnProgress(long downloaded, long total)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (total > 0)
            {
                double pct = (double)downloaded / total;
                PrgFill.Width = pct * _progressBarMaxWidth;
                TxtProgressPct.Text = $"{pct * 100:F0} %  ({FormatBytes(downloaded)} / {FormatBytes(total)})";
            }
            else
            {
                TxtProgressPct.Text = FormatBytes(downloaded);
            }
        });
    }

    private void SetDownloadingState(bool downloading)
    {
        BtnDownload.IsEnabled = !downloading;
        BtnLater.IsEnabled    = !downloading;
        if (downloading) ProgressPanel.Visibility = Visibility.Visible;
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024        => $"{bytes} B",
        < 1048576     => $"{bytes / 1024.0:F1} KB",
        _             => $"{bytes / 1048576.0:F1} MB"
    };

    // ─── Markdown → FlowDocument ──────────────────────────────────────
    //
    // Light parser that handles: ## headings, ### sub-headings,
    // - bullet lists, **bold**, `inline code`, blank lines.

    private static FlowDocument ParseMarkdown(string? markdown)
    {
        var doc = new FlowDocument
        {
            PagePadding  = new Thickness(12),
            FontFamily   = new FontFamily("Segoe UI"),
            FontSize     = 12.5,
            Foreground   = TextBrush,
            LineHeight   = 20
        };

        if (string.IsNullOrWhiteSpace(markdown))
        {
            doc.Blocks.Add(new Paragraph(new Run("(No release notes provided)") { Foreground = DimBrush }));
            return doc;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        List? currentList = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            // ── ## Heading
            if (line.StartsWith("## "))
            {
                FlushList(doc, ref currentList);
                var p = new Paragraph
                {
                    Margin     = new Thickness(0, 12, 0, 4),
                    FontSize   = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = HeadingBrush,
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3D)),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding         = new Thickness(0, 0, 0, 4)
                };
                AddFormattedInlines(p.Inlines, line[3..]);
                doc.Blocks.Add(p);
            }
            // ── ### Sub-heading
            else if (line.StartsWith("### "))
            {
                FlushList(doc, ref currentList);
                var p = new Paragraph
                {
                    Margin     = new Thickness(0, 10, 0, 2),
                    FontSize   = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = SubHeadBrush
                };
                AddFormattedInlines(p.Inlines, line[4..]);
                doc.Blocks.Add(p);
            }
            // ── - Bullet item
            else if (line.TrimStart().StartsWith("- "))
            {
                currentList ??= new List
                {
                    MarkerStyle  = TextMarkerStyle.Disc,
                    Margin       = new Thickness(8, 2, 0, 2),
                    Padding      = new Thickness(12, 0, 0, 0),
                    Foreground   = BulletBrush
                };
                var indent = line.Length - line.TrimStart().Length;
                var text = line.TrimStart()[2..];
                var para = new Paragraph { Foreground = TextBrush, Margin = new Thickness(0, 1, 0, 1) };
                AddFormattedInlines(para.Inlines, text);

                if (indent >= 2 && currentList.ListItems.Count > 0)
                {
                    // Nested bullet — add as sub-list to last item
                    var parent = currentList.ListItems.LastListItem;
                    var subList = new List
                    {
                        MarkerStyle = TextMarkerStyle.Circle,
                        Margin      = new Thickness(8, 0, 0, 0),
                        Foreground  = DimBrush
                    };
                    subList.ListItems.Add(new ListItem(para));
                    parent?.Blocks.Add(subList);
                }
                else
                {
                    currentList.ListItems.Add(new ListItem(para));
                }
            }
            // ── Blank line
            else if (string.IsNullOrWhiteSpace(line))
            {
                FlushList(doc, ref currentList);
            }
            // ── Regular text
            else
            {
                FlushList(doc, ref currentList);
                var p = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
                AddFormattedInlines(p.Inlines, line);
                doc.Blocks.Add(p);
            }
        }

        FlushList(doc, ref currentList);
        return doc;
    }

    private static void FlushList(FlowDocument doc, ref List? list)
    {
        if (list == null) return;
        doc.Blocks.Add(list);
        list = null;
    }

    /// <summary>Parse **bold** and `code` spans within a line.</summary>
    private static void AddFormattedInlines(InlineCollection inlines, string text)
    {
        // Pattern: **bold**, `code`
        var regex = new Regex(@"(\*\*(.+?)\*\*)|(`(.+?)`)");
        int pos = 0;

        foreach (Match m in regex.Matches(text))
        {
            // Text before match
            if (m.Index > pos)
                inlines.Add(new Run(text[pos..m.Index]));

            if (m.Groups[1].Success) // **bold**
            {
                inlines.Add(new Run(m.Groups[2].Value)
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = BoldBrush
                });
            }
            else if (m.Groups[3].Success) // `code`
            {
                inlines.Add(new Run(m.Groups[4].Value)
                {
                    FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                    FontSize   = 11.5,
                    Foreground = CodeBrush,
                    Background = CodeBgBrush
                });
            }

            pos = m.Index + m.Length;
        }

        // Remaining text
        if (pos < text.Length)
            inlines.Add(new Run(text[pos..]));
    }
}
