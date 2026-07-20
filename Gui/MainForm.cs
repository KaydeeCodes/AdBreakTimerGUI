using System.Diagnostics;
using System.Net.Http;
using AdBreakTimerGUI.Config;
using AdBreakTimerGUI.Engine;
using AdBreakTimerGUI.Gui;
using AdBreakTimerGUI.Server;

namespace AdBreakTimerGUI;

public partial class MainForm : Form
{
    private readonly WebServerHost _server = new();
    private static readonly HttpClient TestHttpClient = new();
    private AppSettings _settings = new();

    private static readonly Color ColorRunningOk = Color.ForestGreen;
    private static readonly Color ColorRunningWithErrors = Color.FromArgb(0xF5, 0xA6, 0x23);
    private static readonly Color ColorStopped = Color.Firebrick;

    private bool _hasErrorSinceStart;

    // Controls I need to reach from more than one method.
    private Panel _ledPanel = null!;
    private Label _lblStatus = null!;
    private Label _lblPort = null!;
    private Button _btnToggle = null!;
    private CheckBox _chkAutoStart = null!;
    private CheckBox _chkMinimizeTray = null!;
    private TextBox _txtBarUrl = null!;
    private TextBox _txtRadialUrl = null!;
    private TextBox _txtApiExample = null!;
    private TextBox _txtAdBreak = null!;
    private TextBox _txtAdFree = null!;
    private NumericUpDown _numAdBuffer = null!;
    private Button _btnSaveSettings = null!;

    private NotifyIcon _trayIcon = null!;
    private bool _reallyExiting;

    public MainForm()
    {
        InitializeComponent();

        Paths.EnsureConfigDirExists();
        Logger.StartFresh();

        _settings = JsonStore.Load<AppSettings>(Paths.SettingsFile) ?? new AppSettings();

        BuildUi();
        BuildTrayIcon();

        _server.StatusChanged += OnServerStatusChanged;
        Logger.ErrorLogged += OnErrorLogged;

        RefreshStatusDisplay();

        if (_settings.StartAutomatically)
            StartServer();
    }

    // ------------------------------------------------------------
    // Building the window
    // ------------------------------------------------------------
    // Reworked into a fixed header (status, quick toggles) plus a
    // TabControl for everything else, rather than one long stack of
    // sections. The old layout kept growing every time I added
    // something, this version has room for the Twitch tab to grow on
    // its own without pushing the Links or Timing tabs further down
    // the screen. Still built entirely in code, no visual designer.
    private void BuildUi()
    {
        Text = "Ad Break Timer";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        int y = 16;

        // ---- Status row: LED, status text, port, start/stop button ----
        _ledPanel = new Panel { Location = new Point(16, y), Size = new Size(16, 16) };
        _ledPanel.Paint += (_, e) =>
        {
            using var brush = new SolidBrush(_ledPanel.BackColor);
            e.Graphics.FillEllipse(brush, 0, 0, _ledPanel.Width - 1, _ledPanel.Height - 1);
        };

        _lblStatus = new Label { Location = new Point(40, y - 2), AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
        _lblPort = new Label { Location = new Point(40, y + 18), AutoSize = true, ForeColor = Color.Gray };

        _btnToggle = new Button { Location = new Point(300, y - 4), Size = new Size(120, 30) };
        _btnToggle.Click += (_, _) => ToggleServer();

        Controls.Add(_ledPanel);
        Controls.Add(_lblStatus);
        Controls.Add(_lblPort);
        Controls.Add(_btnToggle);

        y += 50;
        Controls.Add(NewSeparator(this, y));
        y += 16;

        // ---- Quick toggles, side by side to keep this to one row ----
        _chkAutoStart = new CheckBox { Location = new Point(16, y), AutoSize = true, Text = "Start on launch", Checked = _settings.StartAutomatically };
        _chkAutoStart.CheckedChanged += (_, _) =>
        {
            _settings.StartAutomatically = _chkAutoStart.Checked;
            JsonStore.Save(_settings, Paths.SettingsFile);
        };
        var tipAutoStart = new ToolTip();
        tipAutoStart.SetToolTip(_chkAutoStart, "Starts the service the moment the app opens");
        Controls.Add(_chkAutoStart);

        _chkMinimizeTray = new CheckBox { Location = new Point(180, y), AutoSize = true, Text = "Minimise to tray", Checked = _settings.MinimizeToTrayOnClose };
        _chkMinimizeTray.CheckedChanged += (_, _) =>
        {
            _settings.MinimizeToTrayOnClose = _chkMinimizeTray.Checked;
            JsonStore.Save(_settings, Paths.SettingsFile);
        };
        var tipMinTray = new ToolTip();
        tipMinTray.SetToolTip(_chkMinimizeTray, "The X button hides to the tray instead of quitting");
        Controls.Add(_chkMinimizeTray);

        y += 26;
        Controls.Add(NewSeparator(this, y));
        y += 16;

        // ---- Tab control: Links / Timing / Appearance / Twitch ----
        var tabs = new TabControl { Location = new Point(16, y), Size = new Size(404, 236) };

        var linksTab = new TabPage("Links");
        var timingTab = new TabPage("Timing");
        var appearanceTab = new TabPage("Appearance");
        var twitchTab = new TabPage("Twitch");
        tabs.TabPages.Add(linksTab);
        tabs.TabPages.Add(timingTab);
        tabs.TabPages.Add(appearanceTab);
        tabs.TabPages.Add(twitchTab);

        BuildLinksTab(linksTab);
        BuildTimingTab(timingTab);
        BuildAppearanceTab(appearanceTab);
        BuildTwitchTab(twitchTab);

        Controls.Add(tabs);
        y += tabs.Height + 16;

        // ---- Footer: open config folder / open log file ----
        Button btnOpenConfig = new() { Location = new Point(16, y), Size = new Size(195, 30), Text = "Open config folder" };
        btnOpenConfig.Click += (_, _) => Process.Start("explorer.exe", Paths.ConfigDir);

        Button btnOpenLog = new() { Location = new Point(225, y), Size = new Size(195, 30), Text = "Open log file" };
        btnOpenLog.Click += (_, _) =>
        {
            if (!File.Exists(Paths.LogFile)) File.WriteAllText(Paths.LogFile, "");
            Process.Start(new ProcessStartInfo(Paths.LogFile) { UseShellExecute = true });
        };

        Controls.Add(btnOpenConfig);
        Controls.Add(btnOpenLog);
        y += 40;

        // ---- Footer credit ----
        var lblFooter = new LinkLabel
        {
            Location = new Point(16, y),
            Size = new Size(404, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Gray,
            Text = "Made by Kaydee.Codes - Free to use, no data collected, ever."
        };
        lblFooter.LinkArea = new LinkArea(8, 12);
        lblFooter.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://kaydee.codes/") { UseShellExecute = true });
        Controls.Add(lblFooter);
        y += 26;

        ClientSize = new Size(440, y + 16);

        RefreshLinkText();
    }

    // ------------------------------------------------------------
    // Tab contents
    // ------------------------------------------------------------
    private void BuildLinksTab(TabPage tab)
    {
        int y = 12;
        _txtBarUrl = NewLinkRow(tab, "Bar overlay", y, true, out Button copyBar, out Button? testBar);
        y += 46;
        _txtRadialUrl = NewLinkRow(tab, "Radial overlay", y, true, out Button copyRadial, out Button? testRadial);
        y += 46;
        _txtApiExample = NewLinkRow(tab, "Example go command", y, false, out Button copyApi, out _);
        y += 46;

        copyBar.Click += (_, _) => CopyToClipboard(_txtBarUrl.Text);
        copyRadial.Click += (_, _) => CopyToClipboard(_txtRadialUrl.Text);
        copyApi.Click += (_, _) => CopyToClipboard(_txtApiExample.Text);
        testBar!.Click += (_, _) => FireTestCountdown("bar");
        testRadial!.Click += (_, _) => FireTestCountdown("radial");

        var hint = new Label
        {
            Location = new Point(12, y),
            Size = new Size(360, 40),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 7.5F),
            Text = "Test starts a plain 30 min countdown, just enough to position the Browser Source in OBS."
        };
        tab.Controls.Add(hint);
    }

    private void BuildTimingTab(TabPage tab)
    {
        int y = 16;
        tab.Controls.Add(new Label { Location = new Point(12, y + 3), AutoSize = true, Text = "Ad break length" });
        _txtAdBreak = new TextBox
        {
            Location = new Point(280, y),
            Size = new Size(100, 23),
            TextAlign = HorizontalAlignment.Center,
            Text = TimeParsing.SecsToHms(_settings.AdBreakSeconds)
        };
        tab.Controls.Add(_txtAdBreak);
        y += 32;

        tab.Controls.Add(new Label { Location = new Point(12, y + 3), AutoSize = true, Text = "Ad free interval" });
        _txtAdFree = new TextBox
        {
            Location = new Point(280, y),
            Size = new Size(100, 23),
            TextAlign = HorizontalAlignment.Center,
            Text = TimeParsing.SecsToHms(_settings.AdFreeSeconds)
        };
        tab.Controls.Add(_txtAdFree);
        y += 32;

        tab.Controls.Add(new Label { Location = new Point(12, y + 3), Size = new Size(260, 20), Text = "Gap before ad free countdown (s)" });
        _numAdBuffer = new NumericUpDown
        {
            Location = new Point(280, y),
            Size = new Size(100, 23),
            Minimum = 0,
            Maximum = 300,
            Value = Math.Clamp(_settings.AdBufferSeconds, 0, 300)
        };
        tab.Controls.Add(_numAdBuffer);
        y += 36;

        _btnSaveSettings = new Button { Location = new Point(12, y), Size = new Size(368, 30), Text = "Save settings" };
        _btnSaveSettings.Click += (_, _) => SaveAdTimingSettings();
        tab.Controls.Add(_btnSaveSettings);
    }

    private void BuildAppearanceTab(TabPage tab)
    {
        var hint = new Label
        {
            Location = new Point(12, 12),
            Size = new Size(360, 40),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8F),
            Text = "Bar and radial ring appearance, direction, size, thickness, rotation, and colours, in their own window since there's a fair bit to each."
        };
        tab.Controls.Add(hint);

        Button btnOverlaySettings = new() { Location = new Point(12, 60), Size = new Size(368, 30), Text = "Bar / Radial settings..." };
        btnOverlaySettings.Click += (_, _) => OpenOverlaySettings();
        tab.Controls.Add(btnOverlaySettings);
    }

    private void BuildTwitchTab(TabPage tab)
    {
        tab.Controls.Add(new Label { Location = new Point(12, 15), AutoSize = true, Text = "Not connected", ForeColor = Color.Gray });
        Button btnTwitchConnect = new() { Location = new Point(280, 12), Size = new Size(100, 26), Text = "Connect", Enabled = false };
        var tip = new ToolTip();
        tip.SetToolTip(btnTwitchConnect, "Coming in a future update");
        tab.Controls.Add(btnTwitchConnect);

        CheckBox chkAutoDetectAds = new()
        {
            Location = new Point(12, 50),
            AutoSize = true,
            Text = "Auto detect ads and run the overlay automatically",
            Checked = _settings.AutoDetectAds,
            Enabled = false
        };
        var tipAuto = new ToolTip();
        tipAuto.SetToolTip(chkAutoDetectAds, "Needs a connected Twitch account, coming in a future update");
        tab.Controls.Add(chkAutoDetectAds);

        var hint = new Label
        {
            Location = new Point(12, 78),
            Size = new Size(360, 40),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 7.5F),
            Text = "Turns on by itself the moment an account connects. Switch it off any time to go back to firing commands manually, e.g. from Streamer.bot."
        };
        tab.Controls.Add(hint);
    }

    // ------------------------------------------------------------
    // Shared small helpers
    // ------------------------------------------------------------
    private static Control NewSeparator(Control parent, int y) => new Panel
    {
        Location = new Point(16, y),
        Size = new Size(404, 1),
        BackColor = Color.Gainsboro
    };

    // One row inside a tab: a caption, a read only text box holding
    // the URL, a Copy button, and optionally a Test button. Same idea
    // as before, just now taking a parent so it can add itself to a
    // TabPage instead of always assuming it's going straight onto the
    // form, and using slightly tighter margins since a TabPage has
    // less usable width than the form itself once its own border is
    // accounted for.
    private TextBox NewLinkRow(Control parent, string caption, int y, bool showTestButton, out Button copyButton, out Button? testButton)
    {
        parent.Controls.Add(new Label { Location = new Point(12, y), AutoSize = true, ForeColor = Color.Gray, Text = caption });

        int textBoxWidth = showTestButton ? 210 : 290;

        var textBox = new TextBox
        {
            Location = new Point(12, y + 16),
            Size = new Size(textBoxWidth, 23),
            ReadOnly = true,
            Text = "not started yet"
        };
        parent.Controls.Add(textBox);

        int copyX = 12 + textBoxWidth + 6;
        copyButton = new Button
        {
            Location = new Point(copyX, y + 15),
            Size = new Size(showTestButton ? 50 : 64, 25),
            Text = "Copy"
        };
        parent.Controls.Add(copyButton);

        if (showTestButton)
        {
            testButton = new Button { Location = new Point(copyX + 54, y + 15), Size = new Size(50, 25), Text = "Test" };
            parent.Controls.Add(testButton);
        }
        else
        {
            testButton = null;
        }

        return textBox;
    }

    private void CopyToClipboard(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == "not started yet") return;
        Clipboard.SetText(text);
    }

    private async void FireTestCountdown(string overlay)
    {
        if (!_server.IsRunning)
        {
            MessageBox.Show(this, "Start the service first, then Test will actually have something to show in OBS.",
                "Ad Break Timer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string url = $"http://localhost:{_server.Port}/{overlay}/api?cmd=go&t=00:30:00";
        try
        {
            await TestHttpClient.GetAsync(url);
        }
        catch (Exception ex)
        {
            Logger.Log("[ERROR]", $"Test countdown failed: {ex.Message}");
            MessageBox.Show(this, $"Couldn't start the test countdown: {ex.Message}", "Ad Break Timer",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ------------------------------------------------------------
    // Server start/stop
    // ------------------------------------------------------------
    private void ToggleServer()
    {
        if (_server.IsRunning) StopServer();
        else StartServer();
    }

    private void StartServer()
    {
        try
        {
            _server.Start(_settings.Port);
            _settings.Port = _server.Port;
            JsonStore.Save(_settings, Paths.SettingsFile);
            RefreshLinkText();
        }
        catch (Exception ex)
        {
            Logger.Log("[ERROR]", $"Failed to start server: {ex}");
            MessageBox.Show(this, $"Couldn't start the web service: {ex.Message}", "Ad Break Timer",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopServer() => _server.Stop();

    private void OnServerStatusChanged(bool isRunning)
    {
        if (InvokeRequired) { Invoke(() => OnServerStatusChanged(isRunning)); return; }
        if (isRunning) _hasErrorSinceStart = false;
        RefreshStatusDisplay();
    }

    private void OnErrorLogged()
    {
        if (InvokeRequired) { Invoke(OnErrorLogged); return; }
        _hasErrorSinceStart = true;
        RefreshStatusDisplay();
    }

    private void RefreshStatusDisplay()
    {
        if (!_server.IsRunning)
        {
            _ledPanel.BackColor = ColorStopped;
            _lblStatus.Text = "Stopped";
            _lblPort.Text = "Not listening";
            _btnToggle.Text = "Start service";
            _trayIcon.Text = "Ad Break Timer (stopped)";
        }
        else if (_hasErrorSinceStart)
        {
            _ledPanel.BackColor = ColorRunningWithErrors;
            _lblStatus.Text = "Running (errors logged)";
            _lblPort.Text = $"Listening on localhost:{_server.Port}";
            _btnToggle.Text = "Stop service";
            _trayIcon.Text = $"Ad Break Timer (running with errors, port {_server.Port})";
        }
        else
        {
            _ledPanel.BackColor = ColorRunningOk;
            _lblStatus.Text = "Running";
            _lblPort.Text = $"Listening on localhost:{_server.Port}";
            _btnToggle.Text = "Stop service";
            _trayIcon.Text = $"Ad Break Timer (running, port {_server.Port})";
        }

        _ledPanel.Invalidate();
        RefreshLinkText();
    }

    private void RefreshLinkText()
    {
        if (_server.IsRunning)
        {
            string baseUrl = $"http://localhost:{_server.Port}";
            _txtBarUrl.Text = $"{baseUrl}/bar/";
            _txtRadialUrl.Text = $"{baseUrl}/radial/";
            _txtApiExample.Text = $"{baseUrl}/bar/api?cmd=go&t={TimeParsing.SecsToHms(_settings.AdBreakSeconds)}";
        }
        else
        {
            _txtBarUrl.Text = "not started yet";
            _txtRadialUrl.Text = "not started yet";
            _txtApiExample.Text = "not started yet";
        }
    }

    // ------------------------------------------------------------
    // Settings
    // ------------------------------------------------------------
    private void SaveAdTimingSettings()
    {
        if (!TimeParsing.TryParseDuration(_txtAdBreak.Text, out int adBreakSeconds, out string? error1))
        {
            MessageBox.Show(this, error1, "Ad break length", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!TimeParsing.TryParseDuration(_txtAdFree.Text, out int adFreeSeconds, out string? error2))
        {
            MessageBox.Show(this, error2, "Ad free interval", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.AdBreakSeconds = adBreakSeconds;
        _settings.AdFreeSeconds = adFreeSeconds;
        _settings.AdBufferSeconds = (int)_numAdBuffer.Value;
        JsonStore.Save(_settings, Paths.SettingsFile);

        RefreshLinkText();

        _btnSaveSettings.Text = "Saved";
        var revertTimer = new System.Windows.Forms.Timer { Interval = 1200 };
        revertTimer.Tick += (_, _) =>
        {
            _btnSaveSettings.Text = "Save settings";
            revertTimer.Stop();
            revertTimer.Dispose();
        };
        revertTimer.Start();
    }

    private void OpenOverlaySettings()
    {
        using var dlg = new OverlaySettingsForm();
        dlg.ShowDialog(this);
    }

    // ------------------------------------------------------------
    // System tray
    // ------------------------------------------------------------
    private void BuildTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show window", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Start service", null, (_, _) => StartServer());
        menu.Items.Add("Stop service", null, (_, _) => StopServer());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitFromTray());

        _trayIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        _reallyExiting = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyExiting && _chkMinimizeTray.Checked)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        Logger.ErrorLogged -= OnErrorLogged;
        _server.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnFormClosing(e);
    }
}