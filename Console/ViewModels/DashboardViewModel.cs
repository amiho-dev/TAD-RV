// ───────────────────────────────────────────────────────────────────────────
// DashboardViewModel.cs — Real-time status of driver + service
// ───────────────────────────────────────────────────────────────────────────

using System.Windows.Input;
using System.Windows.Threading;
using TadConsole.Helpers;
using TadConsole.Services;

namespace TadConsole.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly TadServiceController _svcCtrl = new();
    private readonly RegistryService      _registry = new();
    private readonly DispatcherTimer       _timer;

    // ── Driver Status ────────────────────────────────────────────────
    private string _driverStatus = "Checking…";
    public string DriverStatus
    {
        get => _driverStatus;
        set => SetProperty(ref _driverStatus, value);
    }

    private string _driverStatusColor = "Gray";
    public string DriverStatusColor
    {
        get => _driverStatusColor;
        set => SetProperty(ref _driverStatusColor, value);
    }

    // ── Bridge Service Status ────────────────────────────────────────
    private string _serviceStatus = "Checking…";
    public string ServiceStatus
    {
        get => _serviceStatus;
        set => SetProperty(ref _serviceStatus, value);
    }

    private string _serviceStatusColor = "Gray";
    public string ServiceStatusColor
    {
        get => _serviceStatusColor;
        set => SetProperty(ref _serviceStatusColor, value);
    }

    private int _servicePid;
    public int ServicePid
    {
        get => _servicePid;
        set => SetProperty(ref _servicePid, value);
    }

    // ── Registry Info ────────────────────────────────────────────────
    private string _installDir = "";
    public string InstallDir
    {
        get => _installDir;
        set => SetProperty(ref _installDir, value);
    }

    private string _domainController = "";
    public string DomainController
    {
        get => _domainController;
        set => SetProperty(ref _domainController, value);
    }

    private string _deployedAt = "";
    public string DeployedAt
    {
        get => _deployedAt;
        set => SetProperty(ref _deployedAt, value);
    }

    private bool _provisioned;
    public bool Provisioned
    {
        get => _provisioned;
        set => SetProperty(ref _provisioned, value);
    }

    private string _organizationalUnit = "";
    public string OrganizationalUnit
    {
        get => _organizationalUnit;
        set => SetProperty(ref _organizationalUnit, value);
    }

    private int _policyVersion;
    public int PolicyVersion
    {
        get => _policyVersion;
        set => SetProperty(ref _policyVersion, value);
    }

    private bool _registryExists;
    public bool RegistryExists
    {
        get => _registryExists;
        set => SetProperty(ref _registryExists, value);
    }

    // ── Commands ─────────────────────────────────────────────────────
    public ICommand RefreshCommand          { get; }
    public ICommand StartDriverCommand      { get; }
    public ICommand StopDriverCommand       { get; }
    public ICommand StartServiceCommand     { get; }
    public ICommand StopServiceCommand      { get; }
    public ICommand RestartServiceCommand   { get; }

    public DashboardViewModel()
    {
        RefreshCommand        = new AsyncRelayCommand(RefreshAsync);
        StartDriverCommand    = new AsyncRelayCommand(() => ControlServiceAsync(TadServiceController.DriverServiceName, "start"));
        StopDriverCommand     = new AsyncRelayCommand(() => ControlServiceAsync(TadServiceController.DriverServiceName, "stop"));
        StartServiceCommand   = new AsyncRelayCommand(() => ControlServiceAsync(TadServiceController.BridgeServiceName, "start"));
        StopServiceCommand    = new AsyncRelayCommand(() => ControlServiceAsync(TadServiceController.BridgeServiceName, "stop"));
        RestartServiceCommand = new AsyncRelayCommand(() => ControlServiceAsync(TadServiceController.BridgeServiceName, "restart"));

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var (driver, bridge) = await _svcCtrl.QueryAllAsync();

        // Driver
        DriverStatus      = driver.Exists ? driver.Status : "Not Installed";
        DriverStatusColor = driver.Status switch
        {
            "RUNNING" => "#27AE60",
            "STOPPED" => "#E74C3C",
            _         => "#F39C12"
        };

        // Bridge Service
        ServiceStatus      = bridge.Exists ? bridge.Status : "Not Installed";
        ServiceStatusColor = bridge.Status switch
        {
            "RUNNING" => "#27AE60",
            "STOPPED" => "#E74C3C",
            _         => "#F39C12"
        };
        ServicePid = bridge.Pid;

        // Registry
        var reg = _registry.ReadConfig();
        RegistryExists      = reg.KeyExists;
        InstallDir          = reg.InstallDir;
        DomainController    = reg.DomainController;
        DeployedAt          = reg.DeployedAt;
        Provisioned         = reg.Provisioned;
        OrganizationalUnit  = reg.OrganizationalUnit;
        PolicyVersion       = reg.PolicyVersion;
    }

    private async Task ControlServiceAsync(string name, string action)
    {
        switch (action)
        {
            case "start":   await _svcCtrl.StartServiceAsync(name);   break;
            case "stop":    await _svcCtrl.StopServiceAsync(name);    break;
            case "restart": await _svcCtrl.RestartServiceAsync(name); break;
        }

        await Task.Delay(1500);
        await RefreshAsync();
    }
}
