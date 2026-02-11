// ───────────────────────────────────────────────────────────────────────────
// PolicyViewModel.cs — Visual policy editor with live JSON preview
// ───────────────────────────────────────────────────────────────────────────

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using TadConsole.Helpers;
using TadConsole.Services;

namespace TadConsole.ViewModels;

/// <summary>
/// Mutable policy model for two-way binding in the UI.
/// </summary>
public sealed class PolicyModel : ViewModelBase
{
    private int _version = 1;
    public int Version { get => _version; set => SetProperty(ref _version, value); }

    private bool _blockUsb;
    public bool BlockUsb { get => _blockUsb; set { SetProperty(ref _blockUsb, value); FlagsChanged?.Invoke(); } }

    private bool _blockPrinting;
    public bool BlockPrinting { get => _blockPrinting; set { SetProperty(ref _blockPrinting, value); FlagsChanged?.Invoke(); } }

    private bool _logScreenshots;
    public bool LogScreenshots { get => _logScreenshots; set { SetProperty(ref _logScreenshots, value); FlagsChanged?.Invoke(); } }

    private bool _blockTaskManager;
    public bool BlockTaskManager { get => _blockTaskManager; set { SetProperty(ref _blockTaskManager, value); FlagsChanged?.Invoke(); } }

    private bool _enforceWebFilter;
    public bool EnforceWebFilter { get => _enforceWebFilter; set { SetProperty(ref _enforceWebFilter, value); FlagsChanged?.Invoke(); } }

    private int _heartbeatIntervalMs = 2000;
    public int HeartbeatIntervalMs { get => _heartbeatIntervalMs; set => SetProperty(ref _heartbeatIntervalMs, value); }

    private int _heartbeatTimeoutMs = 6000;
    public int HeartbeatTimeoutMs { get => _heartbeatTimeoutMs; set => SetProperty(ref _heartbeatTimeoutMs, value); }

    private string _blockedApps = "taskmgr.exe\ncmd.exe\npowershell.exe\npwsh.exe\nregedit.exe";
    public string BlockedApps { get => _blockedApps; set => SetProperty(ref _blockedApps, value); }

    private string _groupMappings = "Domain Students = Student\nDomain Teachers = Teacher\nDomain Admins = Admin\nTAD-Administrators = Admin";
    public string GroupMappings { get => _groupMappings; set => SetProperty(ref _groupMappings, value); }

    public int ComputedFlags =>
        (BlockUsb ? 0x01 : 0) |
        (BlockPrinting ? 0x02 : 0) |
        (LogScreenshots ? 0x04 : 0) |
        (BlockTaskManager ? 0x08 : 0) |
        (EnforceWebFilter ? 0x10 : 0);

    public event Action? FlagsChanged;
}

public sealed class PolicyViewModel : ViewModelBase
{
    private readonly RegistryService _registry = new();

    public PolicyModel Policy { get; } = new();

    private string _jsonPreview = "";
    public string JsonPreview
    {
        get => _jsonPreview;
        set => SetProperty(ref _jsonPreview, value);
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private int _computedFlagsValue;
    public int ComputedFlagsValue
    {
        get => _computedFlagsValue;
        set => SetProperty(ref _computedFlagsValue, value);
    }

    // ── Commands ─────────────────────────────────────────────────────
    public ICommand LoadFromRegistryCommand       { get; }
    public ICommand SaveToRegistryCommand         { get; }
    public ICommand ExportJsonCommand             { get; }
    public ICommand ImportJsonCommand             { get; }
    public ICommand ResetProvisioningCommand      { get; }

    public PolicyViewModel()
    {
        LoadFromRegistryCommand  = new RelayCommand(LoadFromRegistry);
        SaveToRegistryCommand    = new RelayCommand(SaveToRegistry);
        ExportJsonCommand        = new RelayCommand(ExportJson);
        ImportJsonCommand        = new RelayCommand(ImportJson);
        ResetProvisioningCommand = new RelayCommand(ResetProvisioning);

        Policy.PropertyChanged += (_, _) => UpdateJsonPreview();
        Policy.FlagsChanged += () =>
        {
            ComputedFlagsValue = Policy.ComputedFlags;
            UpdateJsonPreview();
        };

        UpdateJsonPreview();
    }

    private void UpdateJsonPreview()
    {
        try
        {
            var obj = BuildPolicyObject();
            JsonPreview = JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            ComputedFlagsValue = Policy.ComputedFlags;
        }
        catch (Exception ex)
        {
            JsonPreview = $"// Error: {ex.Message}";
        }
    }

    private object BuildPolicyObject()
    {
        var mappings = new Dictionary<string, int>();
        foreach (string line in Policy.GroupMappings.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2)
            {
                string group = parts[0].Trim();
                string roleS = parts[1].Trim().ToLowerInvariant();
                int role = roleS switch
                {
                    "student" or "0" => 0,
                    "teacher" or "1" => 1,
                    "admin"   or "2" => 2,
                    _ => 0
                };
                mappings[group] = role;
            }
        }

        var blockedApps = Policy.BlockedApps
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        return new
        {
            Policy.Version,
            Flags               = Policy.ComputedFlags,
            Policy.HeartbeatIntervalMs,
            Policy.HeartbeatTimeoutMs,
            AllowedUnloadRoles  = 4, // Admin only
            GroupRoleMappings   = mappings,
            BlockedApplications = blockedApps,
        };
    }

    private void LoadFromRegistry()
    {
        try
        {
            var config = _registry.ReadConfig();
            if (!config.KeyExists || string.IsNullOrEmpty(config.PolicyJson))
            {
                StatusText = "No policy found in registry.";
                return;
            }

            ApplyJsonToModel(config.PolicyJson);
            StatusText = $"Loaded policy v{Policy.Version} from registry.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading: {ex.Message}";
        }
    }

    private void SaveToRegistry()
    {
        try
        {
            var obj = BuildPolicyObject();
            string json = JsonSerializer.Serialize(obj);
            _registry.WritePolicyJson(json, Policy.Version);
            StatusText = $"Policy v{Policy.Version} saved to registry.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error saving: {ex.Message}";
        }
    }

    private void ExportJson()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title    = "Export Policy.json",
            Filter   = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            FileName = "Policy.json",
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dlg.FileName, JsonPreview);
                StatusText = $"Exported to {dlg.FileName}";
            }
            catch (Exception ex)
            {
                StatusText = $"Export failed: {ex.Message}";
            }
        }
    }

    private void ImportJson()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Import Policy.json",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                string json = File.ReadAllText(dlg.FileName);
                ApplyJsonToModel(json);
                StatusText = $"Imported from {dlg.FileName}";
            }
            catch (Exception ex)
            {
                StatusText = $"Import failed: {ex.Message}";
            }
        }
    }

    private void ApplyJsonToModel(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("Version", out var vProp))
            Policy.Version = vProp.GetInt32();

        if (root.TryGetProperty("Flags", out var fProp))
        {
            int flags = fProp.GetInt32();
            Policy.BlockUsb         = (flags & 0x01) != 0;
            Policy.BlockPrinting    = (flags & 0x02) != 0;
            Policy.LogScreenshots   = (flags & 0x04) != 0;
            Policy.BlockTaskManager = (flags & 0x08) != 0;
            Policy.EnforceWebFilter = (flags & 0x10) != 0;
        }
        // Also accept "PolicyFlags" (from readme format)
        else if (root.TryGetProperty("PolicyFlags", out var pfProp))
        {
            int flags = pfProp.GetInt32();
            Policy.BlockUsb         = (flags & 0x01) != 0;
            Policy.BlockPrinting    = (flags & 0x02) != 0;
            Policy.LogScreenshots   = (flags & 0x04) != 0;
            Policy.BlockTaskManager = (flags & 0x08) != 0;
            Policy.EnforceWebFilter = (flags & 0x10) != 0;
        }

        if (root.TryGetProperty("HeartbeatIntervalMs", out var hiProp))
            Policy.HeartbeatIntervalMs = hiProp.GetInt32();

        if (root.TryGetProperty("HeartbeatTimeoutMs", out var htProp))
            Policy.HeartbeatTimeoutMs = htProp.GetInt32();

        if (root.TryGetProperty("BlockedApplications", out var baProp))
        {
            var apps = new List<string>();
            foreach (var item in baProp.EnumerateArray())
                apps.Add(item.GetString() ?? "");
            Policy.BlockedApps = string.Join("\n", apps);
        }

        if (root.TryGetProperty("GroupRoleMappings", out var gmProp))
        {
            var lines = new List<string>();
            foreach (var prop in gmProp.EnumerateObject())
            {
                string roleName = prop.Value.GetInt32() switch
                {
                    0 => "Student",
                    1 => "Teacher",
                    2 => "Admin",
                    3 => "Admin",
                    _ => "Student"
                };
                lines.Add($"{prop.Name} = {roleName}");
            }
            Policy.GroupMappings = string.Join("\n", lines);
        }
    }

    private void ResetProvisioning()
    {
        try
        {
            var result = MessageBox.Show(
                "This will reset the provisioning flag.\n" +
                "The bridge service will re-provision from AD on next start.\n\n" +
                "Continue?",
                "Reset Provisioning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _registry.ResetProvisioning();
                StatusText = "Provisioning flag reset. Restart TadBridgeService to re-provision.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }
}
