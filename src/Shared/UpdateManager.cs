// ───────────────────────────────────────────────────────────────────────────
// UpdateManager.cs — GitHub Release-based Software Update System
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Checks for new versions via GitHub Releases API and downloads update
// packages. Used by all TAD.RV components:
//
//   - Teacher:  checks on startup, shows notification in dashboard
//   - Console:  checks on startup, shows banner in main window
//   - Service:  checks periodically, auto-applies if configured
//   - Bootstrap: compares cached version on each GPO run
//
// Configuration:
//   - GitHub repo owner/name from HKLM\SOFTWARE\TAD_RV\UpdateRepo
//   - Fallback: environment variable TAD_UPDATE_REPO
//   - Default:  "tad-europe/TAD-RV" (can be self-hosted / private)
//
// Update flow:
//   1. Query https://api.github.com/repos/{owner}/{repo}/releases/latest
//   2. Compare version tag with current assembly version
//   3. Download matching asset (TadTeacher-*.zip, TadBridgeService-*.zip, etc.)
//   4. Extract to temp directory
//   5. Signal the caller to apply (swap binaries and restart)
//
// For push updates (teacher → students via service), the Teacher sends
// a TadCommand.PushUpdate command with the update payload.
// ───────────────────────────────────────────────────────────────────────────

using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

#if WINDOWS
using Microsoft.Win32;
#endif

namespace TadBridge.Shared;

// ═══════════════════════════════════════════════════════════════════════════
// Update Info
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Represents an available update discovered from a GitHub Release.
/// </summary>
public sealed class UpdateInfo
{
    /// <summary>New version string (e.g., "26200.173").</summary>
    public required string Version { get; init; }

    /// <summary>Release name / title.</summary>
    public required string Title { get; init; }

    /// <summary>Markdown release notes body.</summary>
    public required string ReleaseNotes { get; init; }

    /// <summary>ISO 8601 publication date.</summary>
    public required DateTime PublishedAt { get; init; }

    /// <summary>Direct download URL for the matching asset.</summary>
    public required string DownloadUrl { get; init; }

    /// <summary>Asset file name (e.g., "TadTeacher-26200.173-win-x64.zip").</summary>
    public required string AssetName { get; init; }

    /// <summary>Asset file size in bytes.</summary>
    public long AssetSizeBytes { get; init; }

    /// <summary>HTML URL to the release page on GitHub.</summary>
    public string HtmlUrl { get; init; } = "";

    /// <summary>True if this version is newer than the running version.</summary>
    public bool IsNewer { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════
// GitHub Release API Models (minimal)
// ═══════════════════════════════════════════════════════════════════════════

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = [];
}

internal sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = "";
}

// ═══════════════════════════════════════════════════════════════════════════
// Update Manager
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Checks for software updates via GitHub Releases and manages downloads.
///
/// Thread-safe. Designed to be used as a singleton across the application.
/// </summary>
public sealed class UpdateManager : IDisposable
{
    // ── Configuration ────────────────────────────────────────────────

    private const string DefaultRepo = "amiho-dev/TAD-RV";
    private const string GitHubApiBase = "https://api.github.com";
    private const string UserAgent = "TAD-RV-UpdateCheck/1.0";
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);

    // Asset name prefixes per component
    private const string TeacherAssetPrefix    = "TadTeacher";
    private const string ServiceAssetPrefix    = "TadBridgeService";
    private const string ConsoleAssetPrefix    = "TadConsole";
    private const string BootstrapAssetPrefix  = "TadBootstrap";
    private const string DriverAssetPrefix     = "TAD_RV";

    private readonly HttpClient _http;
    private readonly string _repoSlug;
    private readonly string _currentVersion;
    private readonly string _componentPrefix;

    private UpdateInfo? _cachedUpdate;
    private DateTime _lastCheck = DateTime.MinValue;

    /// <summary>Minimum interval between API checks to respect rate limits.</summary>
    public TimeSpan MinCheckInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Raised when a new update is discovered.</summary>
    public event Action<UpdateInfo>? OnUpdateAvailable;

    /// <summary>Raised during download with (bytesDownloaded, totalBytes).</summary>
    public event Action<long, long>? OnDownloadProgress;

    /// <summary>
    /// Create an UpdateManager for a specific component.
    /// </summary>
    /// <param name="component">One of: "teacher", "service", "console", "bootstrap", "driver"</param>
    /// <param name="currentVersion">Override version string. If null, reads from assembly.</param>
    /// <param name="repoSlug">Override GitHub repo (e.g., "org/repo"). If null, reads from registry/env.</param>
    public UpdateManager(string component, string? currentVersion = null, string? repoSlug = null)
    {
        _repoSlug = repoSlug ?? ResolveRepoSlug();
        _currentVersion = currentVersion ?? GetAssemblyVersion();
        _componentPrefix = component.ToLowerInvariant() switch
        {
            "teacher"   => TeacherAssetPrefix,
            "service"   => ServiceAssetPrefix,
            "console"   => ConsoleAssetPrefix,
            "bootstrap" => BootstrapAssetPrefix,
            "driver"    => DriverAssetPrefix,
            _           => component
        };

        _http = new HttpClient { Timeout = HttpTimeout };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    // ─── Public API ──────────────────────────────────────────────────

    /// <summary>
    /// Check for a newer release on GitHub.
    /// Returns null if the current version is up-to-date.
    /// Respects <see cref="MinCheckInterval"/> to avoid hammering the API.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        // Rate limit
        if (DateTime.UtcNow - _lastCheck < MinCheckInterval && _cachedUpdate != null)
            return _cachedUpdate.IsNewer ? _cachedUpdate : null;

        _lastCheck = DateTime.UtcNow;

        try
        {
            string url = $"{GitHubApiBase}/repos/{_repoSlug}/releases/latest";
            var response = await _http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);
            if (release == null || release.Prerelease)
                return null;

            // Parse version from tag (e.g., "v26200.173" → "26200.173")
            string tagVersion = release.TagName.TrimStart('v', 'V');

            bool isNewer = CompareVersions(tagVersion, _currentVersion) > 0;

            // Find matching asset for this component
            var asset = FindMatchingAsset(release.Assets);

            _cachedUpdate = new UpdateInfo
            {
                Version      = tagVersion,
                Title        = release.Name,
                ReleaseNotes = release.Body,
                PublishedAt  = release.PublishedAt,
                DownloadUrl  = asset?.BrowserDownloadUrl ?? "",
                AssetName    = asset?.Name ?? "",
                AssetSizeBytes = asset?.Size ?? 0,
                HtmlUrl      = release.HtmlUrl,
                IsNewer      = isNewer
            };

            if (isNewer)
                OnUpdateAvailable?.Invoke(_cachedUpdate);

            return isNewer ? _cachedUpdate : null;
        }
        catch
        {
            // Network failure, API unreachable — silently return null
            return null;
        }
    }

    /// <summary>
    /// Download the update asset to a local temp directory.
    /// Returns the path to the downloaded file, or null on failure.
    /// </summary>
    public async Task<string?> DownloadUpdateAsync(
        UpdateInfo update,
        string? destinationDir = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(update.DownloadUrl))
            return null;

        destinationDir ??= Path.Combine(Path.GetTempPath(), "TAD_RV_Update");
        Directory.CreateDirectory(destinationDir);

        string filePath = Path.Combine(destinationDir, update.AssetName);

        try
        {
            using var response = await _http.GetAsync(
                update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? update.AssetSizeBytes;
            long downloaded = 0;

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var dest = File.Create(filePath);
            var buffer = new byte[81920]; // 80 KB buffer
            int read;

            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                OnDownloadProgress?.Invoke(downloaded, totalBytes);
            }

            return filePath;
        }
        catch
        {
            // Clean up partial download
            try { if (File.Exists(filePath)) File.Delete(filePath); } catch { }
            return null;
        }
    }

    /// <summary>
    /// Apply a downloaded update by extracting it alongside the running executable.
    /// Returns true if extraction succeeded. The caller should then restart.
    /// </summary>
    public static bool ApplyUpdate(string zipPath, string? targetDir = null)
    {
        targetDir ??= AppContext.BaseDirectory;

        try
        {
            // Extract to a staging directory first
            string staging = Path.Combine(Path.GetTempPath(), "TAD_RV_Staging_" + Guid.NewGuid().ToString("N")[..8]);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true);

            // Move files from staging to target (overwrite)
            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(staging, file);
                string destPath = Path.Combine(targetDir, relativePath);

                string? destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);

                // Try to overwrite; if locked, rename the old file first
                try
                {
                    File.Copy(file, destPath, overwrite: true);
                }
                catch (IOException)
                {
                    // File is locked (currently running exe/dll) — rename old, copy new
                    string backup = destPath + ".old";
                    try { File.Delete(backup); } catch { }
                    File.Move(destPath, backup);
                    File.Copy(file, destPath);
                }
            }

            // Cleanup
            try { Directory.Delete(staging, recursive: true); } catch { }
            try { File.Delete(zipPath); } catch { }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get the cached update info from the last check, or null if no check was performed.
    /// </summary>
    public UpdateInfo? GetCachedUpdate() => _cachedUpdate?.IsNewer == true ? _cachedUpdate : null;

    /// <summary>
    /// Returns the current running version string.
    /// </summary>
    public string CurrentVersion => _currentVersion;

    // ─── Helpers ─────────────────────────────────────────────────────

    private GitHubAsset? FindMatchingAsset(List<GitHubAsset> assets)
    {
        // Primary: exact prefix match (e.g., "TadTeacher-26200.173-win-x64.zip")
        var match = assets.FirstOrDefault(a =>
            a.Name.StartsWith(_componentPrefix, StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        // Fallback: any zip containing the prefix
        match ??= assets.FirstOrDefault(a =>
            a.Name.Contains(_componentPrefix, StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        return match;
    }

    /// <summary>
    /// Compare two version strings. Returns > 0 if a > b, 0 if equal, less than 0 if a less than b.
    /// Handles formats like "26200.173", "26200.173.0.0", "v26200.173"
    /// </summary>
    internal static int CompareVersions(string a, string b)
    {
        a = a.TrimStart('v', 'V');
        b = b.TrimStart('v', 'V');

        var partsA = a.Split('.').Select(s => int.TryParse(s, out int v) ? v : 0).ToArray();
        var partsB = b.Split('.').Select(s => int.TryParse(s, out int v) ? v : 0).ToArray();

        int maxLen = Math.Max(partsA.Length, partsB.Length);

        for (int i = 0; i < maxLen; i++)
        {
            int va = i < partsA.Length ? partsA[i] : 0;
            int vb = i < partsB.Length ? partsB[i] : 0;

            if (va != vb) return va.CompareTo(vb);
        }

        return 0;
    }

    private static string GetAssemblyVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version;
        return ver != null ? $"{ver.Major}.{ver.Minor}" : "0.0";
    }

    private static string ResolveRepoSlug()
    {
        // 1. Environment variable
        string? envRepo = Environment.GetEnvironmentVariable("TAD_UPDATE_REPO");
        if (!string.IsNullOrWhiteSpace(envRepo)) return envRepo;

#if WINDOWS
        // 2. Registry
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\TAD_RV");
            string? regRepo = key?.GetValue("UpdateRepo") as string;
            if (!string.IsNullOrWhiteSpace(regRepo)) return regRepo;
        }
        catch { /* Registry not available */ }
#endif

        // 3. Default
        return DefaultRepo;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
