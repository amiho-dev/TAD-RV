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

namespace TadTeacher;

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
            using var stream = asm.GetManifestResourceStream("TadTeacher.Assets.logo32.b64");
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
        var version = asm.GetName().Version?.ToString() ?? "1.0.0.0";

        // Get build date from assembly informational version or linker timestamp
        string buildDate;
        var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (infoVer != null && infoVer.InformationalVersion.Contains('-'))
        {
            buildDate = infoVer.InformationalVersion;
        }
        else
        {
            // Fallback: use file write time
            var loc = asm.Location;
            buildDate = !string.IsNullOrEmpty(loc) && File.Exists(loc)
                ? File.GetLastWriteTime(loc).ToString("yyyy-MM-dd")
                : DateTime.Now.ToString("yyyy-MM-dd");
        }

        TxtVersion.Text = $"Version {version}  •  Build {buildDate}";
    }
}
