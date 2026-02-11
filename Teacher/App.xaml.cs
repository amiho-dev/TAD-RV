// ───────────────────────────────────────────────────────────────────────────
// App.xaml.cs — Teacher Controller entry point
// (C) 2026 TAD Europe — https://tad-it.eu
// ───────────────────────────────────────────────────────────────────────────

using System.Windows;

namespace TadTeacher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var mainWindow = new MainWindow();
        mainWindow.Show();
    }
}
