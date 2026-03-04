// ───────────────────────────────────────────────────────────────────────────
// TadOverlay — Fullscreen overlay for teacher-controlled screen commands
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Usage:
//   TadOverlay.exe --lock      Dark fullscreen lock screen with lock icon
//   TadOverlay.exe --blank     Pure black fullscreen (eyes on teacher)
//
// Launched by TADBridgeService via CreateProcessAsUser in the user's session.
// Killed by the service when the teacher unlocks/unblanks.
//
// Lock mode features:
//   • Covers ALL monitors (multi-screen)
//   • Low-level keyboard hook (WH_KEYBOARD_LL) blocks Alt+Tab, Win, Ctrl+Esc
//   • FormClosing is canceled — students cannot close it
//   • TopMost + no taskbar entry
//   • Re-activates if somehow loses focus
// ───────────────────────────────────────────────────────────────────────────

using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TADOverlay;

static class Program
{
    // Low-level keyboard hook handle
    private static IntPtr _hookId = IntPtr.Zero;
    private static LowLevelKeyboardProc? _hookProc;

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string mode = "lock"; // default
        foreach (var a in args)
        {
            var arg = a.TrimStart('-', '/').ToLowerInvariant();
            if (arg is "lock" or "blank" or "freeze")
            {
                // Freeze → Lock (freeze is removed, redirect)
                mode = arg == "freeze" ? "lock" : arg;
                break;
            }
        }

        // For lock mode, install low-level keyboard hook to block Alt+Tab, Win, Ctrl+Esc
        if (mode == "lock")
        {
            InstallKeyboardHook();
        }

        if (mode == "lock")
        {
            // Lock: create a form covering ALL screens
            var forms = BuildLockOverlay();
            // Run the primary form (first screen) — others show modeless
            for (int i = 1; i < forms.Length; i++)
                forms[i].Show();
            Application.Run(forms[0]);
        }
        else
        {
            // Blank: single fullscreen form
            var form = BuildBlankOverlay();
            Application.Run(form);
        }

        // Unhook on exit
        if (_hookId != IntPtr.Zero)
            UnhookWindowsHookEx(_hookId);
    }

    // ── Lock Overlay (multi-monitor) ─────────────────────────────────
    static Form[] BuildLockOverlay()
    {
        var bg = Color.FromArgb(13, 17, 23);  // #0D1117
        var screens = Screen.AllScreens;
        var forms = new Form[screens.Length];

        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var form = MakeFullscreenForm(bg, 1.0);
            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = screen.Bounds;

            if (screen.Primary)
            {
                // Primary monitor: show lock icon + message
                var icon = new Label
                {
                    Text      = "\U0001F512",
                    Font      = new Font("Segoe UI Emoji", 64f, FontStyle.Regular),
                    ForeColor = Color.White,
                    BackColor = bg,
                    AutoSize  = true
                };

                var msg = new Label
                {
                    Text      = "This workstation is locked by the teacher",
                    Font      = new Font("Segoe UI", 22f, FontStyle.Regular),
                    ForeColor = Color.FromArgb(201, 209, 217),
                    BackColor = bg,
                    AutoSize  = true
                };

                var sub = new Label
                {
                    Text      = "Please wait for your teacher to unlock this screen",
                    Font      = new Font("Segoe UI", 11f, FontStyle.Regular),
                    ForeColor = Color.FromArgb(139, 148, 158),
                    BackColor = bg,
                    AutoSize  = true
                };

                form.Controls.AddRange(new Control[] { icon, msg, sub });

                form.Load += (_, _) =>
                {
                    CenterLabel(icon, form, -60);
                    CenterLabel(msg, form, icon.Bottom + 16 - form.ClientSize.Height / 2 + 60);
                    CenterLabel(sub, form, msg.Bottom + 8 - form.ClientSize.Height / 2 + 60);
                };

                form.Resize += (_, _) =>
                {
                    CenterLabel(icon, form, -60);
                    CenterLabel(msg, form, icon.Bottom + 16 - form.ClientSize.Height / 2 + 60);
                    CenterLabel(sub, form, msg.Bottom + 8 - form.ClientSize.Height / 2 + 60);
                };
            }
            // Secondary monitors: just solid background, no text needed

            forms[i] = form;
        }

        return forms;
    }

    // ── Blank Overlay ────────────────────────────────────────────────────
    static Form BuildBlankOverlay()
    {
        return MakeFullscreenForm(Color.Black, 1.0);
    }

    // ── Shared Form Builder ──────────────────────────────────────────────
    static Form MakeFullscreenForm(Color bgColor, double opacity)
    {
        var form = new Form
        {
            BackColor       = bgColor,
            FormBorderStyle = FormBorderStyle.None,
            WindowState     = FormWindowState.Maximized,
            TopMost         = true,
            ShowInTaskbar   = false,
            Opacity         = opacity,
            StartPosition   = FormStartPosition.Manual,
            Location        = Point.Empty,
            Cursor          = Cursors.Arrow
        };

        // Prevent Alt+F4 and other close attempts
        form.FormClosing += (_, e) =>
        {
            if (e.CloseReason != CloseReason.TaskManagerClosing &&
                e.CloseReason != CloseReason.WindowsShutDown)
            {
                e.Cancel = true;
            }
        };

        // Also suppress keyboard at the form level as backup
        form.KeyPreview = true;
        form.KeyDown += (_, e) =>
        {
            if (e.Alt || e.Control || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin ||
                e.KeyCode == Keys.Escape || e.KeyCode == Keys.Tab)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        // Re-activate if somehow loses focus (timer-based)
        var focusTimer = new System.Windows.Forms.Timer { Interval = 500 };
        focusTimer.Tick += (_, _) =>
        {
            if (!form.Focused && !form.ContainsFocus)
            {
                form.TopMost = true;
                form.BringToFront();
                form.Activate();
            }
        };
        form.Load += (_, _) => focusTimer.Start();
        form.FormClosed += (_, _) => focusTimer.Stop();

        return form;
    }

    static void CenterLabel(Label lbl, Form form, int yOffset)
    {
        lbl.Left = (form.ClientSize.Width - lbl.Width) / 2;
        lbl.Top  = (form.ClientSize.Height - lbl.Height) / 2 + yOffset;
    }

    // ══════════════════════════════════════════════════════════════════
    // Low-Level Keyboard Hook — blocks Alt+Tab, Win key, Ctrl+Esc
    // ══════════════════════════════════════════════════════════════════

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN    = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private static void InstallKeyboardHook()
    {
        _hookProc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc,
            GetModuleHandle(curModule.ModuleName), 0);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint vk = hookStruct.vkCode;

            // Block: Left Win (0x5B), Right Win (0x5C)
            if (vk == 0x5B || vk == 0x5C)
                return (IntPtr)1;

            // Block: Alt+Tab (Alt flag in SYSKEYDOWN + Tab)
            if (vk == 0x09 && (hookStruct.flags & 0x20) != 0) // VK_TAB + LLKHF_ALTDOWN
                return (IntPtr)1;

            // Block: Alt+Esc
            if (vk == 0x1B && (hookStruct.flags & 0x20) != 0) // VK_ESCAPE + LLKHF_ALTDOWN
                return (IntPtr)1;

            // Block: Alt+F4
            if (vk == 0x73 && (hookStruct.flags & 0x20) != 0) // VK_F4 + LLKHF_ALTDOWN
                return (IntPtr)1;

            // Block: Ctrl+Esc (opens start menu)
            if (vk == 0x1B && (Control.ModifierKeys & Keys.Control) != 0)
                return (IntPtr)1;
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}
