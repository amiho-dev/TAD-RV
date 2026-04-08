// ───────────────────────────────────────────────────────────────────────────
// AlertsViewModel.cs — Alert / event log viewer with filtering
// ───────────────────────────────────────────────────────────────────────────

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TADDomainController.Helpers;
using TADDomainController.Services;

namespace TADDomainController.ViewModels;

public sealed class AlertsViewModel : INotifyPropertyChanged
{
    private readonly EventLogService _eventLogService = new();
    private readonly List<string> _allAlerts = [];

    private string _filterText = string.Empty;

    public AlertsViewModel()
    {
        RefreshCommand = new RelayCommand(RefreshAlerts);
        ClearCommand   = new RelayCommand(ClearAlerts);

        RefreshAlerts();
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            _filterText = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public ObservableCollection<string> Alerts { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand ClearCommand   { get; }

    private void RefreshAlerts()
    {
        _allAlerts.Clear();

        var events = _eventLogService.ReadRecentEvents();
        foreach (var item in events)
        {
            _allAlerts.Add($"[{item.TimeStamp:yyyy-MM-dd HH:mm:ss}] [{item.Level}] ({item.EventId}) {item.Source}: {item.Message}");
        }

        if (_allAlerts.Count == 0)
        {
            _allAlerts.Add("[INFO] No recent TAD.RV events were found in the Application event log.");
        }

        ApplyFilter();
    }

    private void ClearAlerts()
    {
        _allAlerts.Clear();
        Alerts.Clear();
    }

    private void ApplyFilter()
    {
        Alerts.Clear();

        IEnumerable<string> source = _allAlerts;
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            source = source.Where(a => a.Contains(FilterText, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var alert in source)
            Alerts.Add(alert);
    }

    // ── INotifyPropertyChanged ──────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
