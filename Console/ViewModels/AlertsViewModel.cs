// ───────────────────────────────────────────────────────────────────────────
// AlertsViewModel.cs — Event log viewer for TAD.RV alerts
// ───────────────────────────────────────────────────────────────────────────

using System.Collections.ObjectModel;
using System.Windows.Input;
using TadConsole.Helpers;
using TadConsole.Services;

namespace TadConsole.ViewModels;

public sealed class AlertsViewModel : ViewModelBase
{
    private readonly EventLogService _eventService = new();

    public ObservableCollection<TadEventEntry> Events { get; } = new();

    private TadEventEntry? _selectedEvent;
    public TadEventEntry? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            SetProperty(ref _selectedEvent, value);
            OnPropertyChanged(nameof(SelectedEventDetail));
        }
    }

    public string SelectedEventDetail => _selectedEvent != null
        ? $"Time: {_selectedEvent.TimeStamp:yyyy-MM-dd HH:mm:ss}\n" +
          $"Level: {_selectedEvent.Level}\n" +
          $"Event ID: {_selectedEvent.EventId}\n" +
          $"Source: {_selectedEvent.Source}\n\n" +
          $"{_selectedEvent.Message}"
        : "Select an event to view details.";

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    // ── Commands ─────────────────────────────────────────────────────
    public ICommand RefreshCommand { get; }
    public ICommand ClearCommand   { get; }

    public AlertsViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ClearCommand   = new RelayCommand(() => { Events.Clear(); StatusText = "Cleared."; });
    }

    private async Task RefreshAsync()
    {
        IsLoading  = true;
        StatusText = "Loading events…";
        Events.Clear();

        await Task.Run(() =>
        {
            var entries = _eventService.ReadRecentEvents(500);
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var entry in entries)
                    Events.Add(entry);
            });
        });

        StatusText = $"{Events.Count} events loaded.";
        IsLoading  = false;
    }
}
