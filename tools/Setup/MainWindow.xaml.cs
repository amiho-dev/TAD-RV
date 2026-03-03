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

    // ── UI state ─────────────────────────────────────────────────────────

    private void UpdateStatusBadge()
    {
        bool installed = InstallerCore.IsInstalled();
        StatusBadgeText.Text = installed ? "INSTALLED" : "NOT INSTALLED";
        if (installed)
        {
            StatusBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x87, 0xCE, 0xEB));
            StatusBadge.Background     = new SolidColorBrush(Color.FromRgb(0x10, 0x28, 0x3C));
        }
        else
        {
            StatusBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0xD0, 0x60));
            StatusBadge.Background     = new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x1E));
        }
        InstallBtn.IsEnabled   = !installed && !_busy;
        UninstallBtn.IsEnabled =  installed && !_busy;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        InstallBtn.IsEnabled   = !busy && !InstallerCore.IsInstalled();
        UninstallBtn.IsEnabled = !busy &&  InstallerCore.IsInstalled();
        BrowseBtn.IsEnabled    = !busy;
        PathBox.IsReadOnly     = busy;
    }

    // ── Progress callback (called from background thread) ────────────────

    private void ReportProgress(string message, int percent)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ReportProgress(message, percent));
            return;
        }
        StepText.Text = message;
        if (percent >= 0)
        {
            PctText.Text = percent + " %";
            PBar.Value   = percent;
        }
    }

    // ── Buttons ──────────────────────────────────────────────────────────

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title            = "Choose installation folder",
            InitialDirectory = PathBox.Text,
        };
        if (dlg.ShowDialog() == true)
            PathBox.Text = dlg.FolderName;
    }

    private async void InstallBtn_Click(object sender, RoutedEventArgs e)
    {
        ReportProgress("Starting installation...", 0);
        SetBusy(true);
        try
        {
            string path = PathBox.Text.Trim();
            bool ok = await Task.Run(() =>
                InstallerCore.RunInstall(path, ReportProgress));

            if (ok)
            {
                ReportProgress("Installation complete.", 100);
                UpdateStatusBadge();
            }
            else
            {
                ReportProgress("Installation failed.", 0);
                MessageBox.Show(
                    "Installation failed.\n\nMake sure you are running as Administrator.",
                    InstallerCore.Config.AppDisplayName + " Setup",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            ReportProgress("Error: " + ex.Message, 0);
            MessageBox.Show(
                "Unexpected error:\n\n" + ex.Message,
                InstallerCore.Config.AppDisplayName + " Setup",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void UninstallBtn_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                $"Remove {InstallerCore.Config.AppDisplayName} from this machine?",
                "Confirm Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        ReportProgress("Starting uninstall...", 0);
        SetBusy(true);
        try
        {
            bool ok = await Task.Run(() =>
                InstallerCore.RunUninstall(ReportProgress));

            ReportProgress(ok ? "Uninstall complete." : "Uninstall finished with warnings.", 100);
            UpdateStatusBadge();
        }
        catch (Exception ex)
        {
            ReportProgress("Error: " + ex.Message, 0);
            MessageBox.Show(
                "Unexpected error:\n\n" + ex.Message,
                InstallerCore.Config.AppDisplayName + " Setup",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
