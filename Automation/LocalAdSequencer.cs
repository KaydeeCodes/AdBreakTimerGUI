using System.Collections.Specialized;
using AdBreakTimerGUI.Config;
using AdBreakTimerGUI.Engine;
using AdBreakTimerGUI.Server;
using AdBreakTimerGUI.Twitch;

namespace AdBreakTimerGUI.Automation;

// A basic self looping ad/ad-free cycle driven purely by the Timing tab's saved numbers, no Twitch connection needed. Twitch mode always takes priority over this, MainForm.UpdateAutomationState decides when that's true, this class just runs or stops when told, it has no opinion of its own on priority.
public class LocalAdSequencer
{
    private readonly Func<AppSettings> _getSettings;
    private readonly Func<AdSequencerTarget> _getTarget;

    private CancellationTokenSource? _loopCts;
    private readonly object _lock = new();

    public LocalAdSequencer(Func<AppSettings> getSettings, Func<AdSequencerTarget> getTarget)
    {
        _getSettings = getSettings;
        _getTarget = getTarget;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_loopCts is { IsCancellationRequested: false }) return; // already running
            _loopCts = new CancellationTokenSource();
            Logger.Log("[LOCAL]", "Local loop starting.");
            _ = RunLoopAsync(_loopCts.Token);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_loopCts == null) return;
            Logger.Log("[LOCAL]", "Local loop stopping.");
            _loopCts.Cancel();
            _loopCts = null;
        }
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                // Read fresh every lap, on purpose, a Timing tab change while this is running takes effect on the next cycle rather than needing a restart.
                AppSettings settings = _getSettings();
                AdSequencerTarget target = _getTarget();

                int adSeconds = Math.Max(1, settings.AdBreakSeconds);
                int adFreeSeconds = Math.Max(1, settings.AdFreeSeconds);

                FireGo(target, adSeconds, isAdPhase: true);
                await Task.Delay(TimeSpan.FromSeconds(adSeconds), token);

                if (settings.AdBufferSeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(settings.AdBufferSeconds), token);

                FireGo(target, adFreeSeconds, isAdPhase: false);
                await Task.Delay(TimeSpan.FromSeconds(adFreeSeconds), token);

                if (settings.AdBufferSeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(settings.AdBufferSeconds), token);

                // Loops back to the top on its own, forever, until Stop() cancels the token.
            }
        }
        catch (OperationCanceledException)
        {
            // Stop() was called, expected, nothing to clean up.
        }
        catch (Exception ex)
        {
            Logger.Log("[ERROR]", $"Local loop crashed unexpectedly: {ex}");
        }
    }

    private static void FireGo(AdSequencerTarget target, int seconds, bool isAdPhase)
    {
        if (target is AdSequencerTarget.Bar or AdSequencerTarget.Both)
        {
            var (adFreeColor, adColor, _) = OverlayCommandExecutor.GetBarColors();
            var qs = new NameValueCollection { ["t"] = TimeParsing.SecsToHms(seconds), ["color"] = isAdPhase ? adColor : adFreeColor };
            LogIfError(OverlayCommandExecutor.ExecuteBar("go", qs));
        }
        if (target is AdSequencerTarget.Radial or AdSequencerTarget.Both)
        {
            var (adFreeColor, adColor, _) = OverlayCommandExecutor.GetRadialColors();
            var qs = new NameValueCollection { ["t"] = TimeParsing.SecsToHms(seconds), ["color"] = isAdPhase ? adColor : adFreeColor };
            LogIfError(OverlayCommandExecutor.ExecuteRadial("go", qs));
        }
    }

    private static void LogIfError(OverlayCommandExecutor.CommandResult result)
    {
        if (!result.Ok)
            Logger.Log("[ERROR]", $"Local loop command failed: {result.Error}");
    }
}