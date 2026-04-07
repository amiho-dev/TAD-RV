using System.Drawing;
using System.Windows.Forms;

namespace TADBridge.Shared.Licensing;

public static class TadLicenseDialogs
{
    public static string? PromptForActivationKey(string serial, string title)
    {
        using Form form = new()
        {
            Text = title,
            Width = 680,
            Height = 300,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(0x16, 0x1B, 0x22),
            ForeColor = Color.FromArgb(0xC9, 0xD1, 0xD9)
        };

        Label info = new()
        {
            Left = 20,
            Top = 18,
            Width = 620,
            Height = 56,
            ForeColor = Color.FromArgb(0xC9, 0xD1, 0xD9),
            Text = "Enter your TAD activation key.\r\nThis key must match your device serial.",
        };

        TextBox serialBox = new()
        {
            Left = 20,
            Top = 82,
            Width = 620,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(0x0D, 0x11, 0x17),
            ForeColor = Color.FromArgb(0x58, 0xA6, 0xFF),
            Text = "Serial: " + serial
        };

        TextBox keyBox = new()
        {
            Left = 20,
            Top = 126,
            Width = 620,
            Height = 64,
            Multiline = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(0x0D, 0x11, 0x17),
            ForeColor = Color.FromArgb(0xE6, 0xED, 0xF3),
            ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "Activation key"
        };

        Button ok = new()
        {
            Text = "Activate",
            Left = 440,
            Width = 96,
            Top = 210,
            DialogResult = DialogResult.OK,
            BackColor = Color.FromArgb(0x58, 0xA6, 0xFF),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        ok.FlatAppearance.BorderSize = 0;

        Button cancel = new()
        {
            Text = "Exit",
            Left = 544,
            Width = 96,
            Top = 210,
            DialogResult = DialogResult.Cancel,
            BackColor = Color.FromArgb(0x30, 0x36, 0x3D),
            ForeColor = Color.FromArgb(0xC9, 0xD1, 0xD9),
            FlatStyle = FlatStyle.Flat
        };
        cancel.FlatAppearance.BorderSize = 0;

        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Controls.Add(info);
        form.Controls.Add(serialBox);
        form.Controls.Add(keyBox);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);

        return form.ShowDialog() == DialogResult.OK ? keyBox.Text.Trim() : null;
    }

    public static void ShowInfo(string message, string title)
    {
        MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public static void ShowError(string message, string title)
    {
        MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
