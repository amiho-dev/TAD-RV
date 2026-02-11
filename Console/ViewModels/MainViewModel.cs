// ───────────────────────────────────────────────────────────────────────────
// MainViewModel.cs — Top-level ViewModel with sidebar navigation
// ───────────────────────────────────────────────────────────────────────────

using System.Windows.Input;
using TadConsole.Helpers;

namespace TadConsole.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private ViewModelBase _currentView = null!;

    public ViewModelBase CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    // Child ViewModels (created once, reused)
    public DashboardViewModel  Dashboard  { get; }
    public DeployViewModel     Deploy     { get; }
    public PolicyViewModel     Policy     { get; }
    public AlertsViewModel     Alerts     { get; }

    public ICommand NavigateDashboardCommand  { get; }
    public ICommand NavigateDeployCommand     { get; }
    public ICommand NavigatePolicyCommand     { get; }
    public ICommand NavigateAlertsCommand     { get; }

    public MainViewModel()
    {
        Dashboard = new DashboardViewModel();
        Deploy    = new DeployViewModel();
        Policy    = new PolicyViewModel();
        Alerts    = new AlertsViewModel();

        NavigateDashboardCommand = new RelayCommand(() => CurrentView = Dashboard);
        NavigateDeployCommand    = new RelayCommand(() => CurrentView = Deploy);
        NavigatePolicyCommand    = new RelayCommand(() => CurrentView = Policy);
        NavigateAlertsCommand    = new RelayCommand(() => CurrentView = Alerts);

        // Start on the Dashboard
        CurrentView = Dashboard;
    }
}
