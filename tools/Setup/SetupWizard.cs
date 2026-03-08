using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TADSetup
{
    public class SetupWizard : Form
    {
        public bool CreateDesktopShortcut { get; private set; } = true;
        public bool LaunchImmediately { get; private set; } = true;
        public bool InstallConfirmed { get; private set; } = false;

        public SetupWizard(string appDisplayName, string installDir, bool isService)
        {
            this.Text = $"{appDisplayName} Installer";
            this.Size = new Size(500, 360);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.White;

            var title = new Label { Text = $"Welcome to the {appDisplayName} Setup Wizard", Font = new Font("Segoe UI", 14, FontStyle.Bold), AutoSize = true, Location = new Point(20, 20) };
            this.Controls.Add(title);

            var info = new Label { 
                Text = $"This wizard will install {appDisplayName} on your computer.\n\nInstall path:\n{installDir}", 
                Font = new Font("Segoe UI", 10), 
                AutoSize = true, 
                Location = new Point(25, 70),
                MaximumSize = new Size(440, 0)
            };
            this.Controls.Add(info);

            var chkDesktop = new CheckBox { Text = "Create a Desktop Shortcut", Checked = true, AutoSize = true, Location = new Point(30, 150), Font = new Font("Segoe UI", 10) };
            this.Controls.Add(chkDesktop);

            var chkLaunch = new CheckBox { Text = isService ? "Start Service immediately after install" : "Launch App after install", Checked = true, AutoSize = true, Location = new Point(30, 180), Font = new Font("Segoe UI", 10) };
            this.Controls.Add(chkLaunch);

            var line = new Label { BorderStyle = BorderStyle.Fixed3D, Location = new Point(0, 260), Size = new Size(500, 2) };
            this.Controls.Add(line);

            var btnInstall = new Button { Text = "Install", Location = new Point(300, 275), Size = new Size(80, 30), BackColor = SystemColors.Control, Font = new Font("Segoe UI", 9) };
            var btnCancel = new Button { Text = "Cancel", Location = new Point(390, 275), Size = new Size(80, 30), BackColor = SystemColors.Control, Font = new Font("Segoe UI", 9) };

            btnInstall.Click += (s, e) => {
                this.CreateDesktopShortcut = chkDesktop.Checked;
                this.LaunchImmediately = chkLaunch.Checked;
                this.InstallConfirmed = true;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            btnCancel.Click += (s, e) => {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };

            this.Controls.Add(btnInstall);
            this.Controls.Add(btnCancel);
            this.AcceptButton = btnInstall;
            this.CancelButton = btnCancel;
        }
    }
}
