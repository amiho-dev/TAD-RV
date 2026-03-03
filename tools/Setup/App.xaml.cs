using System.Windows;

namespace TADSetup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var win = new MainWindow();
        MainWindow = win;
        win.Show();
    }
}
