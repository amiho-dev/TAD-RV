// ───────────────────────────────────────────────────────────────────────────
// SplashScreen.xaml.cs — Branded splash screen with logo, version, build date
//
// (C) 2026 TAD Europe — https://tad-it.eu
// TAD.RV — The Greater Brother of the mighty te.comp NET.FX
// ───────────────────────────────────────────────────────────────────────────

using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TADAdmin;

public partial class SplashScreen : Window
{
    public SplashScreen()
    {
        InitializeComponent();
        LoadLogo();
        LoadVersionInfo();
    }

    public void SetStatus(string text)
    {
        TxtLoadingStatus.Text = text;
    }

    private void LoadLogo()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("TADAdmin.Assets.logo32.b64");
            if (stream == null) return;

            using var reader = new StreamReader(stream);
            string base64 = reader.ReadToEnd().Trim();
            byte[] bytes = Convert.FromBase64String(base64);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            LogoImage.Source = bitmap;
        }
        catch
        {
            // Logo is decorative — silently skip if missing
        }
    }

    private void LoadVersionInfo()
    {
        var asm = Assembly.GetExecutingAssembly();

        // Get clean version from InformationalVersion (e.g. "v26.3.04.123-admin")
        var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        string version;
        if (infoVer != null)
        {
            version = infoVer.InformationalVersion;
            // Strip +commitHash suffix if present
            var plusIdx = version.IndexOf('+');
            if (plusIdx >= 0) version = version[..plusIdx];
            // Strip -admin/-client suffix for display
            var dashIdx = version.LastIndexOf('-');
            if (dashIdx > 0) version = version[..dashIdx];
        }
        else
        {
            version = asm.GetName().Version?.ToString() ?? "1.0.0.0";
        }

        TxtVersion.Text = version;
    }
}
