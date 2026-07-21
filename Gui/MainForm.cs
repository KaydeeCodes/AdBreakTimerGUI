using System.Diagnostics;
using System.Net.Http;
using AdBreakTimerGUI.Config;
using AdBreakTimerGUI.Engine;
using AdBreakTimerGUI.Gui;
using AdBreakTimerGUI.Server;
using AdBreakTimerGUI.Twitch;

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

    private Label _lblTwitchStatus = null!;
    private Button _btnTwitchConnect = null!;
    private Button _btnTwitchDisconnect = null!;
    private CheckBox _chkAutoDetectAds = null!;
    private TwitchTokenData? _twitchToken;
    private TwitchEventSubClient? _eventSubClient;
    private TwitchAdSchedulePoller? _schedulePoller;
    private TwitchAdSequencer? _adSequencer;
    private ComboBox _cmbAutoDetectTarget = null!;
    private TwitchTokenValidator? _tokenValidator;
    private PictureBox _picTwitchAvatar = null!;
    private static readonly HttpClient AvatarHttpClient = new();

    private Label _lblLastAd = null!;
    private Label _lblNextAd = null!;
    private System.Windows.Forms.Timer _adCycleDisplayTimer = null!;

    private NotifyIcon _trayIcon = null!;
    private bool _reallyExiting;

    public MainForm()
    {
        InitializeComponent();

        Paths.EnsureConfigDirExists();
        Logger.StartFresh();
        Logger.Log("[APP]", $"Ad Break Timer v{Application.ProductVersion}, OS {GetFriendlyOsName()}, .NET {Environment.Version}");

        _settings = JsonStore.Load<AppSettings>(Paths.SettingsFile) ?? new AppSettings();

        BuildUi();
        BuildTrayIcon();

        _server.StatusChanged += OnServerStatusChanged;
        Logger.ErrorLogged += OnErrorLogged;

        RefreshStatusDisplay();

        if (_settings.StartAutomatically)
            StartServer();

        _ = LoadTwitchStateAsync();

        _adCycleDisplayTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _adCycleDisplayTimer.Tick += (_, _) => RefreshAdCycleDisplay();
        _adCycleDisplayTimer.Start();
    }

    private static string GetFriendlyOsName()
    {
        Version v = Environment.OSVersion.Version;
        string name = v.Build switch
        {
            >= 22000 => "Windows 11",
            >= 10240 => "Windows 10",
            _ => $"Windows NT {v.Major}.{v.Minor}"
        };
        return $"{name} (build {v.Build})";
    }

    // ------------------------------------------------------------
    // Building the window
    // ------------------------------------------------------------
    private void BuildUi()
    {
        Text = $"Ad Break Timer v{Application.ProductVersion}";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        int y = 16;

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
        Controls.Add(NewSeparator(y));
        y += 16;

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
        Controls.Add(NewSeparator(y));
        y += 16;

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
            Text = "Test starts a plain 1 hour countdown using your saved ad-free colour, so it also works as a quick preview after changing colours in Overlay settings."
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

        tab.Controls.Add(new Label { Location = new Point(12, y + 3), AutoSize = true, Text = "Time between ads (Twitch's number)" });
        _txtAdFree = new TextBox
        {
            Location = new Point(280, y),
            Size = new Size(100, 23),
            TextAlign = HorizontalAlignment.Center,
            Text = TimeParsing.SecsToHms(_settings.AdFreeSeconds)
        };
        tab.Controls.Add(_txtAdFree);
        y += 24;

        var adFreeHint = new Label
        {
            Location = new Point(12, y),
            Size = new Size(360, 28),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 7.5F),
            Text = "Same number as Twitch's Ads Manager (\"every X minutes\"), full cycle including the ad itself, not just the gap. Only used as a fallback when Twitch's live schedule isn't available."
        };
        tab.Controls.Add(adFreeHint);
        y += 34;

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
        _picTwitchAvatar = new PictureBox
        {
            Location = new Point(12, 8),
            Size = new Size(42, 42),
            SizeMode = PictureBoxSizeMode.StretchImage,
            Visible = false
        };
        tab.Controls.Add(_picTwitchAvatar);

        _lblTwitchStatus = new Label { Location = new Point(62, 20), AutoSize = true, Text = "Not connected", ForeColor = Color.Gray };
        tab.Controls.Add(_lblTwitchStatus);

        _btnTwitchConnect = new Button { Location = new Point(280, 16), Size = new Size(100, 26), Text = "Connect" };
        _btnTwitchConnect.Click += (_, _) => OnTwitchConnectClicked();
        tab.Controls.Add(_btnTwitchConnect);

        _btnTwitchDisconnect = new Button { Location = new Point(280, 16), Size = new Size(100, 26), Text = "Disconnect", Visible = false };
        _btnTwitchDisconnect.Click += (_, _) => OnTwitchDisconnectClicked();
        tab.Controls.Add(_btnTwitchDisconnect);

        _chkAutoDetectAds = new CheckBox
        {
            Location = new Point(12, 58),
            AutoSize = true,
            Text = "Auto detect ads and run the overlay automatically",
            Checked = _settings.AutoDetectAds,
            Enabled = false
        };
        _chkAutoDetectAds.CheckedChanged += (_, _) =>
        {
            _settings.AutoDetectAds = _chkAutoDetectAds.Checked;
            JsonStore.Save(_settings, Paths.SettingsFile);
            UpdateAutoDetectRunningState();
        };
        tab.Controls.Add(_chkAutoDetectAds);

        _cmbAutoDetectTarget = new ComboBox
        {
            Location = new Point(12, 83),
            Size = new Size(150, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Enabled = false
        };
        _cmbAutoDetectTarget.Items.Add("Bar only");
        _cmbAutoDetectTarget.Items.Add("Radial only");
        _cmbAutoDetectTarget.Items.Add("Both");
        _cmbAutoDetectTarget.SelectedIndex = _settings.AutoDetectTarget switch { "Bar" => 0, "Radial" => 1, _ => 2 };
        _cmbAutoDetectTarget.SelectedIndexChanged += (_, _) =>
        {
            _settings.AutoDetectTarget = _cmbAutoDetectTarget.SelectedIndex switch { 0 => "Bar", 1 => "Radial", _ => "Both" };
            JsonStore.Save(_settings, Paths.SettingsFile);
        };
        tab.Controls.Add(_cmbAutoDetectTarget);

        var hint = new Label
        {
            Location = new Point(12, 113),
            Size = new Size(360, 34),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 7.5F),
            Text = "Turns on by itself the moment an account connects. Switch it off any time to trigger the overlay manually instead, e.g. via the API."
        };
        tab.Controls.Add(hint);

        tab.Controls.Add(new Label { Location = new Point(12, 153), AutoSize = true, ForeColor = Color.Gray, Text = "Last ad break:" });
        _lblLastAd = new Label { Location = new Point(120, 153), AutoSize = true, Text = "No ad detected yet" };
        tab.Controls.Add(_lblLastAd);

        tab.Controls.Add(new Label { Location = new Point(12, 173), AutoSize = true, ForeColor = Color.Gray, Text = "Next ad (est.):" });
        _lblNextAd = new Label { Location = new Point(120, 173), AutoSize = true, Text = "-" };
        tab.Controls.Add(_lblNextAd);
    }

    private async void LoadTwitchAvatarAsync(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            _picTwitchAvatar.Visible = false;
            return;
        }

        try
        {
            byte[] bytes = await AvatarHttpClient.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            Image image = Image.FromStream(ms);

            _picTwitchAvatar.Image?.Dispose();
            _picTwitchAvatar.Image = image;
            _picTwitchAvatar.Visible = true;
        }
        catch (Exception ex)
        {
            Logger.Log("[ERROR]", $"Failed to load Twitch avatar: {ex.Message}");
            _picTwitchAvatar.Visible = false;
        }
    }

    private TwitchTokenValidator EnsureTokenValidator()
    {
        if (_tokenValidator == null)
        {
            _tokenValidator = new TwitchTokenValidator(GetValidTwitchTokenAsync);
            _tokenValidator.TokenInvalid += OnTwitchTokenInvalid;
        }
        return _tokenValidator;
    }

    private void OnTwitchTokenInvalid()
    {
        if (InvokeRequired) { Invoke(OnTwitchTokenInvalid); return; }

        TwitchTokenStore.Delete();
        _twitchToken = null;
        _settings.AutoDetectAds = false;
        JsonStore.Save(_settings, Paths.SettingsFile);
        UpdateTwitchTabUi(null);
        UpdateAutoDetectRunningState();
        _tokenValidator?.Stop();

        MessageBox.Show(this,
            "Your Twitch connection is no longer valid, most likely because it was disconnected from Twitch's own site. Reconnect on the Twitch tab to keep using auto detect.",
            "Ad Break Timer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // ------------------------------------------------------------
    // Twitch connect / disconnect
    // ------------------------------------------------------------
    private async Task LoadTwitchStateAsync()
    {
        TwitchTokenData? token = TwitchTokenStore.Load();
        if (token is null)
        {
            UpdateTwitchTabUi(null);
            return;
        }

        if (token.IsExpiredOrExpiringSoon)
        {
            TwitchTokenData? refreshed = await TwitchAuthService.RefreshAsync(token, CancellationToken.None);
            if (refreshed is null)
            {
                TwitchTokenStore.Delete();
                UpdateTwitchTabUi(null);
                return;
            }
            token = refreshed;
        }

        _twitchToken = token;
        UpdateTwitchTabUi(token);
        UpdateAutoDetectRunningState();
        EnsureTokenValidator().Start();
    }

    private void OnTwitchConnectClicked()
    {
        using var dlg = new TwitchConnectForm();
        DialogResult result = dlg.ShowDialog(this);

        if (result == DialogResult.OK && dlg.Result != null)
        {
            _twitchToken = dlg.Result;
            UpdateTwitchTabUi(_twitchToken);

            _settings.AutoDetectAds = true;
            _chkAutoDetectAds.Checked = true;
            JsonStore.Save(_settings, Paths.SettingsFile);

            Logger.Log("[TWITCH]", $"Connected as {_twitchToken.DisplayName}");
            UpdateAutoDetectRunningState();
            EnsureTokenValidator().Start();
        }
    }

    private void OnTwitchDisconnectClicked()
    {
        DialogResult confirm = MessageBox.Show(this,
            "Disconnect this Twitch account? Auto detect ads will stop working until reconnected.",
            "Ad Break Timer", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        TwitchTokenStore.Delete();
        _twitchToken = null;
        _settings.AutoDetectAds = false;
        JsonStore.Save(_settings, Paths.SettingsFile);
        UpdateTwitchTabUi(null);

        Logger.Log("[TWITCH]", "Disconnected.");
        UpdateAutoDetectRunningState();
        // Was wrongly calling EnsureTokenValidator().Start() here before, a copy-paste leftover from the connect path. There's no token to validate any more at this point, this genuinely mirrors OnTwitchTokenInvalid's handling of the same situation now, stop it, don't start a pointless loop that just checks a null token every hour until reconnected.
        _tokenValidator?.Stop();
    }

    private async Task<TwitchTokenData?> GetValidTwitchTokenAsync()
    {
        if (_twitchToken is null) return null;
        if (!_twitchToken.IsExpiredOrExpiringSoon) return _twitchToken;

        TwitchTokenData? refreshed = await TwitchAuthService.RefreshAsync(_twitchToken, CancellationToken.None);
        if (refreshed != null) _twitchToken = refreshed;
        return refreshed;
    }

    private void UpdateAutoDetectRunningState()
    {
        bool shouldRun = _server.IsRunning && _twitchToken != null && _settings.AutoDetectAds;

        Logger.Log("[TWITCH]", $"Auto detect check: shouldRun={shouldRun} (serverRunning={_server.IsRunning}, connected={_twitchToken != null}, autoDetectAds={_settings.AutoDetectAds})");

        if (shouldRun)
        {
            _adSequencer ??= CreateAdSequencer();

            if (_eventSubClient == null)
            {
                _eventSubClient = new TwitchEventSubClient(GetValidTwitchTokenAsync);
                _adSequencer.Attach(_eventSubClient);
            }
            _eventSubClient.Start();

            if (_schedulePoller == null)
            {
                _schedulePoller = new TwitchAdSchedulePoller(GetValidTwitchTokenAsync);
                _adSequencer.AttachSchedulePoller(_schedulePoller);
            }
            _schedulePoller.Start();
        }
        else
        {
            _eventSubClient?.Stop();
            _schedulePoller?.Stop();
            _adSequencer?.Stop();
        }
    }

    private TwitchAdSequencer CreateAdSequencer() => new(
        getSettings: () => _settings,
        getTarget: () => _settings.AutoDetectTarget switch
        {
            "Bar" => AdSequencerTarget.Bar,
            "Radial" => AdSequencerTarget.Radial,
            _ => AdSequencerTarget.Both
        });

    private void RefreshAdCycleDisplay()
    {
        if (_adSequencer is null)
        {
            _lblLastAd.Text = "No ad detected yet";
            _lblNextAd.Text = "-";
            return;
        }

        _lblLastAd.Text = _adSequencer.LastAdStartedUtc is { } last
            ? $"{FormatSpan(DateTime.UtcNow - last)} ago"
            : "No ad detected yet";

        if (_adSequencer.NextAdEstimatedUtc is { } next)
        {
            TimeSpan remaining = next - DateTime.UtcNow;
            _lblNextAd.Text = remaining > TimeSpan.Zero
                ? $"in {FormatSpan(remaining)}"
                : "any moment now";
        }
        else
        {
            _lblNextAd.Text = "-";
        }
    }

    private static string FormatSpan(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m {span.Seconds}s"
            : $"{span.Minutes}m {span.Seconds}s";
    }

    private void UpdateTwitchTabUi(TwitchTokenData? token)
    {
        if (token is null)
        {
            _lblTwitchStatus.Text = "Not connected";
            _lblTwitchStatus.ForeColor = Color.Gray;
            _lblTwitchStatus.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            _btnTwitchConnect.Visible = true;
            _btnTwitchDisconnect.Visible = false;
            _chkAutoDetectAds.Enabled = true;
            _cmbAutoDetectTarget.Enabled = true;
            _chkAutoDetectAds.Checked = false;
            _picTwitchAvatar.Visible = false;
        }
        else
        {
            _lblTwitchStatus.Text = $"Connected as {token.DisplayName}";
            _lblTwitchStatus.ForeColor = SystemColors.ControlText;
            _lblTwitchStatus.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _btnTwitchConnect.Visible = false;
            _btnTwitchDisconnect.Visible = true;
            _chkAutoDetectAds.Enabled = true;
            _cmbAutoDetectTarget.Enabled = true;
            LoadTwitchAvatarAsync(token.ProfileImageUrl);
        }
    }

    // ------------------------------------------------------------
    // Shared small helpers
    // ------------------------------------------------------------
    private static Control NewSeparator(int y) => new Panel
    {
        Location = new Point(16, y),
        Size = new Size(404, 1),
        BackColor = Color.Gainsboro
    };

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

        string color = overlay == "bar"
            ? OverlayCommandExecutor.GetBarColors().adFreeColor
            : OverlayCommandExecutor.GetRadialColors().adFreeColor;

        string url = $"http://localhost:{_server.Port}/{overlay}/api?cmd=go&t=01:00:00&color={Uri.EscapeDataString(color)}";
        Logger.Log("[TEST]", $"Firing test countdown for {overlay}, read colour \"{color}\", full URL: {url}");
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
        UpdateAutoDetectRunningState();
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
            MessageBox.Show(this, error2, "Time between ads", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int newBufferSeconds = (int)_numAdBuffer.Value;

        var changes = new List<string>();
        if (_settings.AdBreakSeconds != adBreakSeconds) changes.Add($"adBreakSeconds {_settings.AdBreakSeconds}->{adBreakSeconds}");
        if (_settings.AdFreeSeconds != adFreeSeconds) changes.Add($"adFreeSeconds {_settings.AdFreeSeconds}->{adFreeSeconds}");
        if (_settings.AdBufferSeconds != newBufferSeconds) changes.Add($"adBufferSeconds {_settings.AdBufferSeconds}->{newBufferSeconds}");

        _settings.AdBreakSeconds = adBreakSeconds;
        _settings.AdFreeSeconds = adFreeSeconds;
        _settings.AdBufferSeconds = newBufferSeconds;
        JsonStore.Save(_settings, Paths.SettingsFile);

        Logger.Log("[SETTINGS]", changes.Count > 0 ? $"Timing updated: {string.Join(", ", changes)}" : "Timing settings saved, no actual changes.");

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
        _adCycleDisplayTimer.Stop();
        _adCycleDisplayTimer.Dispose();

        _eventSubClient?.Stop();
        _schedulePoller?.Stop();
        _adSequencer?.Stop();
        _tokenValidator?.Stop();

        _server.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnFormClosing(e);
    }
}