using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace TADSetup;

public partial class MainWindow : Window
{
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        TitleText.Text    = InstallerCore.Config.AppDisplayName + " Setup";
        SubtitleText.Text = "v" + InstallerCore.AppVersion + "  ·  TAD Europe";
        PathBox.Text      = InstallerCore.DefaultInstallDir();
        UpdateStatusBadge();
    }

    // ── UI state helpers ─────────────────────────────────────────────────

    private void UpdateStatusBadge()
    {
        bool installed = InstallerCore.IsInstalled();
        StatusBadgeText.Text       = installed ? "INSTALLED"     : "NOT INSTALLED";
        StatusBadgeText.Foreground = installed
            ? System.Windows.Media.Brushes.LightSkyBlue
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x60, 0xD0, 0x60));
        StatusBadge.Background = installed
            ? new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x10, 0x28, 0x3C))
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1E, 0x3A, 0x1E));

        InstallBtn.IsEnabled   = !installed && !_busy;
        UninstallBtn.IsEnabled =  installed && !_busy;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        InstallBtn.IsEnabled   = !busy;
        UninstallBtn.IsEnabled = !busy;
        BrowseBtn.IsEnabled    = !busy;
        PathBox.IsReadOnly     = busy;
        if (busy) ProgressSection.Visibility = Visibility.Visible;
    }

    // ── Progress callback (called from background thread) ────────────────

    private void ReportProgress(string message, int percent)
    {
        Dispatcher.Invoke(() =>
        {
            LogBox.AppendText(message + "\n");
            LogBox.ScrollToEnd();
            if (percent < 0) return;   // informational/warning — log only
            StepText.Text = message;
            PctText.Text  = percent + " %";
            PBar.Value    = percent;
        });
    }

    // ── Button handlers ──────────────────────────────────────────────────

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title             = "Choose installation folder",
            InitialDirectory  = PathBox.Text,
        };
        if (dlg.ShowDialog() == true)
            PathBox.Text = dlg.FolderName;
    }

    private async void InstallBtn_Click(object sender, RoutedEventArgs e)
    {
        LogBox.Clear();
        SetBusy(true);
        bool ok = await Task.Run(() =>
            InstallerCore.RunInstall(PathBox.Text.Trim(), ReportProgress));
        SetBusy(false);
        UpdateStatusBadge();
        ReportProgress(ok ? "Installation complete." : "Installation failed — see log above.", 100);
    }

    private async void UninstallBtn_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(
                $"Remove {InstallerCore.Config.AppDisplayName} from this machine?",
                "Confirm Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        LogBox.Clear();
        SetBusy(true);
        bool ok = await Task.Run(() =>
            InstallerCore.RunUninstall(ReportProgress));
        SetBusy(false);
        UpdateStatusBadge();
        ReportProgress(ok ? "Uninstall complete." : "Uninstall finished with warnings.", 100);
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
