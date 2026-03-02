// ───────────────────────────────────────────────────────────────────────────
// MainViewModel.cs — Root view-model driving sidebar navigation
// ───────────────────────────────────────────────────────────────────────────

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TadConsole.Helpers;

namespace TadConsole.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private object _currentView;

    public MainViewModel()
    {
        // Default view
        var dashboard = new DashboardViewModel();
        _currentView = dashboard;

        NavigateDashboardCommand = new RelayCommand(() => CurrentView = new DashboardViewModel());
        NavigateDeployCommand    = new RelayCommand(() => CurrentView = new DeployViewModel());
        NavigatePolicyCommand    = new RelayCommand(() => CurrentView = new PolicyViewModel());
        NavigateAlertsCommand    = new RelayCommand(() => CurrentView = new AlertsViewModel());
    }

    public object CurrentView
    {
        get => _currentView;
        set { _currentView = value; OnPropertyChanged(); }
    }

    public ICommand NavigateDashboardCommand { get; }
    public ICommand NavigateDeployCommand    { get; }
    public ICommand NavigatePolicyCommand    { get; }
    public ICommand NavigateAlertsCommand    { get; }

    // ── INotifyPropertyChanged ──────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
