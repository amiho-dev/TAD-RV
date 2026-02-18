// ───────────────────────────────────────────────────────────────────────────
// DeployViewModel.cs — Deployment management: push, rollback, status
// ───────────────────────────────────────────────────────────────────────────

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TadConsole.Helpers;

namespace TadConsole.ViewModels;

public sealed class DeployViewModel : INotifyPropertyChanged
{
    private string _targetPath = @"\\server\share\TAD-RV";
    private bool   _isDeploying;

    public DeployViewModel()
    {
        DeployCommand   = new RelayCommand(() => { /* TODO: wire DeploymentService */ }, () => !IsDeploying);
        RollbackCommand = new RelayCommand(() => { /* TODO: rollback */ }, () => !IsDeploying);
    }

    public string TargetPath
    {
        get => _targetPath;
        set { _targetPath = value; OnPropertyChanged(); }
    }

    public bool IsDeploying
    {
        get => _isDeploying;
        set { _isDeploying = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> LogMessages { get; } = new();

    public ICommand DeployCommand   { get; }
    public ICommand RollbackCommand { get; }

    // ── INotifyPropertyChanged ──────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
