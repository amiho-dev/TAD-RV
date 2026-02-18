// ───────────────────────────────────────────────────────────────────────────
// AlertsViewModel.cs — Alert / event log viewer with filtering
// ───────────────────────────────────────────────────────────────────────────

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TadConsole.Helpers;

namespace TadConsole.ViewModels;

public sealed class AlertsViewModel : INotifyPropertyChanged
{
    private string _filterText = string.Empty;

    public AlertsViewModel()
    {
        RefreshCommand = new RelayCommand(() => { /* TODO: reload from EventLogService */ });
        ClearCommand   = new RelayCommand(() => Alerts.Clear());

        // Seed with a placeholder
        Alerts.Add("[INFO]  TAD.RV Management Console started");
    }

    public string FilterText
    {
        get => _filterText;
        set { _filterText = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> Alerts { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand ClearCommand   { get; }

    // ── INotifyPropertyChanged ──────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
