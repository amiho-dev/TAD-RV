// ─────────────────────────────────────────────────────────────────────────────
// TADUpdater.exe — Detached updater helper for TAD Setup EXEs
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Spawned by *Setup.exe --update after a newer installer has been downloaded.
// Because *Setup.exe exits immediately after spawning this process, this helper
// is free to overwrite the installed Setup EXE without "file in use" errors.
//
// Usage (called internally by Setup EXEs — not intended for direct use):
//   TADUpdater.exe <download-url> <dest-path> <caller-pid> [installer-args...]
//
//   <download-url>    HTTPS URL of the new Setup EXE asset on GitHub Releases
//   <dest-path>       Full path to write the downloaded EXE
//                     (e.g. C:\Program Files\TAD\TADClientSetup.exe)
//   <caller-pid>      PID of the *Setup.exe process that spawned us;
//                     we wait for it to exit before overwriting the file
//   [installer-args]  Arguments forwarded to the new installer
//                     (typically just "--install")
// ─────────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

// ── Argument parsing ──────────────────────────────────────────────────────────
//
// Mode A (download): TAD-Update.exe <url> <dest-path> <caller-pid> [installer-args...]
//   Downloads a new Setup EXE from URL, replaces dest-path, launches with args.
//
// Mode B (apply):    TAD-Update.exe --apply <setup-exe-path> <caller-pid>
//   Waits for caller to exit, then runs the already-downloaded setup EXE with --install.
//

PrintBanner();

if (args.Length >= 3 && args[0].Equals("--apply", StringComparison.OrdinalIgnoreCase))
{
    // Mode B: apply a pre-downloaded setup EXE
    string setupExe = args[1];
    if (!int.TryParse(args[2], out int applyPid))
    {
        Console.Error.WriteLine("Invalid caller PID.");
        return 1;
    }

    Console.WriteLine($"  Mode:      Apply downloaded installer");
    Console.WriteLine($"  Installer: {Path.GetFileName(setupExe)}");
    Console.WriteLine();

    // Wait for caller to exit
    Console.WriteLine("  [1/2] Waiting for caller process to exit...");
    try
    {
        using var caller = Process.GetProcessById(applyPid);
        if (!caller.WaitForExit(30_000))
            Warn("Caller did not exit within 30 s — attempting install anyway.");
        else
            Ok("Caller exited.");
    }
    catch (ArgumentException) { Ok("Caller already exited."); }
    Console.WriteLine();

    // Launch the downloaded setup EXE with --install
    Console.WriteLine("  [2/2] Launching installer...");
    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName        = setupExe,
            Arguments       = "--install",
            UseShellExecute = true,  // allow UAC elevation
        });
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  Update installer launched.");
        Console.ResetColor();
        await Task.Delay(1500);
        return 0;
    }
    catch (Exception ex)
    {
        Err($"Could not launch installer: {ex.Message}");
        Pause();
        return 1;
    }
}

// Mode A: download and replace
if (args.Length < 3)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  TAD-Update.exe <url> <dest-path> <caller-pid> [installer-args...]");
    Console.Error.WriteLine("  TAD-Update.exe --apply <setup-exe-path> <caller-pid>");
    return 1;
}

string downloadUrl   = args[0];
string destPath      = args[1];
string installerArgs = args.Length > 3 ? string.Join(" ", args[3..]) : "--install";

if (!int.TryParse(args[2], out int callerPid))
{
    Console.Error.WriteLine("Invalid caller PID.");
    return 1;
}

Console.WriteLine($"  Mode:      Download & replace");
Console.WriteLine($"  Updating:  {Path.GetFileName(destPath)}");
Console.WriteLine($"  Dest:      {destPath}");
Console.WriteLine();

// ── Wait for the caller (Setup EXE) to exit ───────────────────────────────────
Console.WriteLine("  [1/3] Waiting for caller process to exit...");
try
{
    using var caller = Process.GetProcessById(callerPid);
    if (!caller.WaitForExit(30_000))
    {
        Warn("Caller did not exit within 30 s — attempting update anyway.");
    }
    else
    {
        Ok("Caller exited.");
    }
}
catch (ArgumentException)
{
    Ok("Caller already exited.");
}
Console.WriteLine();

// ── Download the new installer ────────────────────────────────────────────────
Console.WriteLine("  [2/3] Downloading new installer...");

string tempPath = destPath + ".new";
try
{
    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

    using var http = new HttpClient();
    http.DefaultRequestHeaders.UserAgent.ParseAdd("TADUpdater/1.0");
    http.Timeout = TimeSpan.FromMinutes(10);

    using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
    response.EnsureSuccessStatusCode();

    long   total      = response.Content.Headers.ContentLength ?? -1;
    long   downloaded = 0;
    byte[] buffer     = new byte[81920];
    int    read;

    using (var fs     = File.Create(tempPath))
    using (var stream = await response.Content.ReadAsStreamAsync())
    {
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, read));
            downloaded += read;
            if (total > 0)
            {
                int pct = (int)(downloaded * 100L / total);
                Console.Write($"\r    Downloading: {pct,3}%  ({downloaded / 1024:N0} / {total / 1024:N0} KB)   ");
            }
        }
    }

    Console.WriteLine();
    Console.WriteLine();
    Ok($"Downloaded  →  {tempPath}  ({downloaded / 1024:N0} KB)");
}
catch (Exception ex)
{
    Err($"Download failed: {ex.Message}");
    try { File.Delete(tempPath); } catch { }
    Pause();
    return 1;
}

// ── Replace the installed Setup EXE ──────────────────────────────────────────
Console.WriteLine();
Console.WriteLine($"  [3/3] Installing update...");
try
{
    // Small retry loop — AV scanners sometimes hold the file briefly
    bool moved = false;
    for (int attempt = 0; attempt < 5; attempt++)
    {
        try
        {
            File.Move(tempPath, destPath, overwrite: true);
            moved = true;
            break;
        }
        catch (IOException)
        {
            if (attempt < 4) await Task.Delay(1000);
        }
    }

    if (!moved)
    {
        Err($"Could not replace {destPath} — file may still be locked. Try again.");
        Pause();
        return 1;
    }

    Ok($"Replaced  →  {destPath}");
}
catch (Exception ex)
{
    Err($"Replace failed: {ex.Message}");
    try { File.Delete(tempPath); } catch { }
    Pause();
    return 1;
}

// ── Launch the new installer ──────────────────────────────────────────────────
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"  Launching  {Path.GetFileName(destPath)} {installerArgs} ...");
Console.ResetColor();
Console.WriteLine();

try
{
    Process.Start(new ProcessStartInfo
    {
        FileName        = destPath,
        Arguments       = installerArgs,
        UseShellExecute = true,   // allow UAC elevation prompt if needed
    });
}
catch (Exception ex)
{
    Err($"Could not launch installer: {ex.Message}");
    Pause();
    return 1;
}

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("  Update complete — installer launched.");
Console.ResetColor();
await Task.Delay(1500);
return 0;

// ── Helpers ───────────────────────────────────────────────────────────────────

static void PrintBanner()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(@"  _____ _   ___     _   _           _       _           ");
    Console.WriteLine(@" |_   _/_\ |   \   | | | |_ __  ___| |_ ___| |_ ___ _ _ ");
    Console.WriteLine(@"   | |/ _ \| |) |  | |_| | '_ \/ _` |  _/ -_) '_(_-< '_|");
    Console.WriteLine(@"   |_/_/ \_\___/    \___/| .__/\__,_|\__\___|_| /__/_|  ");
    Console.WriteLine(@"                         |_|                             ");
    Console.ResetColor();
    Console.WriteLine("  TAD-Update — background installer helper");
    Console.WriteLine("  (C) 2026 TAD Europe");
    Console.WriteLine();
}

static void Ok(string msg)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("  ✓ " + msg);
    Console.ResetColor();
}

static void Err(string msg)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("  [ERROR] " + msg);
    Console.ResetColor();
}

static void Warn(string msg)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Error.WriteLine("  [WARN]  " + msg);
    Console.ResetColor();
}

static void Pause()
{
    Console.WriteLine("  Press any key to exit...");
    Console.ReadKey(true);
}
