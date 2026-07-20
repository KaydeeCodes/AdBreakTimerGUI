using System.Diagnostics;
using AdBreakTimerGUI.Config;
using AdBreakTimerGUI.Engine;
using AdBreakTimerGUI.Gui;
using AdBreakTimerGUI.Server;

namespace AdBreakTimerGUI;

public partial class MainForm : Form
{
    // The web server the whole app revolves around. I create it once
    // here and keep it for the form's whole lifetime, starting and
    // stopping it as I like rather than recreating it every time.
    private readonly WebServerHost _server = new();

    // Loaded once on startup, saved back whenever something changes it,
    // the port after a successful start, the ad timing when I click
    // Save settings, and so on.
    private AppSettings _settings = new();

    // The three colours the LED can actually be. Pulled out as fields
    // rather than typed inline everywhere, so if I ever want to tweak
    // the exact shade there's one place to do it.
    private static readonly Color ColorRunningOk = Color.ForestGreen;
    private static readonly Color ColorRunningWithErrors = Color.FromArgb(0xF5, 0xA6, 0x23); // amber, matches the mockup's warning colour
    private static readonly Color ColorStopped = Color.Firebrick;

    // True once at least one [ERROR] has been logged since the server
    // last started. Reset back to false every time StartServer()
    // succeeds, so a fresh start always begins green even if the
    // previous run ended with errors logged.
    private bool _hasErrorSinceStart;

    // Controls I need to reach from more than one method are kept as
    // fields. Anything only used inside BuildUi stays a local variable.
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
    private Button _btnSaveSettings = null!;

    private NotifyIcon _trayIcon = null!;

    // Set true only by the tray menu's Exit item. This is how I tell
    // "the X button was clicked, minimise to tray" apart from "I
    // actually want this to quit" in OnFormClosing further down.
    private bool _reallyExiting;

    public MainForm()
    {
        InitializeComponent();

        // Everything on disk lives under %AppData%\AdBreakTimer, see
        // Config.Paths for why. I make sure that folder exists and the
        // log file is started fresh before anything else touches either.
        Paths.EnsureConfigDirExists();
        Logger.StartFresh();

        _settings = JsonStore.Load<AppSettings>(Paths.SettingsFile) ?? new AppSettings();

        BuildUi();
        BuildTrayIcon();

        // I want the traffic light and button text to update the moment
        // the server's state actually changes, from whichever thread
        // that happens on, rather than manually flipping the UI in
        // every place that calls Start() or Stop().
        _server.StatusChanged += OnServerStatusChanged;

        // And the same for errors, anything that calls Logger.Log with
        // an [ERROR] tag flips the light to amber while the server's
        // still running. See Logger.cs for why this lives there rather
        // than being tracked separately in several places.
        Logger.ErrorLogged += OnErrorLogged;

        // The LED and status text were previously only ever updated by
        // OnServerStatusChanged, which only fires once Start() or
        // Stop() actually runs. With auto start off, neither runs at
        // launch, so the LED was sitting at whatever colour BuildUi
        // happened to create it with, regardless of what the label next
        // to it said. Calling this once here makes sure what's on
        // screen always matches the server's real state from the very
        // first frame, not just after the first state change.
        RefreshStatusDisplay();

        if (_settings.StartAutomatically)
            StartServer();
    }

    // ------------------------------------------------------------
    // Building the window
    // ------------------------------------------------------------
    // I'm laying this out in code rather than the visual designer. The
    // window's fixed and doesn't need to resize dynamically, so a
    // simple running Y cursor is easier for me to read back later than
    // hunting through a hidden .designer.cs file to work out what's
    // actually where.
    private void BuildUi()
    {
        Text = "Ad Break Timer";
        ClientSize = new Size(440, 700);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        // I'm extracting this from the exe's own icon resource rather
        // than loading Assets/app.ico separately. ApplicationIcon in
        // the csproj is what actually embeds it into the exe in the
        // first place, this just reads it back out, so there's only
        // ever one file to swap if I change the icon later.
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        int y = 16;

        // ---- Status row: LED, status text, port, start/stop button ----
        // I'm not setting BackColor here any more, it used to be
        // hardcoded to green, which is exactly what caused the LED to
        // show the wrong colour on a launch with auto start off. The
        // real colour gets set once by RefreshStatusDisplay() right
        // after BuildUi runs, and from then on by whatever actually
        // changes (OnServerStatusChanged / OnErrorLogged).
        _ledPanel = new Panel
        {
            Location = new Point(16, y),
            Size = new Size(16, 16)
        };
        // A plain Panel is a square by default. I paint it as a circle
        // instead so it reads as a status dot, same idea as the LED in
        // the HTML mockup.
        _ledPanel.Paint += (_, e) =>
        {
            using var brush = new SolidBrush(_ledPanel.BackColor);
            e.Graphics.FillEllipse(brush, 0, 0, _ledPanel.Width - 1, _ledPanel.Height - 1);
        };

        _lblStatus = new Label
        {
            Location = new Point(40, y - 2),
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold)
        };

        _lblPort = new Label
        {
            Location = new Point(40, y + 18),
            AutoSize = true,
            ForeColor = Color.Gray
        };

        _btnToggle = new Button
        {
            Location = new Point(300, y - 4),
            Size = new Size(120, 30)
        };
        _btnToggle.Click += (_, _) => ToggleServer();

        Controls.Add(_ledPanel);
        Controls.Add(_lblStatus);
        Controls.Add(_lblPort);
        Controls.Add(_btnToggle);

        y += 50;
        Controls.Add(NewSeparator(y));
        y += 16;

        // ---- Checkboxes ----
        _chkAutoStart = new CheckBox
        {
            Location = new Point(16, y),
            AutoSize = true,
            Text = "Start automatically on launch",
            Checked = _settings.StartAutomatically
        };
        _chkAutoStart.CheckedChanged += (_, _) =>
        {
            _settings.StartAutomatically = _chkAutoStart.Checked;
            JsonStore.Save(_settings, Paths.SettingsFile);
        };
        Controls.Add(_chkAutoStart);
        y += 26;

        _chkMinimizeTray = new CheckBox
        {
            Location = new Point(16, y),
            AutoSize = true,
            Text = "Minimise to tray when closed",
            Checked = _settings.MinimizeToTrayOnClose
        };
        _chkMinimizeTray.CheckedChanged += (_, _) =>
        {
            _settings.MinimizeToTrayOnClose = _chkMinimizeTray.Checked;
            JsonStore.Save(_settings, Paths.SettingsFile);
        };
        Controls.Add(_chkMinimizeTray);
        y += 34;

        Controls.Add(NewSeparator(y));
        y += 16;

        // ---- Overlay & API links ----
        Controls.Add(NewSectionLabel("OVERLAY && API LINKS", y));
        y += 22;

        _txtBarUrl = NewLinkRow("Bar overlay", y, out Button copyBar);
        y += 46;
        _txtRadialUrl = NewLinkRow("Radial overlay", y, out Button copyRadial);
        y += 46;
        _txtApiExample = NewLinkRow("Example go command", y, out Button copyApi);
        y += 46;

        copyBar.Click += (_, _) => CopyToClipboard(_txtBarUrl.Text);
        copyRadial.Click += (_, _) => CopyToClipboard(_txtRadialUrl.Text);
        copyApi.Click += (_, _) => CopyToClipboard(_txtApiExample.Text);

        Controls.Add(NewSeparator(y));
        y += 16;

        // ---- Ad timing ----
        Controls.Add(NewSectionLabel("AD TIMING", y));
        y += 22;

        Controls.Add(new Label { Location = new Point(16, y + 3), AutoSize = true, Text = "Ad break length" });
        _txtAdBreak = new TextBox
        {
            Location = new Point(320, y),
            Size = new Size(100, 23),
            TextAlign = HorizontalAlignment.Center,
            Text = TimeParsing.SecsToHms(_settings.AdBreakSeconds)
        };
        Controls.Add(_txtAdBreak);
        y += 32;

        Controls.Add(new Label { Location = new Point(16, y + 3), AutoSize = true, Text = "Ad free interval" });
        _txtAdFree = new TextBox
        {
            Location = new Point(320, y),
            Size = new Size(100, 23),
            TextAlign = HorizontalAlignment.Center,
            Text = TimeParsing.SecsToHms(_settings.AdFreeSeconds)
        };
        Controls.Add(_txtAdFree);
        y += 36;

        _btnSaveSettings = new Button
        {
            Location = new Point(16, y),
            Size = new Size(404, 30),
            Text = "Save settings"
        };
        _btnSaveSettings.Click += (_, _) => SaveAdTimingSettings();
        Controls.Add(_btnSaveSettings);
        y += 46;

        Controls.Add(NewSeparator(y));
        y += 16;

        // ---- Overlay appearance ----
        Controls.Add(NewSectionLabel("OVERLAY APPEARANCE", y));
        y += 22;

        Button btnOverlaySettings = new()
        {
            Location = new Point(16, y),
            Size = new Size(404, 30),
            Text = "Bar / Radial settings..."
        };
        btnOverlaySettings.Click += (_, _) => OpenOverlaySettings();
        Controls.Add(btnOverlaySettings);
        y += 46;

        Controls.Add(NewSeparator(y));
        y += 16;

        // ---- Twitch account (locked for now, this is the phase two bit) ----
        Controls.Add(NewSectionLabel("TWITCH ACCOUNT", y));
        y += 22;

        Controls.Add(new Label { Location = new Point(16, y + 3), AutoSize = true, Text = "Not connected", ForeColor = Color.Gray });
        Button btnTwitchConnect = new()
        {
            Location = new Point(320, y),
            Size = new Size(100, 26),
            Text = "Connect",
            Enabled = false
        };
        var tip = new ToolTip();
        tip.SetToolTip(btnTwitchConnect, "Coming in a future update");
        Controls.Add(btnTwitchConnect);
        y += 32;

        // Cosmetic only for now, greyed out until there's an actual
        // Twitch connection behind it. Once EventSub is wired up, this
        // is what flips the app from "wait for a URL command" to
        // "watch channel.ad_break.begin and fire go automatically".
        CheckBox chkAutoDetectAds = new()
        {
            Location = new Point(16, y),
            AutoSize = true,
            Text = "Auto detect ads and start the overlay automatically",
            Checked = _settings.AutoDetectAds,
            Enabled = false
        };
        var tipAuto = new ToolTip();
        tipAuto.SetToolTip(chkAutoDetectAds, "Needs a connected Twitch account, coming in a future update");
        Controls.Add(chkAutoDetectAds);
        y += 32;

        Controls.Add(NewSeparator(y));
        y += 16;

        // ---- Footer: open config folder / open log file ----
        Button btnOpenConfig = new()
        {
            Location = new Point(16, y),
            Size = new Size(195, 30),
            Text = "Open config folder"
        };
        btnOpenConfig.Click += (_, _) => Process.Start("explorer.exe", Paths.ConfigDir);

        Button btnOpenLog = new()
        {
            Location = new Point(225, y),
            Size = new Size(195, 30),
            Text = "Open log file"
        };
        btnOpenLog.Click += (_, _) =>
        {
            // If the log somehow doesn't exist yet, opening it directly
            // would just error. I create an empty one first so the
            // button always does something sensible.
            if (!File.Exists(Paths.LogFile)) File.WriteAllText(Paths.LogFile, "");
            Process.Start(new ProcessStartInfo(Paths.LogFile) { UseShellExecute = true });
        };

        Controls.Add(btnOpenConfig);
        Controls.Add(btnOpenLog);
        y += 40;

        // ---- Footer credit, same line I put at the bottom of all my tools ----
        var lblFooter = new LinkLabel
        {
            Location = new Point(16, y),
            Size = new Size(404, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Gray,
            Text = "Made by Kaydee.Codes - Free to use, no data collected, ever."
        };
        // Only the "Kaydee.Codes" part of the string should actually be
        // a clickable link. LinkArea takes a start index and a length
        // into the text above, "Made by " is 8 characters long, and
        // "Kaydee.Codes" is 12 characters long.
        lblFooter.LinkArea = new LinkArea(8, 12);
        lblFooter.LinkClicked += (_, _) =>
            Process.Start(new ProcessStartInfo("https://kaydee.codes/") { UseShellExecute = true });
        Controls.Add(lblFooter);
        y += 26;

        ClientSize = new Size(440, y + 16);

        RefreshLinkText();
    }

    // A thin horizontal line, purely to break the window into the same
    // sections as the mockup.
    private Control NewSeparator(int y) => new Panel
    {
        Location = new Point(16, y),
        Size = new Size(404, 1),
        BackColor = Color.Gainsboro
    };

    // A small grey, capitalised section heading, matching the mockup.
    private Control NewSectionLabel(string text, int y) => new Label
    {
        Location = new Point(16, y),
        AutoSize = true,
        ForeColor = Color.Gray,
        Font = new Font("Segoe UI", 8F, FontStyle.Bold),
        Text = text
    };

    // One row in the overlay & API links section: a caption, a read
    // only text box holding the URL, and a Copy button next to it. I
    // hand the text box back as the return value so I can update its
    // text later once the port's actually known, and the copy button
    // back through an out parameter so the caller can wire up whatever
    // that particular row should copy.
    private TextBox NewLinkRow(string caption, int y, out Button copyButton)
    {
        Controls.Add(new Label { Location = new Point(16, y), AutoSize = true, ForeColor = Color.Gray, Text = caption });

        var textBox = new TextBox
        {
            Location = new Point(16, y + 16),
            Size = new Size(330, 23),
            ReadOnly = true,
            Text = "not started yet"
        };
        Controls.Add(textBox);

        copyButton = new Button
        {
            Location = new Point(352, y + 15),
            Size = new Size(68, 25),
            Text = "Copy"
        };
        Controls.Add(copyButton);

        return textBox;
    }

    private void CopyToClipboard(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == "not started yet") return;
        Clipboard.SetText(text);
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
            _settings.Port = _server.Port; // remember whichever port it actually bound to
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

    // WebServerHost raises this from its own background task, not the
    // UI thread, so I have to marshal back onto the UI thread with
    // Invoke before touching any control here. This is the single
    // easiest WinForms mistake to make, leaving this comment as a
    // reminder to myself for next time I touch this method.
    private void OnServerStatusChanged(bool isRunning)
    {
        if (InvokeRequired)
        {
            Invoke(() => OnServerStatusChanged(isRunning));
            return;
        }

        // A fresh start always begins green, even if the previous run
        // ended with errors logged. Only reset this on the transition
        // into "running", not on every call, otherwise a Stop() right
        // after an error would also wipe the flag before I've had a
        // chance to see it.
        if (isRunning) _hasErrorSinceStart = false;

        RefreshStatusDisplay();
    }

    // Raised by Logger whenever an [ERROR] line gets written anywhere
    // in the app. Same threading caveat as OnServerStatusChanged, this
    // can fire from a background thread.
    private void OnErrorLogged()
    {
        if (InvokeRequired)
        {
            Invoke(OnErrorLogged);
            return;
        }

        _hasErrorSinceStart = true;
        RefreshStatusDisplay();
    }

    // The single place that decides what the LED, status label, port
    // label, toggle button, and tray icon text should actually say,
    // based on _server.IsRunning and _hasErrorSinceStart. Called from
    // three places: once right after BuildUi (so the very first frame
    // is correct even if auto start is off), and again whenever either
    // of those two things changes.
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
        JsonStore.Save(_settings, Paths.SettingsFile);

        RefreshLinkText();

        // A little "Saved" confirmation on the button itself, echoing
        // the mockup, reverting back after just over a second.
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

    // Opens the Bar/Radial appearance settings as a modal dialog. I'm
    // not keeping a field for this one since it's only ever open for
    // as long as I'm using it, unlike the tray icon or the server,
    // which live for the whole lifetime of the app.
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

    // I want the window's X button to minimise to tray rather than
    // quit outright, since the whole point of this app is to sit in
    // the background while OBS and Streamer.bot are talking to it. The
    // tray menu's Exit item is the only normal way to actually close
    // it, tracked by the _reallyExiting flag above.
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