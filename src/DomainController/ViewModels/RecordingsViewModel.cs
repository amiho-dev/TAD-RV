// ───────────────────────────────────────────────────────────────────────────
// RecordingsViewModel.cs — View-model for automatic screenshot/recording UI
// ───────────────────────────────────────────────────────────────────────────

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TADDomainController.Helpers;
using TADDomainController.Services;

namespace TADDomainController.ViewModels;

public sealed class RecordingsViewModel : INotifyPropertyChanged, IDisposable
{
    private bool   _isActive;
    private int    _intervalMinutes = 5;
    private bool   _videoEnabled;
    private string _saveFolder;
    private int    _discoveredCount;
    private string _statusText = "Stopped";
    private RecordingService? _service;

    // ─── Recent capture log (newest first, capped at 200) ─────────────

    public ObservableCollection<RecordingEntry> RecentCaptures { get; } = new();

    // ─── Properties ───────────────────────────────────────────────────

    public bool IsActive
    {
        get => _isActive;
        private set { _isActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotActive)); }
    }

    public bool IsNotActive => !_isActive;

    public int IntervalMinutes
    {
        get => _intervalMinutes;
        set { _intervalMinutes = Math.Clamp(value, 1, 60); OnPropertyChanged(); }
    }

    public bool VideoEnabled
    {
        get => _videoEnabled;
        set { _videoEnabled = value; OnPropertyChanged(); }
    }

    public string SaveFolder
    {
        get => _saveFolder;
        set { _saveFolder = value; OnPropertyChanged(); }
    }

    public int DiscoveredCount
    {
        get => _discoveredCount;
        private set { _discoveredCount = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    // ─── Commands ─────────────────────────────────────────────────────

    public ICommand StartCommand    { get; }
    public ICommand StopCommand     { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand ClearHistoryCommand { get; }

    // ─── Constructor ──────────────────────────────────────────────────

    public RecordingsViewModel()
    {
        _saveFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TAD.RV", "Recordings");

        StartCommand   = new RelayCommand(StartRecording,  () => !IsActive);
        StopCommand    = new RelayCommand(StopRecording,   () => IsActive);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        ClearHistoryCommand = new RelayCommand(() =>
        {
            App.Current.Dispatcher.Invoke(() => RecentCaptures.Clear());
        });
    }

    // ─── Start / Stop ─────────────────────────────────────────────────

    private void StartRecording()
    {
        if (IsActive) return;

        _service = new RecordingService
        {
            SaveFolder              = SaveFolder,
            SnapshotIntervalSeconds = IntervalMinutes * 60,
            VideoRecordingEnabled   = VideoEnabled
        };

        _service.FileSaved += OnFileSaved;
        _service.EndpointDiscovered += OnEndpointDiscovered;
        _service.Start();

        IsActive   = true;
        StatusText = $"Active — scanning for endpoints (interval: {IntervalMinutes} min)";
        DiscoveredCount = 0;
    }

    private void StopRecording()
    {
        if (!IsActive) return;

        _service?.Stop();
        _service?.Dispose();
        _service = null;

        IsActive   = false;
        StatusText = "Stopped";
    }

    // ─── Event handlers ───────────────────────────────────────────────

    private void OnFileSaved(RecordingEntry entry)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            RecentCaptures.Insert(0, entry);
            // Cap at 200 entries
            while (RecentCaptures.Count > 200)
                RecentCaptures.RemoveAt(RecentCaptures.Count - 1);
        });
    }

    private void OnEndpointDiscovered(string ip, string hostname)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            DiscoveredCount++;
            StatusText = $"Active — {DiscoveredCount} endpoint(s) discovered (interval: {IntervalMinutes} min)";
        });
    }

    // ─── Open folder ──────────────────────────────────────────────────

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(SaveFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = SaveFolder,
                UseShellExecute = true
            });
        }
        catch { /* Folder may not exist yet */ }
    }

    // ─── INotifyPropertyChanged ───────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose() => StopRecording();
}
