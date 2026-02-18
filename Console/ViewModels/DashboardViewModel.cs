// ───────────────────────────────────────────────────────────────────────────
// DashboardViewModel.cs — Overview: service status, driver status, stats
// ───────────────────────────────────────────────────────────────────────────

using System.ComponentModel;
using System.Runtime.CompilerServices;
using TadBridge.Shared;

namespace TadConsole.ViewModels;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private string _serviceStatus = "Unknown";
    private string _driverStatus  = "Unknown";
    private int    _activeClients;
    private int    _alertCount;
    private string _updateStatus  = "";
    private bool   _updateAvailable;

    private readonly UpdateManager _updater = new("console");

    public DashboardViewModel()
    {
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

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            UpdateStatus = "Checking for updates...";
            var update = await _updater.CheckForUpdateAsync();

            if (update != null)
            {
                UpdateStatus = $"Update available: v{update.Version} — {update.Title}";
                UpdateAvailable = true;
            }
            else
            {
                UpdateStatus = $"Up to date (v{_updater.CurrentVersion})";
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
