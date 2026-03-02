// ───────────────────────────────────────────────────────────────────────────
// UpdaterWindow.xaml.cs — Visible software update dialog for Teacher
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Shows the available version, release notes, and a download progress bar.
// Calls UpdateManager to download and apply the update, then asks the user
// to restart.
//
// The client endpoint service uses UpdateWorker which does the same thing
// silently in the background — no UI involved on the client side.
// ───────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.Windows;
using TADBridge.Shared;

namespace TADAdmin;

public partial class UpdaterWindow : Window
{
    private readonly UpdateInfo _update;
    private readonly UpdateManager _manager;
    private string? _downloadedPath;

    public UpdaterWindow(UpdateInfo update, UpdateManager manager)
    {
        InitializeComponent();
        _update  = update;
        _manager = manager;

        TxtHeadline.Text     = $"Version {update.Version} is available";
        TxtVersionLine.Text  = $"Current: {manager.CurrentVersion}   →   New: {update.Version}   •   {update.PublishedAt:yyyy-MM-dd}";
        TxtReleaseNotes.Text = string.IsNullOrWhiteSpace(update.ReleaseNotes)
            ? "(No release notes provided)"
            : update.ReleaseNotes;
    }

    // ─── Buttons ─────────────────────────────────────────────────────

    private void BtnLater_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnOpenGithub_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_update.HtmlUrl))
        {
            try { Process.Start(new ProcessStartInfo(_update.HtmlUrl) { UseShellExecute = true }); }
            catch { /* best-effort */ }
        }
    }

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadedPath != null)
        {
            // Already downloaded — apply directly
            ApplyAndRestart();
            return;
        }

        SetDownloadingState(true);

        _manager.OnDownloadProgress += OnProgress;

        try
        {
            TxtStatus.Text = $"Downloading {_update.AssetName}...";
            _downloadedPath = await _manager.DownloadUpdateAsync(_update);

            if (_downloadedPath == null)
            {
                TxtStatus.Text = "⚠  Download failed — please try again or download manually from GitHub.";
                SetDownloadingState(false);
                return;
            }

            PrgDownload.Value = 100;
            TxtProgressPct.Text = "100%";
            TxtStatus.Text = $"Downloaded to: {_downloadedPath}";
            BtnDownload.Content = "✔ Apply Update & Restart";
            SetDownloadingState(false);
        }
        finally
        {
            _manager.OnDownloadProgress -= OnProgress;
        }
    }

    private void ApplyAndRestart()
    {
        if (_downloadedPath == null) return;

        TxtStatus.Text = "Applying update...";
        BtnDownload.IsEnabled = false;
        BtnLater.IsEnabled    = false;

        bool ok = UpdateManager.ApplyUpdate(_downloadedPath);
        if (ok)
        {
            var res = MessageBox.Show(
                "Update applied successfully!\n\nRestart TAD.RV Admin now?",
                "TAD.RV Update", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (res == MessageBoxResult.Yes)
            {
                // Re-launch self and exit
                try
                {
                    var exe = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exe))
                        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                }
                catch { /* best effort */ }

                System.Windows.Application.Current.Shutdown();
            }
        }
        else
        {
            TxtStatus.Text = "⚠  Could not apply update — the binary may be in use. Try restarting manually.";
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
                double pct = (double)downloaded / total * 100;
                PrgDownload.Value   = pct;
                TxtProgressPct.Text = $"{pct:F0}%  ({FormatBytes(downloaded)} / {FormatBytes(total)})";
            }
            else
            {
                PrgDownload.IsIndeterminate = true;
                TxtProgressPct.Text = FormatBytes(downloaded);
            }
        });
    }

    private void SetDownloadingState(bool downloading)
    {
        BtnDownload.IsEnabled  = !downloading;
        BtnLater.IsEnabled     = !downloading;
        ProgressPanel.Visibility = downloading ? Visibility.Visible : Visibility.Visible; // keep visible once shown
        if (downloading) ProgressPanel.Visibility = Visibility.Visible;
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024               => $"{bytes} B",
        < 1024 * 1024        => $"{bytes / 1024.0:F1} KB",
        _                    => $"{bytes / 1048576.0:F1} MB"
    };
}
