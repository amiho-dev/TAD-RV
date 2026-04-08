// ───────────────────────────────────────────────────────────────────────────
// DashboardViewModel.cs — Overview: service status, driver status, stats
// ───────────────────────────────────────────────────────────────────────────

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TADBridge.Shared;
using TADBridge.Shared.Classrooms;
using TADDomainController.Helpers;
using TADDomainController.Services;

namespace TADDomainController.ViewModels;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private string _serviceStatus = "Unknown";
    private string _driverStatus  = "Unknown";
    private int    _activeClients;
    private int    _alertCount;
    private string _updateStatus  = "";
    private bool   _updateAvailable;
    private string _releaseNotes  = "";
    private string _lastRefreshed = "Never";

    private readonly UpdateManager _updater = new("dc");
    private readonly TADServiceController _serviceController = new();
    private readonly EventLogService _eventLogService = new();

    public DashboardViewModel()
    {
        RefreshRuntimeCommand = new AsyncRelayCommand(RefreshRuntimeStatusAsync);

        _ = RefreshRuntimeStatusAsync();
        _ = CheckForUpdatesAsync();
    }

    public string ServiceStatus
    {
        get => _serviceStatus;
        set { _serviceStatus = value; OnPropertyChanged(); }
    }

    public string DriverStatus
    {
        get => _driverStatus;
        set { _driverStatus = value; OnPropertyChanged(); }
    }

    public int ActiveClients
    {
        get => _activeClients;
        set { _activeClients = value; OnPropertyChanged(); }
    }

    public int AlertCount
    {
        get => _alertCount;
        set { _alertCount = value; OnPropertyChanged(); }
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        set { _updateStatus = value; OnPropertyChanged(); }
    }

    public bool UpdateAvailable
    {
        get => _updateAvailable;
        set { _updateAvailable = value; OnPropertyChanged(); }
    }

    public string ReleaseNotes
    {
        get => _releaseNotes;
        set { _releaseNotes = value; OnPropertyChanged(); }
    }

    public string LastRefreshed
    {
        get => _lastRefreshed;
        set { _lastRefreshed = value; OnPropertyChanged(); }
    }

    public ICommand RefreshRuntimeCommand { get; }

    private async Task RefreshRuntimeStatusAsync()
    {
        try
        {
            var (driver, bridge) = await _serviceController.QueryAllAsync();

            ServiceStatus = FormatServiceStatus(bridge);
            DriverStatus = FormatServiceStatus(driver);

            var events = _eventLogService.ReadRecentEvents(300);
            AlertCount = events.Count(e =>
                string.Equals(e.Level, "Error", StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.Level, "Warning", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("[TAD.RV ALERT]", StringComparison.OrdinalIgnoreCase));

            ActiveClients = ReadAssignedClientCount();
            LastRefreshed = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch
        {
            ServiceStatus = "Unknown";
            DriverStatus = "Unknown";
            LastRefreshed = "Refresh failed";
        }
    }

    private static string FormatServiceStatus(ServiceStatusInfo info)
    {
        if (!info.Exists)
            return "Not Installed";

        return info.Status.ToUpperInvariant() switch
        {
            "RUNNING" => "Running",
            "STOPPED" => "Stopped",
            "START_PENDING" => "Starting",
            "STOP_PENDING" => "Stopping",
            _ => info.Status
        };
    }

    private static int ReadAssignedClientCount()
    {
        try
        {
            var layout = RoomLayout.Load();
            return layout.AssignedItems
                .Select(i => i.Host?.Trim())
                .Where(host => !string.IsNullOrWhiteSpace(host))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }
        catch
        {
            return 0;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            UpdateStatus = "Checking for updates...";
            var update = await _updater.CheckForUpdateAsync();

            if (update != null)
            {
                UpdateStatus = update.IsForceUpdate
                    ? $"CRITICAL update required: v{update.Version} — install enforced"
                    : $"Update available: v{update.Version} — {update.Title}";
                ReleaseNotes = update.ReleaseNotes;
                UpdateAvailable = true;
            }
            else
            {
                UpdateStatus = $"Up to date (v{_updater.CurrentVersion})";
                ReleaseNotes = "";
                UpdateAvailable = false;
            }
        }
        catch
        {
            UpdateStatus = "Update check failed — no internet?";
            UpdateAvailable = false;
        }
    }

    // ── INotifyPropertyChanged ──────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
