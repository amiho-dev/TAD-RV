// RecordingsView.xaml.cs
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using TADDomainController.Services;
using TADDomainController.ViewModels;
using WinForms = System.Windows.Forms;

namespace TADDomainController.Views;

public partial class RecordingsView : UserControl
{
    public RecordingsView()
    {
        InitializeComponent();
    }

    private void BtnBrowse_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        using var dlg = new WinForms.FolderBrowserDialog
        {
            Description = "Select folder for screenshots and recordings",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (DataContext is RecordingsViewModel vm)
            dlg.InitialDirectory = vm.SaveFolder;

        if (dlg.ShowDialog() == WinForms.DialogResult.OK
            && DataContext is RecordingsViewModel vm2)
        {
            vm2.SaveFolder = dlg.SelectedPath;
        }
    }

    private void OnCaptureDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView lv && lv.SelectedItem is RecordingEntry entry)
        {
            try
            {
                if (File.Exists(entry.FilePath))
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = entry.FilePath,
                        UseShellExecute = true
                    });
            }
            catch { /* file may have been deleted */ }
        }
    }
}
