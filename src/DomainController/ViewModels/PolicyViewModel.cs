// ───────────────────────────────────────────────────────────────────────────
// PolicyViewModel.cs — Policy editor: lockdown rules, allowed apps, roles
// ───────────────────────────────────────────────────────────────────────────

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using TADDomainController.Helpers;
using TADDomainController.Services;

namespace TADDomainController.ViewModels;

public sealed class PolicyViewModel : INotifyPropertyChanged
{
    private readonly RegistryService _registryService = new();

    private bool _lockdownEnabled;
    private bool _stealthEnabled;
    private int _policyVersion;

    public PolicyViewModel()
    {
        SaveCommand = new RelayCommand(SavePolicy);
        LoadPolicy();

        if (AllowedProcesses.Count == 0)
        {
            AllowedProcesses.Add("explorer.exe");
            AllowedProcesses.Add("notepad.exe");
        }
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

    private void LoadPolicy()
    {
        var config = _registryService.ReadConfig();
        _policyVersion = Math.Max(config.PolicyVersion, 0);

        if (string.IsNullOrWhiteSpace(config.PolicyJson))
            return;

        try
        {
            var payload = JsonSerializer.Deserialize<PolicyPayload>(config.PolicyJson);
            if (payload == null)
                return;

            _lockdownEnabled = payload.LockdownEnabled;
            _stealthEnabled = payload.StealthEnabled;

            AllowedProcesses.Clear();
            foreach (var proc in payload.AllowedProcesses
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                AllowedProcesses.Add(proc.Trim());
            }

            OnPropertyChanged(nameof(LockdownEnabled));
            OnPropertyChanged(nameof(StealthEnabled));
        }
        catch
        {
            // Keep default UI values if registry payload is invalid.
        }
    }

    private void SavePolicy()
    {
        var payload = new PolicyPayload
        {
            LockdownEnabled = LockdownEnabled,
            StealthEnabled = StealthEnabled,
            AllowedProcesses = AllowedProcesses
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        _policyVersion = Math.Max(1, _policyVersion + 1);
        _registryService.WritePolicyJson(json, _policyVersion);
    }

    private sealed class PolicyPayload
    {
        public bool LockdownEnabled { get; set; }
        public bool StealthEnabled { get; set; }
        public List<string> AllowedProcesses { get; set; } = [];
    }

    // ── INotifyPropertyChanged ──────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
