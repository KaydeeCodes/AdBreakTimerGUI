using System.Collections.Specialized;
using AdBreakTimerGUI.Config;
using AdBreakTimerGUI.Engine;
using AdBreakTimerGUI.Server;

namespace AdBreakTimerGUI.Twitch;

public enum AdSequencerTarget { Bar, Radial, Both }

public class TwitchAdSequencer
{
    private const string AdColor = "#ff0000";
    private const string AdFreeColor = "#00ff00";
    private const int MinimumAdFreeSeconds = 5;

    private readonly Func<AppSettings> _getSettings;
    private readonly Func<AdSequencerTarget> _getTarget;

    private CancellationTokenSource? _cycleCts;
    private readonly object _lock = new();

    // When the most recent real ad break actually started, and my
    // best estimate of when the next one will, based on the
    // configured cycle length. Both null until the very first ad
    // break comes through, there's nothing genuine to show before
    // that. NextAdEstimatedUtc specifically is only set once the
    // ad-free countdown actually begins, not the moment the ad break
    // starts, since the true "next ad" estimate depends on knowing
    // this ad's real duration first.
    public DateTime? LastAdStartedUtc { get; private set; }
    public DateTime? NextAdEstimatedUtc { get; private set; }

    public TwitchAdSequencer(Func<AppSettings> getSettings, Func<AdSequencerTarget> getTarget)
    {
        _getSettings = getSettings;
        _getTarget = getTarget;
    }

    public void Attach(TwitchEventSubClient client) => client.AdBreakBegan += (_, e) => OnAdBreakBegan(e.DurationSeconds);

    public void Stop()
    {
        lock (_lock)
        {
            if (_cycleCts != null)
                Logger.Log("[TWITCH]", "Sequencer stopping, cancelling any running cycle.");
            _cycleCts?.Cancel();
            _cycleCts = null;
        }
        // Deliberately not clearing LastAdStartedUtc/NextAdEstimatedUtc
        // here. Even once stopped, "when did the last ad actually
        // happen" is still true and still worth showing, it just isn't
        // being actively tracked toward a new one any more.
    }

    private void OnAdBreakBegan(int durationSeconds)
    {
        if (durationSeconds <= 0)
        {
            Logger.Log("[ERROR]", $"Sequencer received an ad break event with a non-positive duration ({durationSeconds}s), ignoring it.");
            return;
        }

        Logger.Log("[TWITCH]", $"Sequencer received the event, starting a red countdown for {durationSeconds}s.");

        LastAdStartedUtc = DateTime.UtcNow;
        // Cleared until the ad-free phase actually starts and works
        // out a real estimate, rather than showing a stale number left
        // over from the previous cycle while this one's still running.
        NextAdEstimatedUtc = null;

        var newCts = new CancellationTokenSource();
        lock (_lock)
        {
            _cycleCts?.Cancel();
            _cycleCts = newCts;
        }

        _ = RunCycleAsync(durationSeconds, newCts.Token);
    }

    private async Task RunCycleAsync(int adDurationSeconds, CancellationToken token)
    {
        try
        {
            AppSettings settings = _getSettings();
            AdSequencerTarget target = _getTarget();

            FireGo(target, adDurationSeconds, AdColor);
            await Task.Delay(TimeSpan.FromSeconds(adDurationSeconds), token);

            if (settings.AdBufferSeconds > 0)
            {
                Logger.Log("[TWITCH]", $"Ad countdown finished, waiting {settings.AdBufferSeconds}s before the ad-free countdown.");
                await Task.Delay(TimeSpan.FromSeconds(settings.AdBufferSeconds), token);
            }

            int rawAdFreeSeconds = settings.AdFreeSeconds - adDurationSeconds - settings.AdBufferSeconds;
            int adFreeSeconds = Math.Max(MinimumAdFreeSeconds, rawAdFreeSeconds);

            if (rawAdFreeSeconds < MinimumAdFreeSeconds)
                Logger.Log("[ERROR]", $"Configured cycle length ({settings.AdFreeSeconds}s) is too short for a {adDurationSeconds}s ad plus a {settings.AdBufferSeconds}s buffer, flooring the ad-free countdown to {MinimumAdFreeSeconds}s instead of {rawAdFreeSeconds}s.");

            // This is my best estimate of when the next ad will start,
            // worked out now, right as the ad-free countdown itself
            // begins, so it's based on the real remaining time rather
            // than a number calculated back when the ad break started.
            NextAdEstimatedUtc = DateTime.UtcNow.AddSeconds(adFreeSeconds);

            Logger.Log("[TWITCH]", $"Starting ad-free countdown for {adFreeSeconds}s (cycle {settings.AdFreeSeconds}s minus {adDurationSeconds}s ad minus {settings.AdBufferSeconds}s buffer).");
            FireGo(target, adFreeSeconds, AdFreeColor);
            await Task.Delay(TimeSpan.FromSeconds(adFreeSeconds), token);

            Logger.Log("[TWITCH]", "Ad-free countdown finished with no new ad break, clearing overlay to idle.");
            FireStop(target);
        }
        catch (OperationCanceledException)
        {
            Logger.Log("[TWITCH]", "Cycle interrupted (a new ad break arrived, or the sequencer was stopped).");
        }
        catch (Exception ex)
        {
            Logger.Log("[ERROR]", $"Ad sequencer cycle crashed unexpectedly: {ex}");
        }
    }

    private static void FireGo(AdSequencerTarget target, int seconds, string color)
    {
        var qs = new NameValueCollection
        {
            ["t"] = TimeParsing.SecsToHms(seconds),
            ["color"] = color
        };

        if (target is AdSequencerTarget.Bar or AdSequencerTarget.Both)
            LogIfError(OverlayCommandExecutor.ExecuteBar("go", qs));
        if (target is AdSequencerTarget.Radial or AdSequencerTarget.Both)
            LogIfError(OverlayCommandExecutor.ExecuteRadial("go", qs));
    }

    private static void FireStop(AdSequencerTarget target)
    {
        var qs = new NameValueCollection();
        if (target is AdSequencerTarget.Bar or AdSequencerTarget.Both)
            LogIfError(OverlayCommandExecutor.ExecuteBar("stop", qs));
        if (target is AdSequencerTarget.Radial or AdSequencerTarget.Both)
            LogIfError(OverlayCommandExecutor.ExecuteRadial("stop", qs));
    }

    private static void LogIfError(OverlayCommandExecutor.CommandResult result)
    {
        if (!result.Ok)
            Logger.Log("[ERROR]", $"Ad sequencer command failed: {result.Error}");
    }
}