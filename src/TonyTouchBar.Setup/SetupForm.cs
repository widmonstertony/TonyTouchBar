using System.Diagnostics;

namespace TonyTouchBar.Setup;

internal sealed class SetupForm : Form
{
    private readonly Label status = new();
    private readonly ProgressBar progress = new();
    private readonly Button install = new();
    private readonly CheckBox codex = new();
    private readonly Label detail = new();

    public SetupForm()
    {
        Text = "TonyTouchBar Setup";
        ClientSize = new Size(720, 480);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(10, 14, 18);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        Controls.Add(new Label {
            Text = "TonyTouchBar", Font = new Font("Segoe UI Semibold", 26),
            ForeColor = Color.FromArgb(91, 218, 194), AutoSize = true, Location = new Point(42, 30)
        });
        Controls.Add(new Label {
            Text = "Bring your MacBook Pro Touch Bar to life on Windows.",
            ForeColor = Color.FromArgb(190, 200, 208), AutoSize = true, Location = new Point(46, 82)
        });

        var featureText = "✓ Keeps Secure Boot enabled\r\n✓ Follows games, video and foreground apps\r\n✓ Installs the Windows app, WSL bridge and startup entry\r\n✓ No terminal commands or manual Ubuntu setup";
        Controls.Add(new Label {
            Text = featureText, AutoSize = true, Location = new Point(48, 130),
            ForeColor = Color.FromArgb(220, 226, 230), Font = new Font("Segoe UI", 11),
            Padding = new Padding(0, 0, 0, 5)
        });

        codex.Text = "Enable Codex Live status (recommended)";
        codex.Checked = true;
        codex.AutoSize = true;
        codex.Location = new Point(48, 250);
        Controls.Add(codex);
        Controls.Add(new Label {
            Text = "Codex will ask you once to review and trust its local lifecycle hooks.",
            AutoSize = true, Location = new Point(69, 278), ForeColor = Color.FromArgb(145, 158, 168)
        });

        status.Text = "Ready to install";
        status.AutoSize = false;
        status.Size = new Size(620, 24);
        status.Location = new Point(48, 330);
        Controls.Add(status);

        detail.Text = "On a new PC, Windows may require one restart while enabling WSL. Setup resumes automatically.";
        detail.AutoSize = false;
        detail.Size = new Size(620, 42);
        detail.Location = new Point(48, 357);
        detail.ForeColor = Color.FromArgb(145, 158, 168);
        Controls.Add(detail);

        progress.Location = new Point(48, 407);
        progress.Size = new Size(470, 20);
        progress.Style = ProgressBarStyle.Continuous;
        Controls.Add(progress);

        install.Text = "Install";
        install.Size = new Size(132, 42);
        install.Location = new Point(540, 397);
        install.BackColor = Color.FromArgb(35, 118, 105);
        install.FlatStyle = FlatStyle.Flat;
        install.FlatAppearance.BorderSize = 0;
        install.ForeColor = Color.White;
        install.Click += InstallClick;
        Controls.Add(install);
    }

    private async void InstallClick(object? sender, EventArgs e)
    {
        install.Enabled = false;
        codex.Enabled = false;
        var engine = new InstallerEngine((message, value) => BeginInvoke(() => {
            status.Text = message;
            progress.Value = Math.Clamp(value, 0, 100);
        }));
        try
        {
            var result = await Task.Run(() => engine.Install(codex.Checked));
            progress.Value = 100;
            if (result.NeedsRestart)
            {
                status.Text = "Windows needs one restart to finish enabling WSL.";
                detail.Text = "Setup has saved your progress and will reopen automatically after sign-in.";
                install.Text = "Restart now";
                install.Enabled = true;
                install.Click -= InstallClick;
                install.Click += (_, _) => Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = true });
                return;
            }

            status.Text = "TonyTouchBar is installed and running.";
            detail.Text = codex.Checked
                ? "Restart Codex, enable TonyTouchBar Codex Live, then approve its one-time hook trust prompt."
                : "You can enable Codex Live later from the TonyTouchBar installer.";
            install.Text = "Close";
            install.Enabled = true;
            install.Click -= InstallClick;
            install.Click += (_, _) => Close();
        }
        catch (Exception ex)
        {
            status.Text = "Setup could not finish.";
            detail.Text = ex.Message;
            install.Text = "Try again";
            install.Enabled = true;
            codex.Enabled = true;
        }
    }
}
