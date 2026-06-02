using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace TADBridge.Shared.Licensing;

public sealed class LicenseState
{
    public bool IsLicensed { get; init; }
    public bool IsTrial { get; init; }
    public bool NeedsActivation { get; init; }
    public bool IsTampered { get; init; }
    public int TrialDaysRemaining { get; init; }
    public string DeviceSerial { get; init; } = "";
    public string Message { get; init; } = "";
}

public static class TADLicenseManager
{
    private const int TrialDays = 40;
    private const string ProductName = "TAD-RV";
    private const string LicenseFileName = "license.key";
    private const string TrialFileName = "trial.state";
    private const string RegistryRoot = "SOFTWARE\\TAD-RV\\Licensing";
    private const string TrialStartValue = "TrialStartUtc";
    private const string LastSeenValue = "LastSeenUtc";
    private const string InstallFingerprintValue = "InstallFingerprint";

    // Replace with the real activation public key from your licensing backend.
    // SubjectPublicKeyInfo DER, Base64.
    private const string ActivationPublicKeySpkiBase64 =
        "MFwwDQYJKoZIhvcNAQEBBQADSwAwSAJBAKk6j+ClQnQ5m9rkQwX0YwYH6Q0J7mzc5Qm3Z2P0x7a0Wk4xE4h2VfS6GfR4QfL7uJmM6d2i6x1zD4dG7A8PvM8CAwEAAQ==";

    public static LicenseState EnsureLicense(string edition)
    {
        string serial = GetDeviceSerial();

        // 1) Activation key has priority.
        if (TryReadActivationToken(out string token) &&
            TryValidateActivationToken(token, edition, serial, out string activationError))
        {
            return new LicenseState
            {
                IsLicensed = true,
                IsTrial = false,
                NeedsActivation = false,
                DeviceSerial = serial,
                Message = "Activated"
            };
        }

        // 2) Trial path with anti-reset checks.
        TrialState trial = LoadOrCreateTrial(serial);
        DateTime utcNow = DateTime.UtcNow;

        if (trial.IsTampered)
        {
            return new LicenseState
            {
                IsLicensed = false,
                IsTrial = false,
                NeedsActivation = true,
                IsTampered = true,
                DeviceSerial = serial,
                Message = "Trial data tampering detected. Activation key required."
            };
        }

        if (utcNow < trial.LastSeenUtc.AddHours(-24))
        {
            return new LicenseState
            {
                IsLicensed = false,
                IsTrial = false,
                NeedsActivation = true,
                IsTampered = true,
                DeviceSerial = serial,
                Message = "System clock rollback detected. Activation key required."
            };
        }

        DateTime trialEnd = trial.StartUtc.AddDays(TrialDays);
        int daysLeft = (int)Math.Ceiling((trialEnd - utcNow).TotalDays);

        if (daysLeft <= 0)
        {
            return new LicenseState
            {
                IsLicensed = false,
                IsTrial = false,
                NeedsActivation = true,
                DeviceSerial = serial,
                Message = "Trial expired. Please enter a valid product key."
            };
        }

        SaveTrial(trial with { LastSeenUtc = utcNow }, serial);

        return new LicenseState
        {
            IsLicensed = true,
            IsTrial = true,
            TrialDaysRemaining = daysLeft,
            NeedsActivation = false,
            DeviceSerial = serial,
            Message = $"Trial active: {daysLeft} day(s) remaining."
        };
    }

    public static bool TryActivate(string activationKey, string edition, out string error)
    {
        error = "";
        string serial = GetDeviceSerial();

        if (!TryValidateActivationToken(activationKey, edition, serial, out error))
            return false;

        try
        {
            string path = GetLicenseFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, activationKey, Encoding.UTF8);

            using RegistryKey? key = Registry.LocalMachine.CreateSubKey(RegistryRoot);
            key?.SetValue("LicenseHash", ComputeSha256Hex(activationKey), RegistryValueKind.String);
            key?.SetValue(InstallFingerprintValue, BuildInstallFingerprint(serial), RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            error = "Activation write failed: " + ex.Message;
            return false;
        }
    }

    private static bool TryValidateActivationToken(string token, string edition, string serial, out string error)
    {
        error = "";
        try
        {
            string[] parts = token.Trim().Split('.');
            if (parts.Length != 2)
            {
                error = "Invalid key format.";
                return false;
            }

            byte[] payloadBytes = Base64UrlDecode(parts[0]);
            byte[] sigBytes = Base64UrlDecode(parts[1]);

            using RSA rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(ActivationPublicKeySpkiBase64), out _);

            bool validSig = rsa.VerifyData(payloadBytes, sigBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (!validSig)
            {
                error = "Signature mismatch.";
                return false;
            }

            ActivationPayload payload = JsonSerializer.Deserialize<ActivationPayload>(payloadBytes)
                ?? new ActivationPayload();

            if (string.IsNullOrWhiteSpace(payload.SerialHash))
            {
                error = "Invalid payload.";
                return false;
            }

            string serialHash = ComputeSha256Hex(serial + "|" + ProductName);
            if (!string.Equals(payload.SerialHash, serialHash, StringComparison.OrdinalIgnoreCase))
            {
                error = "This key is not valid for this device serial.";
                return false;
            }

            if (!string.Equals(payload.Edition, "all", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(payload.Edition, edition, StringComparison.OrdinalIgnoreCase))
            {
                error = "Key edition does not match this product edition.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(payload.ExpiresUtc) &&
                DateTime.TryParse(payload.ExpiresUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime expUtc) &&
                DateTime.UtcNow > expUtc)
            {
                error = "Activation key expired.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = "Activation validation failed: " + ex.Message;
            return false;
        }
    }

    private static TrialState LoadOrCreateTrial(string serial)
    {
        TrialState? fileState = TryReadTrialFile(serial);
        TrialState? regState = TryReadTrialRegistry(serial);

        if (fileState == null && regState == null)
        {
            TrialState fresh = new()
            {
                StartUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow,
                InstallFingerprint = BuildInstallFingerprint(serial)
            };
            SaveTrial(fresh, serial);
            return fresh;
        }

        if (fileState == null || regState == null)
        {
            return new TrialState { IsTampered = true, StartUtc = DateTime.UtcNow, LastSeenUtc = DateTime.UtcNow };
        }

        if (!string.Equals(fileState.InstallFingerprint, regState.InstallFingerprint, StringComparison.Ordinal) ||
            fileState.StartUtc != regState.StartUtc)
        {
            return new TrialState { IsTampered = true, StartUtc = DateTime.UtcNow, LastSeenUtc = DateTime.UtcNow };
        }

        return fileState with
        {
            LastSeenUtc = fileState.LastSeenUtc > regState.LastSeenUtc ? fileState.LastSeenUtc : regState.LastSeenUtc
        };
    }

    private static void SaveTrial(TrialState state, string serial)
    {
        string path = GetTrialFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string json = JsonSerializer.Serialize(state);
        File.WriteAllText(path, json, Encoding.UTF8);

        using RegistryKey? key = Registry.LocalMachine.CreateSubKey(RegistryRoot);
        key?.SetValue(TrialStartValue, state.StartUtc.ToString("O"), RegistryValueKind.String);
        key?.SetValue(LastSeenValue, state.LastSeenUtc.ToString("O"), RegistryValueKind.String);
        key?.SetValue(InstallFingerprintValue, state.InstallFingerprint, RegistryValueKind.String);
        key?.SetValue("SerialHash", ComputeSha256Hex(serial + "|" + ProductName), RegistryValueKind.String);
    }

    private static TrialState? TryReadTrialFile(string serial)
    {
        try
        {
            string path = GetTrialFilePath();
            if (!File.Exists(path)) return null;

            TrialState? state = JsonSerializer.Deserialize<TrialState>(File.ReadAllText(path, Encoding.UTF8));
            if (state == null) return null;
            if (!string.Equals(state.InstallFingerprint, BuildInstallFingerprint(serial), StringComparison.Ordinal))
                state = state with { IsTampered = true };
            return state;
        }
        catch
        {
            return null;
        }
    }

    private static TrialState? TryReadTrialRegistry(string serial)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(RegistryRoot, false);
            if (key == null) return null;

            string? start = key.GetValue(TrialStartValue) as string;
            string? last = key.GetValue(LastSeenValue) as string;
            string? fp = key.GetValue(InstallFingerprintValue) as string;
            if (string.IsNullOrWhiteSpace(start) || string.IsNullOrWhiteSpace(last) || string.IsNullOrWhiteSpace(fp))
                return null;

            if (!DateTime.TryParse(start, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime startUtc))
                return null;
            if (!DateTime.TryParse(last, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime lastUtc))
                return null;

            bool tampered = !string.Equals(fp, BuildInstallFingerprint(serial), StringComparison.Ordinal);
            return new TrialState
            {
                StartUtc = startUtc,
                LastSeenUtc = lastUtc,
                InstallFingerprint = fp,
                IsTampered = tampered
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadActivationToken(out string token)
    {
        token = "";
        try
        {
            string path = GetLicenseFilePath();
            if (!File.Exists(path)) return false;
            token = File.ReadAllText(path, Encoding.UTF8).Trim();
            return !string.IsNullOrWhiteSpace(token);
        }
        catch
        {
            return false;
        }
    }

    private static string GetLicenseFilePath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(root, ProductName, "licensing", LicenseFileName);
    }

    private static string GetTrialFilePath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(root, ProductName, "licensing", TrialFileName);
    }

    public static string GetDeviceSerial()
    {
        // Use stable machine-bound material to prevent cross-device activation reuse.
        string machineGuid = "unknown";
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Cryptography", false);
            machineGuid = key?.GetValue("MachineGuid")?.ToString() ?? "unknown";
        }
        catch { }

        string biosSerial = "unknown";
        try
        {
            using var mos = new System.Management.ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BIOS");
            foreach (var item in mos.Get())
            {
                biosSerial = item["SerialNumber"]?.ToString() ?? "unknown";
                break;
            }
        }
        catch { }

        return (biosSerial + "|" + machineGuid).Trim();
    }

    private static string BuildInstallFingerprint(string serial)
    {
        string basis = serial + "|" + ProductName + "|v2026-apr-final";
        return ComputeSha256Hex(basis);
    }

    private static string ComputeSha256Hex(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private static byte[] Base64UrlDecode(string input)
    {
        string s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    private sealed class ActivationPayload
    {
        public string SerialHash { get; set; } = "";
        public string Edition { get; set; } = "all";
        public string ExpiresUtc { get; set; } = "";
    }

    private sealed record TrialState
    {
        public DateTime StartUtc { get; init; }
        public DateTime LastSeenUtc { get; init; }
        public string InstallFingerprint { get; init; } = "";
        public bool IsTampered { get; init; }
    }
}
