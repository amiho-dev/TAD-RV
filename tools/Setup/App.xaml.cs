using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace TADSetup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Catch exceptions from background threads before WPF is ready
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            ShowFatalError(ex.ExceptionObject?.ToString() ?? "Unknown error");

        // Catch exceptions on the WPF dispatcher thread
        DispatcherUnhandledException += (_, ex) =>
        {
            ex.Handled = true;
            ShowFatalError(ex.Exception.ToString());
        };

        base.OnStartup(e);
        var win = new MainWindow();
        MainWindow = win;
        win.Show();
    }

    private static void ShowFatalError(string details)
    {
        // Write a crash log to the desktop so the user can report it
        try
        {
            string log = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "TADSetup_crash.txt");
            File.WriteAllText(log, $"[{DateTime.Now:u}]\n{details}\n");
        }
        catch { }

        MessageBox.Show(
            "An unexpected error occurred:\n\n" + details,
            "TAD Setup — Fatal Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
