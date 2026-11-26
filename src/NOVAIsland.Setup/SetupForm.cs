using System.Drawing;

namespace NOVAIsland.Setup;

internal sealed class SetupForm : Form
{
    private readonly TextBox _pathBox;
    private readonly CheckBox _browsersCheck;
    private readonly CheckBox _desktopCheck;
    private readonly CheckBox _startupCheck;
    private readonly CheckBox _launchCheck;
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;
    private readonly Button _installButton;
    private readonly Button _browseButton;
    private readonly Button _cancelButton;

    public SetupForm(string defaultInstallDir)
    {
        Text = "DOOM Island Setup v2.3.1";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(540, 395);
        BackColor = Color.FromArgb(14, 15, 20);
        ForeColor = Color.FromArgb(244, 244, 246);
        Font = new Font("Segoe UI", 9.5f);

        var titleLabel = new Label
        {
            Text = "Install DOOM Island",
            Font = new Font("Segoe UI Semibold", 16f),
            ForeColor = Color.White,
            Location = new Point(24, 20),
            AutoSize = true
        };

        var subtitleLabel = new Label
        {
            Text = "Native Windows Dynamic Island with automatic Chrome, Edge & Opera media sync",
            ForeColor = Color.FromArgb(161, 161, 175),
            Location = new Point(26, 54),
            Size = new Size(490, 22)
        };

        var dirLabel = new Label
        {
            Text = "Installation folder",
            ForeColor = Color.FromArgb(212, 212, 220),
            Location = new Point(26, 92),
            AutoSize = true
        };

        _pathBox = new TextBox
        {
            Text = defaultInstallDir,
            Location = new Point(26, 116),
            Size = new Size(400, 27),
            BackColor = Color.FromArgb(24, 26, 34),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        _browseButton = new Button
        {
            Text = "Browse...",
            Location = new Point(434, 115),
            Size = new Size(80, 27),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(32, 35, 46),
            ForeColor = Color.White
        };
        _browseButton.FlatAppearance.BorderColor = Color.FromArgb(55, 60, 78);
        _browseButton.Click += OnBrowseClick;

        _browsersCheck = new CheckBox
        {
            Text = "Auto-connect Chrome, Edge & Opera media (SMTC + Native Messaging host)",
            Checked = true,
            Location = new Point(26, 162),
            Size = new Size(490, 24),
            ForeColor = Color.FromArgb(228, 228, 236)
        };

        _desktopCheck = new CheckBox
        {
            Text = "Create Desktop shortcut",
            Checked = true,
            Location = new Point(26, 192),
            Size = new Size(320, 24),
            ForeColor = Color.FromArgb(228, 228, 236)
        };

        _startupCheck = new CheckBox
        {
            Text = "Start DOOM Island automatically when Windows starts",
            Checked = false,
            Location = new Point(26, 222),
            Size = new Size(420, 24),
            ForeColor = Color.FromArgb(228, 228, 236)
        };

        _launchCheck = new CheckBox
        {
            Text = "Launch DOOM Island right after installation",
            Checked = true,
            Location = new Point(26, 252),
            Size = new Size(380, 24),
            ForeColor = Color.FromArgb(228, 228, 236)
        };

        _statusLabel = new Label
        {
            Text = "Ready to install.",
            Location = new Point(26, 294),
            Size = new Size(488, 20),
            ForeColor = Color.FromArgb(148, 163, 184)
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(26, 318),
            Size = new Size(488, 10),
            Style = ProgressBarStyle.Continuous
        };

        _installButton = new Button
        {
            Text = "Install",
            Location = new Point(332, 344),
            Size = new Size(92, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(122, 162, 247),
            ForeColor = Color.FromArgb(10, 12, 18),
            Font = new Font("Segoe UI Semibold", 9.5f)
        };
        _installButton.FlatAppearance.BorderSize = 0;
        _installButton.Click += OnInstallClick;

        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(432, 344),
            Size = new Size(82, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(32, 35, 46),
            ForeColor = Color.White
        };
        _cancelButton.FlatAppearance.BorderColor = Color.FromArgb(55, 60, 78);
        _cancelButton.Click += (_, _) => Close();

        Controls.AddRange(
        [
            titleLabel, subtitleLabel, dirLabel, _pathBox, _browseButton,
            _browsersCheck, _desktopCheck, _startupCheck, _launchCheck,
            _statusLabel, _progressBar, _installButton, _cancelButton
        ]);
    }

    private void OnBrowseClick(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            SelectedPath = _pathBox.Text,
            Description = "Choose where to install DOOM Island"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
        {
            _pathBox.Text = dlg.SelectedPath;
        }
    }

    private async void OnInstallClick(object? sender, EventArgs e)
    {
        _installButton.Enabled = false;
        _browseButton.Enabled = false;
        _pathBox.Enabled = false;
        _cancelButton.Enabled = false;

        var opts = new InstallOptions(
            InstallDir: _pathBox.Text.Trim(),
            RegisterBrowsers: _browsersCheck.Checked,
            CreateDesktopShortcut: _desktopCheck.Checked,
            LaunchOnStartup: _startupCheck.Checked,
            LaunchAfterInstall: _launchCheck.Checked,
            Silent: false);

        try
        {
            await Task.Run(() => InstallerEngine.Install(
                opts,
                status => BeginInvoke(() => _statusLabel.Text = status),
                (done, total) => BeginInvoke(() =>
                {
                    _progressBar.Maximum = total;
                    _progressBar.Value = Math.Min(done, total);
                })));

            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Installation failed:\n{ex.Message}", "DOOM Island Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _installButton.Enabled = true;
            _browseButton.Enabled = true;
            _pathBox.Enabled = true;
            _cancelButton.Enabled = true;
            _statusLabel.Text = "Installation failed.";
        }
    }
}
