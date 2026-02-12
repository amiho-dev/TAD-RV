// ───────────────────────────────────────────────────────────────────────────
// MainWindow.xaml.cs — WebView2 host + C#↔JS bridge
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Embeds the management console SPA inside WebView2. All service calls
// (driver status, registry, deployment, event log) are bridged via
// PostWebMessageAsJson / WebMessageReceived.
// ───────────────────────────────────────────────────────────────────────────

using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using TadConsole.Services;

namespace TadConsole.Views;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly TadServiceController _svcCtrl = new();
    private readonly RegistryService _registry = new();
    private readonly EventLogService _eventLog = new();
    private readonly SystemInfoService _sysInfo = new();
    private readonly DeploymentService _deploy = new();

    private CancellationTokenSource? _deployCts;

    public MainWindow()
    {
        InitializeComponent();
        InitializeWebView();
    }

    // ─── WebView2 Init ───────────────────────────────────────────────

    private async void InitializeWebView()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await WebView.EnsureCoreWebView2Async(env);

            // Load embedded SPA
            WebView.CoreWebView2.NavigateToString(LoadEmbeddedHtml());

            // Bridge: JS → C#
            WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            // Send initial elevated status
            await Task.Delay(500); // let page render
            SendToWeb("elevated", new { isElevated = _sysInfo.IsElevated });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"WebView2 initialization failed:\n\n{ex.Message}\n\n" +
                "Make sure Microsoft Edge WebView2 Runtime is installed.",
                "TAD.RV — Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string LoadEmbeddedHtml()
    {
        var asm = Assembly.GetExecutingAssembly();
        var css = LoadResource(asm, "TadConsole.Web.app.css");
        var js  = LoadResource(asm, "TadConsole.Web.app.js");
        var html = LoadResource(asm, "TadConsole.Web.index.html");

        // Inline CSS & JS into the HTML
        html = html.Replace("<!-- INLINE_CSS -->", $"<style>\n{css}\n</style>");
        html = html.Replace("<!-- INLINE_JS -->",  $"<script>\n{js}\n</script>");
        return html;
    }

    private static string LoadResource(Assembly asm, string name)
    {
        using var stream = asm.GetManifestResourceStream(name);
        if (stream == null) return $"/* resource '{name}' not found */";
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // ─── JS → C# Message Handler ────────────────────────────────────

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            string action = root.GetProperty("action").GetString() ?? "";

            switch (action)
            {
                case "queryServices":
                    await HandleQueryServices();
                    break;

                case "getSystemInfo":
                    HandleGetSystemInfo();
                    break;

                case "queryRegistry":
                    HandleQueryRegistry();
                    break;

                case "runHealthChecks":
                    await HandleHealthChecks();
                    break;

                case "getEvents":
                    HandleGetEvents();
                    break;

                case "startService":
                    await HandleStartService(root);
                    break;

                case "stopService":
                    await HandleStopService(root);
                    break;

                case "deploy":
                    await HandleDeploy(root);
                    break;

                case "cancelDeploy":
                    _deployCts?.Cancel();
                    break;

                case "savePolicy":
                    HandleSavePolicy(root);
                    break;

                case "loadPolicy":
                    HandleLoadPolicy();
                    break;

                case "resetProvisioning":
                    HandleResetProvisioning();
                    break;

                case "importPolicy":
                    HandleImportPolicy();
                    break;

                case "exportPolicy":
                    HandleExportPolicy(root);
                    break;

                case "browseFile":
                    HandleBrowseFile(root);
                    break;

                case "browseFolder":
                    HandleBrowseFolder(root);
                    break;

                // ── Admin — Classroom Designer ────────────────────────
                case "admin_save_rooms":
                    HandleAdminSaveRooms(root);
                    break;

                case "admin_load_rooms":
                    HandleAdminLoadRooms();
                    break;

                case "admin_import_layout":
                    HandleAdminImportLayout();
                    break;

                case "admin_export_layouts":
                    HandleAdminExportLayouts(root);
                    break;
            }
        }
        catch (Exception ex)
        {
            SendToWeb("toast", new { level = "error", text = $"Bridge error: {ex.Message}" });
        }
    }

    // ─── Service Handlers ────────────────────────────────────────────

    private async Task HandleQueryServices()
    {
        var (driver, bridge) = await _svcCtrl.QueryAllAsync();
        SendToWeb("servicesStatus", new { driver, bridge });
    }

    private void HandleGetSystemInfo()
    {
        SendToWeb("systemInfo", new
        {
            hostname = _sysInfo.Hostname,
            osVersion = _sysInfo.OSVersion,
            userDomain = _sysInfo.UserDomain,
            currentUser = _sysInfo.CurrentUser,
            dotNetVersion = _sysInfo.DotNetVersion,
            processorCount = _sysInfo.ProcessorCount,
            systemUptime = _sysInfo.SystemUptime,
            memoryUsage = _sysInfo.MemoryUsage,
            isElevated = _sysInfo.IsElevated,
        });
    }

    private void HandleQueryRegistry()
    {
        var cfg = _registry.ReadConfig();
        SendToWeb("registryConfig", new
        {
            keyExists = cfg.KeyExists,
            installDir = cfg.InstallDir,
            domainController = cfg.DomainController,
            deployedAt = cfg.DeployedAt,
            provisioned = cfg.Provisioned,
            machineDN = cfg.MachineDN,
            organizationalUnit = cfg.OrganizationalUnit,
            policyVersion = cfg.PolicyVersion,
        });
    }

    private async Task HandleHealthChecks()
    {
        var checks = new List<object>();

        // 1. Driver installed
        var driver = await _svcCtrl.QueryServiceAsync(TadServiceController.DriverServiceName);
        checks.Add(new { name = "Kernel Driver Installed", passed = driver.Exists, detail = driver.Status });

        // 2. Driver running
        checks.Add(new { name = "Kernel Driver Running", passed = driver.Status == "RUNNING", detail = driver.Status });

        // 3. Bridge service installed
        var bridge = await _svcCtrl.QueryServiceAsync(TadServiceController.BridgeServiceName);
        checks.Add(new { name = "Bridge Service Installed", passed = bridge.Exists, detail = bridge.Status });

        // 4. Bridge service running
        checks.Add(new { name = "Bridge Service Running", passed = bridge.Status == "RUNNING", detail = bridge.Status });

        // 5. Registry key exists
        var reg = _registry.ReadConfig();
        checks.Add(new { name = "Registry Key Present", passed = reg.KeyExists, detail = reg.KeyExists ? "HKLM\\SOFTWARE\\TAD_RV" : "Not Found" });

        // 6. Administrator
        checks.Add(new { name = "Running as Administrator", passed = _sysInfo.IsElevated, detail = _sysInfo.IsElevated ? "Elevated" : "Standard" });

        SendToWeb("healthChecks", checks);
    }

    private void HandleGetEvents()
    {
        var events = _eventLog.ReadRecentEvents(200);
        SendToWeb("events", events.Select(e => new
        {
            timeStamp = e.TimeStamp,
            level = e.Level,
            eventId = e.EventId,
            source = e.Source,
            message = e.Message,
        }).ToArray());
    }

    private async Task HandleStartService(JsonElement root)
    {
        string svc = root.TryGetProperty("service", out var s) ? s.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(svc)) return;
        try
        {
            await _svcCtrl.StartServiceAsync(svc);
            SendToWeb("toast", new { level = "success", text = $"{svc} started" });
        }
        catch (Exception ex)
        {
            SendToWeb("toast", new { level = "error", text = $"Failed to start {svc}: {ex.Message}" });
        }
        await HandleQueryServices();
    }

    private async Task HandleStopService(JsonElement root)
    {
        string svc = root.TryGetProperty("service", out var s) ? s.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(svc)) return;
        try
        {
            await _svcCtrl.StopServiceAsync(svc);
            SendToWeb("toast", new { level = "success", text = $"{svc} stopped" });
        }
        catch (Exception ex)
        {
            SendToWeb("toast", new { level = "error", text = $"Failed to stop {svc}: {ex.Message}" });
        }
        await HandleQueryServices();
    }

    // ─── Deployment ──────────────────────────────────────────────────

    private async Task HandleDeploy(JsonElement root)
    {
        if (!root.TryGetProperty("config", out var cfgEl)) return;

        var config = new DeploymentConfig
        {
            DriverPath       = cfgEl.TryGetProperty("driverPath", out var dp) ? dp.GetString() ?? "" : "",
            ServicePath      = cfgEl.TryGetProperty("servicePath", out var sp) ? sp.GetString() ?? "" : "",
            TargetDir        = cfgEl.TryGetProperty("targetDir", out var td) ? td.GetString() ?? @"C:\Program Files\TAD_RV" : @"C:\Program Files\TAD_RV",
            DomainController = cfgEl.TryGetProperty("domainController", out var dc) ? dc.GetString() ?? "" : "",
            InstallDriver    = cfgEl.TryGetProperty("installDriver", out var id) && id.GetBoolean(),
            InstallService   = cfgEl.TryGetProperty("installService", out var isvc) && isvc.GetBoolean(),
        };

        _deployCts = new CancellationTokenSource();
        var progress = new Progress<int>(pct =>
        {
            Dispatcher.InvokeAsync(() => SendToWeb("deployProgress", new { percent = pct }));
        });

        _deploy.StepCompleted += OnDeployStep;
        _deploy.LogMessage += OnDeployLog;

        try
        {
            var results = await _deploy.DeployAsync(config, progress, _deployCts.Token);
            bool allOk = results.All(r => r.Success);
            SendToWeb("deployComplete", new { success = allOk });
        }
        catch (OperationCanceledException)
        {
            SendToWeb("deployComplete", new { success = false });
            SendToWeb("toast", new { level = "warning", text = "Deployment cancelled" });
        }
        catch (Exception ex)
        {
            SendToWeb("deployComplete", new { success = false });
            SendToWeb("toast", new { level = "error", text = $"Deployment failed: {ex.Message}" });
        }
        finally
        {
            _deploy.StepCompleted -= OnDeployStep;
            _deploy.LogMessage -= OnDeployLog;
        }
    }

    private void OnDeployStep(DeploymentStepResult step)
    {
        Dispatcher.InvokeAsync(() => SendToWeb("deployStep", new
        {
            name = step.StepName,
            success = step.Success,
            message = step.Message,
            durationMs = (int)step.Duration.TotalMilliseconds,
        }));
    }

    private void OnDeployLog(string msg)
    {
        Dispatcher.InvokeAsync(() => SendToWeb("deployLog", new { text = msg }));
    }

    // ─── Policy Handlers ────────────────────────────────────────────

    private void HandleSavePolicy(JsonElement root)
    {
        string json = root.TryGetProperty("json", out var j) ? j.GetString() ?? "{}" : "{}";
        int version = root.TryGetProperty("version", out var v) ? v.GetInt32() : 1;

        try
        {
            _registry.WritePolicyJson(json, version);
            SendToWeb("toast", new { level = "success", text = $"Policy v{version} saved to registry" });
        }
        catch (Exception ex)
        {
            SendToWeb("toast", new { level = "error", text = $"Failed to save policy: {ex.Message}" });
        }
    }

    private void HandleLoadPolicy()
    {
        var cfg = _registry.ReadConfig();
        if (!cfg.KeyExists || string.IsNullOrEmpty(cfg.PolicyJson))
        {
            SendToWeb("policyLoaded", new { version = 0, flags = new Dictionary<string, bool>() });
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(cfg.PolicyJson);
            var root = doc.RootElement;
            var flags = new Dictionary<string, bool>();

            if (root.TryGetProperty("flags", out var flagsEl))
            {
                foreach (var prop in flagsEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.True || prop.Value.ValueKind == JsonValueKind.False)
                        flags[prop.Name] = prop.Value.GetBoolean();
                }
            }

            SendToWeb("policyLoaded", new { version = cfg.PolicyVersion, flags });
        }
        catch
        {
            SendToWeb("policyLoaded", new { version = cfg.PolicyVersion, flags = new Dictionary<string, bool>() });
        }
    }

    private void HandleResetProvisioning()
    {
        try
        {
            _registry.ResetProvisioning();
            SendToWeb("toast", new { level = "warning", text = "Provisioning flag reset" });
        }
        catch (Exception ex)
        {
            SendToWeb("toast", new { level = "error", text = $"Failed: {ex.Message}" });
        }
    }

    private void HandleImportPolicy()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Import Policy JSON"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                string json = File.ReadAllText(dlg.FileName);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var flags = new Dictionary<string, bool>();
                int version = 0;

                if (root.TryGetProperty("version", out var v)) version = v.GetInt32();
                if (root.TryGetProperty("flags", out var flagsEl))
                {
                    foreach (var prop in flagsEl.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.True || prop.Value.ValueKind == JsonValueKind.False)
                            flags[prop.Name] = prop.Value.GetBoolean();
                    }
                }

                SendToWeb("policyLoaded", new { version, flags });
                SendToWeb("toast", new { level = "success", text = $"Policy imported from {Path.GetFileName(dlg.FileName)}" });
            }
            catch (Exception ex)
            {
                SendToWeb("toast", new { level = "error", text = $"Import failed: {ex.Message}" });
            }
        }
    }

    private void HandleExportPolicy(JsonElement root)
    {
        string json = root.TryGetProperty("json", out var j) ? j.GetString() ?? "{}" : "{}";

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            FileName = "Policy.json",
            Title = "Export Policy JSON"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dlg.FileName, json);
                SendToWeb("toast", new { level = "success", text = $"Policy exported to {Path.GetFileName(dlg.FileName)}" });
            }
            catch (Exception ex)
            {
                SendToWeb("toast", new { level = "error", text = $"Export failed: {ex.Message}" });
            }
        }
    }

    // ─── Browse Dialogs ─────────────────────────────────────────────

    private void HandleBrowseFile(JsonElement root)
    {
        string inputId = root.TryGetProperty("inputId", out var id) ? id.GetString() ?? "" : "";

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Driver files (*.sys)|*.sys|All files (*.*)|*.*",
            Title = "Select File"
        };

        if (dlg.ShowDialog() == true)
        {
            SendToWeb("browseResult", new { inputId, path = dlg.FileName });
        }
    }

    private void HandleBrowseFolder(JsonElement root)
    {
        string inputId = root.TryGetProperty("inputId", out var id) ? id.GetString() ?? "" : "";

        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Folder",
            UseDescriptionForTitle = true,
        };

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SendToWeb("browseResult", new { inputId, path = dlg.SelectedPath });
        }
    }

    // ─── Admin — Classroom Persistence ─────────────────────────────

    private static string RoomsFilePath
    {
        get
        {
            // Try exe directory first (portable), fallback to user's Documents
            string exeDir = AppContext.BaseDirectory;
            string exePath = Path.Combine(exeDir, "TAD_Classrooms.json");
            try
            {
                if (!File.Exists(exePath))
                    File.WriteAllText(exePath, "[]");
                return exePath;
            }
            catch
            {
                string docsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TAD");
                Directory.CreateDirectory(docsDir);
                return Path.Combine(docsDir, "TAD_Classrooms.json");
            }
        }
    }

    private void HandleAdminSaveRooms(JsonElement root)
    {
        string jsonData = root.TryGetProperty("data", out var d) ? d.GetString() ?? "[]" : "[]";
        try
        {
            File.WriteAllText(RoomsFilePath, jsonData, Encoding.UTF8);
            SendToWeb("toast", new { level = "success", text = "Classrooms saved" });
        }
        catch (Exception ex)
        {
            SendToWeb("toast", new { level = "error", text = $"Save failed: {ex.Message}" });
        }
    }

    private void HandleAdminLoadRooms()
    {
        try
        {
            string data = "[]";
            if (File.Exists(RoomsFilePath))
                data = File.ReadAllText(RoomsFilePath, Encoding.UTF8);
            SendToWeb("admin_rooms_loaded", data);
        }
        catch (Exception ex)
        {
            SendToWeb("toast", new { level = "error", text = $"Load failed: {ex.Message}" });
        }
    }

    private void HandleAdminImportLayout()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Classroom Layout",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                var data = File.ReadAllText(dlg.FileName, Encoding.UTF8);
                SendToWeb("admin_layout_imported", data);
            }
            catch (Exception ex)
            {
                SendToWeb("toast", new { level = "error", text = $"Import failed: {ex.Message}" });
            }
        }
    }

    private void HandleAdminExportLayouts(JsonElement root)
    {
        string jsonData = root.TryGetProperty("data", out var d) ? d.GetString() ?? "[]" : "[]";

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Classroom Layouts",
            Filter = "JSON files (*.json)|*.json",
            DefaultExt = ".json",
            FileName = "TAD_Classrooms.json"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dlg.FileName, jsonData, Encoding.UTF8);
                SendToWeb("toast", new { level = "success", text = $"Exported to {Path.GetFileName(dlg.FileName)}" });
            }
            catch (Exception ex)
            {
                SendToWeb("toast", new { level = "error", text = $"Export failed: {ex.Message}" });
            }
        }
    }

    // ─── Send to WebView2 ───────────────────────────────────────────

    private void SendToWeb(string type, object data)
    {
        try
        {
            var msg = JsonSerializer.Serialize(new { type, data }, JsonOpts);
            WebView.CoreWebView2?.PostWebMessageAsJson(msg);
        }
        catch { /* WebView2 may not be ready */ }
    }

    protected override void OnClosed(EventArgs e)
    {
        _deployCts?.Cancel();
        base.OnClosed(e);
    }
}
