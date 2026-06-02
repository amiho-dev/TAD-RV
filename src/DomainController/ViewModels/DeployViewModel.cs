// ───────────────────────────────────────────────────────────────────────────
// DeployViewModel.cs — Deployment management: push, rollback, status
// ───────────────────────────────────────────────────────────────────────────

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TADDomainController.Helpers;
using TADDomainController.Services;

namespace TADDomainController.ViewModels;

public sealed class DeployViewModel : INotifyPropertyChanged
{
    private string _targetPath = @"\\server\share\TAD-RV";
    private string _servicePublishPath = @"C:\Install\TADBridgeService";
    private string _domainController = Environment.MachineName;
    private bool _blockUsbStorage;
    private bool _pushClientUpdate;
    private bool   _isDeploying;
    private readonly DeploymentService _deploymentService = new();

    public DeployViewModel()
    {
        DeployCommand = new RelayCommand(async () => await DeployNowAsync(), () => !IsDeploying);
        RollbackCommand = new RelayCommand(async () => await RollbackAsync(), () => !IsDeploying);
        ToggleUsbBlockCommand = new RelayCommand(async () => await ToggleUsbBlockAsync(), () => !IsDeploying);
        PushUpdatesCommand = new RelayCommand(async () => await PushUpdatesAsync(), () => !IsDeploying);
        RefreshOperationalLogsCommand = new RelayCommand(RefreshOperationalLogs, () => !IsDeploying);

        _deploymentService.LogMessage += msg => LogMessages.Add(msg);
        _deploymentService.StepCompleted += step =>
            LogMessages.Add($"[{(step.Success ? "OK" : "FAIL")}] {step.StepName} - {step.Message} ({step.Duration.TotalSeconds:F1}s)");
    }

    public string TargetPath
    {
        get => _targetPath;
        set { _targetPath = value; OnPropertyChanged(); }
    }

    public string ServicePublishPath
    {
        get => _servicePublishPath;
        set { _servicePublishPath = value; OnPropertyChanged(); }
    }

    public string DomainControllerHost
    {
        get => _domainController;
        set { _domainController = value; OnPropertyChanged(); }
    }

    public bool BlockUsbStorage
    {
        get => _blockUsbStorage;
        set { _blockUsbStorage = value; OnPropertyChanged(); }
    }

    public bool PushClientUpdate
    {
        get => _pushClientUpdate;
        set { _pushClientUpdate = value; OnPropertyChanged(); }
    }

    public bool IsDeploying
    {
        get => _isDeploying;
        set { _isDeploying = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> LogMessages { get; } = new();

    public ICommand DeployCommand   { get; }
    public ICommand RollbackCommand { get; }
    public ICommand ToggleUsbBlockCommand { get; }
    public ICommand PushUpdatesCommand { get; }
    public ICommand RefreshOperationalLogsCommand { get; }

    private async Task DeployNowAsync()
    {
        IsDeploying = true;
        try
        {
            LogMessages.Add("Starting deployment pipeline...");
            var cfg = new DeploymentConfig
            {
                ServicePath = ServicePublishPath,
                TargetDir = TargetPath,
                DomainController = DomainControllerHost,
                InstallService = true,
                BlockUsbStorageForStudents = BlockUsbStorage,
                PushClientUpdateAfterDeploy = PushClientUpdate
            };

            var prog = new Progress<int>(pct => LogMessages.Add($"Progress: {pct}%"));
            await _deploymentService.DeployAsync(cfg, prog, CancellationToken.None);
            LogMessages.Add("Deployment completed.");
        }
        catch (Exception ex)
        {
            LogMessages.Add("Deployment failed: " + ex.Message);
        }
        finally
        {
            IsDeploying = false;
        }
    }

    private async Task ToggleUsbBlockAsync()
    {
        IsDeploying = true;
        try
        {
            await _deploymentService.SetUsbBlockPolicyAsync(BlockUsbStorage);
            LogMessages.Add(BlockUsbStorage
                ? "USB storage blocking enabled for all student clients."
                : "USB storage blocking disabled for all student clients.");
        }
        catch (Exception ex)
        {
            LogMessages.Add("USB policy update failed: " + ex.Message);
        }
        finally
        {
            IsDeploying = false;
        }
    }

    private async Task PushUpdatesAsync()
    {
        IsDeploying = true;
        try
        {
            await _deploymentService.QueueClientForceUpdateAsync();
            LogMessages.Add("Force update queued for all managed clients.");
        }
        catch (Exception ex)
        {
            LogMessages.Add("Force update queue failed: " + ex.Message);
        }
        finally
        {
            IsDeploying = false;
        }
    }

    private async Task RollbackAsync()
    {
        IsDeploying = true;
        try
        {
            LogMessages.Add("Starting rollback...");
            await _deploymentService.RollbackLastDeploymentAsync(TargetPath, CancellationToken.None);
            LogMessages.Add("Rollback completed.");
        }
        catch (Exception ex)
        {
            LogMessages.Add("Rollback failed: " + ex.Message);
        }
        finally
        {
            IsDeploying = false;
        }
    }

    private void RefreshOperationalLogs()
    {
        LogMessages.Clear();
        foreach (var line in _deploymentService.CollectOperationalLogs(120))
            LogMessages.Add(line);
    }

    // ── INotifyPropertyChanged ──────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
