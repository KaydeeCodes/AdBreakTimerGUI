using System.Diagnostics;
using AdBreakTimerGUI.Config;
using AdBreakTimerGUI.Twitch;

namespace AdBreakTimerGUI.Gui;

// Runs the device code flow and shows its progress, approving happens in a browser, not here.
public class TwitchConnectForm : Form
{
    public TwitchTokenData? Result { get; private set; }

    private readonly CancellationTokenSource _cts = new();

    private Label _lblStatus = null!;
    private Label _lblCode = null!;
    private Button _btnOpenBrowser = null!;
    private Button _btnCopyCode = null!;
    private Button _btnCancel = null!;

    private string _verificationUri = "";

    public TwitchConnectForm()
    {
        BuildUi();
        // Kicked off after Shown, not before, so there's something on screen the instant this opens.
        Shown += (_, _) => _ = RunConnectFlowAsync();
    }

    private void BuildUi()
    {
        Text = "Connect to Twitch";
        ClientSize = new Size(360, 240);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9F);

        _lblStatus = new Label { Location = new Point(16, 16), Size = new Size(328, 40), Text = "Requesting a code from Twitch..." };
        Controls.Add(_lblStatus);

        _lblCode = new Label { Location = new Point(16, 60), Size = new Size(328, 44), Font = new Font("Consolas", 20F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, Visible = false };
        Controls.Add(_lblCode);

        _btnOpenBrowser = new Button { Location = new Point(16, 112), Size = new Size(328, 30), Text = "Open twitch.tv/activate", Visible = false };
        _btnOpenBrowser.Click += (_, _) => OpenBrowser();
        Controls.Add(_btnOpenBrowser);

        _btnCopyCode = new Button { Location = new Point(16, 150), Size = new Size(328, 26), Text = "Copy code", Visible = false };
        _btnCopyCode.Click += (_, _) => Clipboard.SetText(_lblCode.Text);
        Controls.Add(_btnCopyCode);

        _btnCancel = new Button { Location = new Point(16, 186), Size = new Size(328, 30), Text = "Cancel" };
        _btnCancel.Click += (_, _) =>
        {
            _cts.Cancel();
            DialogResult = DialogResult.Cancel;
            Close();
        };
        Controls.Add(_btnCancel);

        AcceptButton = null;
        CancelButton = _btnCancel;
    }

    // No InvokeRequired check needed here, RunConnectFlowAsync started on the UI thread, so this callback resumes back on it automatically.
    private void OnCodeReady(DeviceCodeInfo info)
    {
        _verificationUri = info.VerificationUri;
        _lblStatus.Text = "Go to twitch.tv/activate and enter this code, a browser window should open on its own:";
        _lblCode.Text = info.UserCode;
        _lblCode.Visible = true;
        _btnOpenBrowser.Visible = true;
        _btnCopyCode.Visible = true;
        OpenBrowser();
    }

    private void OpenBrowser()
    {
        if (string.IsNullOrEmpty(_verificationUri)) return;
        try
        {
            Process.Start(new ProcessStartInfo(_verificationUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Log("[ERROR]", $"Couldn't open browser for Twitch auth: {ex.Message}");
        }
    }

    private async Task RunConnectFlowAsync()
    {
        try
        {
            TwitchTokenData? token = await TwitchAuthService.ConnectAsync(OnCodeReady, _cts.Token);

            if (_cts.IsCancellationRequested) return;

            if (token != null)
            {
                Result = token;
                _lblStatus.Text = $"Connected as {token.DisplayName}!";
                _lblCode.Visible = false;
                _btnOpenBrowser.Visible = false;
                _btnCopyCode.Visible = false;
                _btnCancel.Text = "Close";
                await Task.Delay(900, _cts.Token);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                _lblStatus.Text = "Couldn't connect. The code may have expired, or the request was denied. Check the log file for detail.";
                _lblCode.Visible = false;
                _btnOpenBrowser.Visible = false;
                _btnCopyCode.Visible = false;
                _btnCancel.Text = "Close";
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled deliberately, Cancel button or window closing, nothing left to show.
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_cts.IsCancellationRequested) _cts.Cancel();
        base.OnFormClosing(e);
    }
}