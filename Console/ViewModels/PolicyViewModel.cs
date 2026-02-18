// ───────────────────────────────────────────────────────────────────────────
// PolicyViewModel.cs — Policy editor: lockdown rules, allowed apps, roles
// ───────────────────────────────────────────────────────────────────────────

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TadConsole.Helpers;

namespace TadConsole.ViewModels;

public sealed class PolicyViewModel : INotifyPropertyChanged
{
    private bool _lockdownEnabled;
    private bool _stealthEnabled;

    public PolicyViewModel()
    {
        SaveCommand = new RelayCommand(() => { /* TODO: persist policy via service */ });
        AllowedProcesses.Add("explorer.exe");
        AllowedProcesses.Add("notepad.exe");
    }

    public bool LockdownEnabled
    {
        get => _lockdownEnabled;
        set { _lockdownEnabled = value; OnPropertyChanged(); }
    }

    public bool StealthEnabled
    {
        get => _stealthEnabled;
        set { _stealthEnabled = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> AllowedProcesses { get; } = new();

    public ICommand SaveCommand { get; }

    // ── INotifyPropertyChanged ──────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
