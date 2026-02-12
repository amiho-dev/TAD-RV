// ───────────────────────────────────────────────────────────────────────────
// App.xaml.cs — Application entry point
// ───────────────────────────────────────────────────────────────────────────

using System.Windows;

namespace TadConsole;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global unhandled exception handler
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"Unhandled error:\n\n{ex.Message}\n\n{ex.StackTrace}",
                    "TAD.RV — Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"UI error:\n\n{args.Exception.Message}",
                "TAD.RV — Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
    }
}
