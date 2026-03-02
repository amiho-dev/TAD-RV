// MainWindow.xaml.cs
using System.Reflection;
using System.Windows;

namespace TADDomainController.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var ver = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "—";
        TxtVersion.Text    = $"tad-it.eu  ·  {ver}";
        TxtVersionBar.Text = ver;
    }
}
