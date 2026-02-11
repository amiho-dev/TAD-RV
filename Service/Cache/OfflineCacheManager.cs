// ───────────────────────────────────────────────────────────────────────────
// OfflineCacheManager.cs — Encrypted Local Cache for Offline AD Verification
//
// When the Domain Controller is unreachable, the service must still resolve
// the current user's role to maintain monitoring continuity.
//
// This cache:
//   1. Stores the last successful AD resolution (SID + role + groups)
//   2. Encrypts the cache using DPAPI (System scope) — only SYSTEM can read
//   3. The driver protects the cache file via the minifilter (anti-deletion)
//   4. Cache entries expire after 7 days to force re-validation
//
// The cache file is stored in:
//   %ProgramData%\TAD_RV\offline_cache.dat
// ───────────────────────────────────────────────────────────────────────────

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TadBridge.Shared;

namespace TadBridge.Cache;

/// <summary>
/// Manages the DPAPI-encrypted offline user resolution cache.
/// </summary>
public sealed class OfflineCacheManager
{
    private static readonly string CacheDir  = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "TAD_RV");

    private static readonly string CacheFile = Path.Combine(CacheDir, "offline_cache.dat");

    /// <summary>Cache entries older than this are considered expired.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);

    private readonly ILogger<OfflineCacheManager> _log;

    public OfflineCacheManager(ILogger<OfflineCacheManager> logger)
    {
        _log = logger;
        EnsureCacheDirectory();
    }

    // ─── Write ───────────────────────────────────────────────────────

    /// <summary>
    /// Persists a successful AD resolution to the encrypted local cache.
    /// </summary>
    public void CacheUserResolution(string sid, TadUserRole role, List<string> groups)
    {
        try
        {
            var entry = new CacheEntry
            {
                Sid        = sid,
                Role       = role,
                Groups     = groups,
                CachedAtUtc = DateTime.UtcNow,
                MachineName = Environment.MachineName
            };

            string json = JsonSerializer.Serialize(entry);
            byte[] plaintext = Encoding.UTF8.GetBytes(json);

            // Encrypt with DPAPI (LocalMachine scope — only SYSTEM can decrypt)
            byte[] encrypted = ProtectedData.Protect(
                plaintext,
                GetEntropy(),
                DataProtectionScope.LocalMachine
            );

            File.WriteAllBytes(CacheFile, encrypted);

            _log.LogDebug("Offline cache updated for SID {Sid}, role={Role}", sid, role);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to write offline cache");
        }
    }

    // ─── Read ────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the cached user resolution.  Returns null if the cache is
    /// missing, corrupted, or expired.
    /// </summary>
    public CacheEntry? LoadCachedResolution()
    {
        try
        {
            if (!File.Exists(CacheFile))
            {
                _log.LogDebug("Offline cache file not found");
                return null;
            }

            byte[] encrypted = File.ReadAllBytes(CacheFile);
            byte[] plaintext = ProtectedData.Unprotect(
                encrypted,
                GetEntropy(),
                DataProtectionScope.LocalMachine
            );

            string json = Encoding.UTF8.GetString(plaintext);
            var entry = JsonSerializer.Deserialize<CacheEntry>(json);

            if (entry == null)
            {
                _log.LogWarning("Offline cache deserialized to null");
                return null;
            }

            // Check expiry
            if (DateTime.UtcNow - entry.CachedAtUtc > CacheTtl)
            {
                _log.LogWarning("Offline cache expired (cached at {Time})", entry.CachedAtUtc);
                return null;
            }

            // Verify machine name (prevent cache transplant attacks)
            if (!string.Equals(entry.MachineName, Environment.MachineName,
                    StringComparison.OrdinalIgnoreCase))
            {
                _log.LogWarning("Offline cache machine mismatch: {Cached} vs {Current}",
                    entry.MachineName, Environment.MachineName);
                return null;
            }

            _log.LogDebug("Offline cache loaded: SID={Sid}, Role={Role}", entry.Sid, entry.Role);
            return entry;
        }
        catch (CryptographicException ex)
        {
            _log.LogWarning(ex, "Offline cache decryption failed — may be corrupt");
            return null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to read offline cache");
            return null;
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────

    private void EnsureCacheDirectory()
    {
        try
        {
            if (!Directory.Exists(CacheDir))
            {
                Directory.CreateDirectory(CacheDir);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to create cache directory {Dir}", CacheDir);
        }
    }

    /// <summary>
    /// Additional entropy for DPAPI.  Prevents cross-application decryption
    /// even under the same SYSTEM account.
    /// </summary>
    private static byte[] GetEntropy()
    {
        // SHA256("TAD.RV.OfflineCache.Entropy.v1")
        return
        [
            0x3A, 0x7F, 0x1C, 0xD2, 0x91, 0xE4, 0x58, 0xB6,
            0x0D, 0xC3, 0x72, 0xAF, 0x45, 0x8E, 0x1D, 0x9B,
            0xF0, 0x63, 0x27, 0xDA, 0x84, 0x5C, 0xE1, 0x09,
            0xBB, 0x46, 0x7D, 0xF8, 0x23, 0x95, 0x6A, 0xCE
        ];
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Cache entry model
// ═══════════════════════════════════════════════════════════════════════════

public sealed class CacheEntry
{
    public string        Sid         { get; set; } = string.Empty;
    public TadUserRole   Role        { get; set; } = TadUserRole.Unknown;
    public List<string>  Groups      { get; set; } = [];
    public DateTime      CachedAtUtc { get; set; }
    public string        MachineName { get; set; } = string.Empty;
}
