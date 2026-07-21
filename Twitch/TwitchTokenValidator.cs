using AdBreakTimerGUI.Config;

namespace AdBreakTimerGUI.Twitch;

// Runs Twitch's required /validate check on startup and hourly after, this is what actually catches "the user clicked Disconnect on Twitch's own site", not just local token expiry.
public class TwitchTokenValidator
{
    private static readonly TimeSpan ValidateInterval = TimeSpan.FromHours(1);
    private readonly Func<Task<TwitchTokenData?>> _getToken;

    public event Action? TokenInvalid;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public TwitchTokenValidator(Func<Task<TwitchTokenData?>> getToken)
    {
        _getToken = getToken;
    }

    public void Start()
    {
        if (_loopTask is { IsCompleted: false }) return;
        Logger.Log("[TWITCH]", "Token validator starting.");
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Stop() => _cts?.Cancel();

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TwitchTokenData? current = await _getToken();
            if (current != null)
            {
                bool valid = await TwitchAuthService.ValidateAsync(current, token);
                if (!valid)
                {
                    Logger.Log("[TWITCH]", "Token validate failed, the account was most likely disconnected from Twitch's side.");
                    TokenInvalid?.Invoke();
                    break; // nothing left to validate until reconnected
                }
            }

            try { await Task.Delay(ValidateInterval, token); }
            catch (OperationCanceledException) { break; }
        }
    }
}