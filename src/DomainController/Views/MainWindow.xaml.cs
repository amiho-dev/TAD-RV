// MainWindow.xaml.cs
using System.Reflection;
using System.Windows;

namespace TADDomainController.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var fullVer = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "—";

        // Strip component suffix: "v26.3.02.004-dc" → "v26.3.02.004"
        var shortVer = fullVer.TrimStart('v', 'V');
        var dash = shortVer.IndexOf('-');
        if (dash > 0) shortVer = shortVer[..dash];

        TxtVersion.Text    = $"tad-it.eu  ·  v{shortVer}";
        TxtVersionBar.Text = $"v{shortVer}";
    }
}
