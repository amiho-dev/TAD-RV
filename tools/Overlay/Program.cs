// ───────────────────────────────────────────────────────────────────────────
// TadOverlay — Fullscreen overlay for teacher-controlled screen commands
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Usage:
//   TadOverlay.exe --lock      Dark fullscreen with lock icon + message
//   TadOverlay.exe --freeze    Semi-transparent blue overlay with freeze msg
//   TadOverlay.exe --blank     Pure black fullscreen (eyes on teacher)
//
// Launched by TADBridgeService via CreateProcessAsUser in the user's session.
// Killed by the service when the teacher unlocks/unfreezes/unblanks.
// The FormClosing event is canceled so students cannot close it.
// No PowerShell dependency — pure .NET WinForms, instant startup.
// ───────────────────────────────────────────────────────────────────────────

using System;
using System.Drawing;
using System.Windows.Forms;

namespace TADOverlay;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string mode = "lock"; // default
        foreach (var a in args)
        {
            var arg = a.TrimStart('-', '/').ToLowerInvariant();
            if (arg is "lock" or "freeze" or "blank")
            {
                mode = arg;
                break;
            }
        }

        var form = mode switch
        {
            "freeze" => BuildFreezeOverlay(),
            "blank"  => BuildBlankOverlay(),
            _        => BuildLockOverlay()
        };

        Application.Run(form);
    }

    // ── Lock Overlay ─────────────────────────────────────────────────────
    // Dark background, centered lock icon + message
    static Form BuildLockOverlay()
    {
        var bg = Color.FromArgb(13, 17, 23);  // #0D1117

        var form = MakeFullscreenForm(bg, 1.0);

        // Lock icon
        var icon = new Label
        {
            Text      = "\U0001F512",
            Font      = new Font("Segoe UI Emoji", 64f, FontStyle.Regular),
            ForeColor = Color.White,
            BackColor = bg,
            AutoSize  = true
        };

        // Message text
        var msg = new Label
        {
            Text      = "This workstation is locked by the teacher",
            Font      = new Font("Segoe UI", 22f, FontStyle.Regular),
            ForeColor = Color.FromArgb(201, 209, 217),  // #C9D1D9
            BackColor = bg,
            AutoSize  = true
        };

        // Subtle subtext
        var sub = new Label
        {
            Text      = "Please wait for your teacher to unlock this screen",
            Font      = new Font("Segoe UI", 11f, FontStyle.Regular),
            ForeColor = Color.FromArgb(139, 148, 158),  // #8B949E
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

        return form;
    }

    // ── Freeze Overlay ───────────────────────────────────────────────────
    // Semi-transparent blue, centered snowflake + message
    static Form BuildFreezeOverlay()
    {
        var bg = Color.FromArgb(30, 60, 100);

        var form = MakeFullscreenForm(bg, 0.55);

        var icon = new Label
        {
            Text      = "\u2744",  // ❄
            Font      = new Font("Segoe UI Emoji", 56f, FontStyle.Regular),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            AutoSize  = true
        };

        var msg = new Label
        {
            Text      = "Screen frozen by the teacher",
            Font      = new Font("Segoe UI", 20f, FontStyle.Regular),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            AutoSize  = true
        };

        form.Controls.AddRange(new Control[] { icon, msg });

        form.Load += (_, _) =>
        {
            CenterLabel(icon, form, -40);
            CenterLabel(msg, form, icon.Bottom + 12 - form.ClientSize.Height / 2 + 40);
        };

        form.Resize += (_, _) =>
        {
            CenterLabel(icon, form, -40);
            CenterLabel(msg, form, icon.Bottom + 12 - form.ClientSize.Height / 2 + 40);
        };

        return form;
    }

    // ── Blank Overlay ────────────────────────────────────────────────────
    // Pure black, nothing visible
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

        // Suppress keyboard shortcuts: Alt+Tab, Alt+F4, Ctrl+Esc, Win key
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

        return form;
    }

    static void CenterLabel(Label lbl, Form form, int yOffset)
    {
        lbl.Left = (form.ClientSize.Width - lbl.Width) / 2;
        lbl.Top  = (form.ClientSize.Height - lbl.Height) / 2 + yOffset;
    }
}
