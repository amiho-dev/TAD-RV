// ───────────────────────────────────────────────────────────────────────────
// DeployViewModel.cs — Full deployment workflow with live progress
// ───────────────────────────────────────────────────────────────────────────

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using TadConsole.Helpers;
using TadConsole.Services;

namespace TadConsole.ViewModels;

public sealed class DeployViewModel : ViewModelBase
{
    private readonly DeploymentService _deployer = new();

    // ── Configuration Fields ─────────────────────────────────────────
    private string _driverPath = "";
    public string DriverPath
    {
        get => _driverPath;
        set => SetProperty(ref _driverPath, value);
    }

    private string _servicePath = "";
    public string ServicePath
    {
        get => _servicePath;
        set => SetProperty(ref _servicePath, value);
    }

    private string _targetDir = @"C:\Program Files\TAD_RV";
    public string TargetDir
    {
        get => _targetDir;
        set => SetProperty(ref _targetDir, value);
    }

    private string _domainController = "dc01.school.local";
    public string DomainController
    {
        get => _domainController;
        set => SetProperty(ref _domainController, value);
    }

    private bool _installDriver = true;
    public bool InstallDriver
    {
        get => _installDriver;
        set => SetProperty(ref _installDriver, value);
    }

    private bool _installService = true;
    public bool InstallService
    {
        get => _installService;
        set => SetProperty(ref _installService, value);
    }

    // ── Progress ─────────────────────────────────────────────────────
    private int _progress;
    public int Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    private bool _isDeploying;
    public bool IsDeploying
    {
        get => _isDeploying;
        set { SetProperty(ref _isDeploying, value); OnPropertyChanged(nameof(IsIdle)); }
    }

    public bool IsIdle => !IsDeploying;

    private string _statusText = "Ready to deploy.";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    // ── Log and Results ──────────────────────────────────────────────
    private string _log = "";
    public string Log
    {
        get => _log;
        set => SetProperty(ref _log, value);
    }

    public ObservableCollection<DeploymentStepResult> StepResults { get; } = new();

    // ── Commands ─────────────────────────────────────────────────────
    public ICommand DeployCommand        { get; }
    public ICommand BrowseDriverCommand  { get; }
    public ICommand BrowseServiceCommand { get; }
    public ICommand BrowseTargetCommand  { get; }

    private CancellationTokenSource? _cts;

    public DeployViewModel()
    {
        DeployCommand        = new AsyncRelayCommand(DeployAsync, () => !IsDeploying);
        BrowseDriverCommand  = new RelayCommand(BrowseDriver);
        BrowseServiceCommand = new RelayCommand(BrowseService);
        BrowseTargetCommand  = new RelayCommand(BrowseTarget);

        _deployer.LogMessage += msg =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Log += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            });
        };

        _deployer.StepCompleted += result =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StepResults.Add(result);
            });
        };
    }

    private async Task DeployAsync()
    {
        // Validate
        if (InstallDriver && string.IsNullOrWhiteSpace(DriverPath))
        {
            StatusText = "ERROR: Please specify the driver path.";
            return;
        }
        if (InstallService && string.IsNullOrWhiteSpace(ServicePath))
        {
            StatusText = "ERROR: Please specify the service publish path.";
            return;
        }

        IsDeploying = true;
        Progress    = 0;
        Log         = "";
        StepResults.Clear();
        StatusText  = "Deploying…";

        _cts = new CancellationTokenSource();

        var config = new DeploymentConfig
        {
            DriverPath       = DriverPath,
            ServicePath      = ServicePath,
            TargetDir        = TargetDir,
            DomainController = DomainController,
            InstallDriver    = InstallDriver,
            InstallService   = InstallService,
        };

        try
        {
            var progress = new Progress<int>(p => Progress = p);
            var results  = await _deployer.DeployAsync(config, progress, _cts.Token);

            int succeeded = results.Count(r => r.Success);
            int failed    = results.Count(r => !r.Success);

            StatusText = $"Done — {succeeded} succeeded, {failed} failed.";
            Progress   = 100;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Deployment cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Deployment failed: {ex.Message}";
        }
        finally
        {
            IsDeploying = false;
        }
    }

    private void BrowseDriver()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Select TAD.RV.sys Driver Binary",
            Filter = "Driver Files (*.sys)|*.sys|All Files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true)
            DriverPath = dlg.FileName;
    }

    private void BrowseService()
    {
        // Use folder picker via WinForms interop
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select published TadBridgeService directory",
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            ServicePath = dlg.SelectedPath;
    }

    private void BrowseTarget()
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select installation target directory",
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TargetDir = dlg.SelectedPath;
    }
}
