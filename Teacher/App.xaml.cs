// ───────────────────────────────────────────────────────────────────────────
// App.xaml.cs — Teacher Controller entry point
// (C) 2026 TAD Europe — https://tad-it.eu
// ───────────────────────────────────────────────────────────────────────────

using System.Windows;
using System.Windows.Threading;

namespace TadTeacher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;

        try
        {
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Fatal startup error:\n\n{ex}",
                "TAD.RV Teacher — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Unhandled error:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
            "TAD.RV Teacher — Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            MessageBox.Show(
                $"Critical error:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "TAD.RV Teacher — Critical",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
