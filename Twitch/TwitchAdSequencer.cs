using System.Collections.Specialized;
using AdBreakTimerGUI.Config;
using AdBreakTimerGUI.Engine;
using AdBreakTimerGUI.Server;

namespace AdBreakTimerGUI.Twitch;

public enum AdSequencerTarget { Bar, Radial, Both }

// Turns TwitchEventSubClient's AdBreakBegan into the red-then-green cycle, and keeps the green target honest against the schedule poller. Can also start the green phase directly from a poll if there's no cycle running yet, that's what covers "app opened mid-stream" or "went live while the app was already open".
public class TwitchAdSequencer
{
    private const string AdColor = "#ff0000";
    private const string AdFreeColor = "#00ff00";
    private const int MinimumAdFreeSeconds = 5;
    private static readonly TimeSpan ImmediatePollTimeout = TimeSpan.FromSeconds(5);
    private const double AdjustThresholdSeconds = 5; // ignore poll jitter smaller than this

    private enum Phase { Idle, Ad, AdFree }
    private volatile Phase _phase = Phase.Idle;

    private readonly object _targetLock = new();
    private DateTime _adFreeTargetUtc;

    private readonly Func<AppSettings> _getSettings;
    private readonly Func<AdSequencerTarget> _getTarget;
    private TwitchAdSchedulePoller? _schedulePoller;

    private CancellationTokenSource? _cycleCts;
    private readonly object _lock = new();

    public DateTime? LastAdStartedUtc { get; private set; }
    public DateTime? NextAdEstimatedUtc { get; private set; }

    public TwitchAdSequencer(Func<AppSettings> getSettings, Func<AdSequencerTarget> getTarget)
    {
        _getSettings = getSettings;
        _getTarget = getTarget;
    }

    public void Attach(TwitchEventSubClient client) => client.AdBreakBegan += (_, e) => OnAdBreakBegan(e.DurationSeconds);

    public void AttachSchedulePoller(TwitchAdSchedulePoller poller)
    {
        _schedulePoller = poller;
        poller.ScheduleUpdated += (_, data) => OnScheduleUpdated(data);
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_cycleCts != null)
                Logger.Log("[TWITCH]", "Sequencer stopping, cancelling any running cycle.");
            _cycleCts?.Cancel();
            _cycleCts = null;
        }
        _phase = Phase.Idle;
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
        NextAdEstimatedUtc = null;

        var newCts = new CancellationTokenSource();
        lock (_lock)
        {
            _cycleCts?.Cancel();
            _cycleCts = newCts;
        }

        _ = RunAdCycleAsync(durationSeconds, newCts.Token);
    }

    // Every poll result comes through here. What it does depends on the current phase: adjusts a running green countdown, starts one from scratch if nothing's running yet, or does nothing during a red countdown, the real ad event owns that entirely.
    private void OnScheduleUpdated(AdScheduleData data)
    {
        if (_phase == Phase.AdFree)
        {
            AdjustAdFreeTarget(data.NextAdAtUtc);
            return;
        }

        if (_phase == Phase.Idle && data.NextAdAtUtc is { } nextAd && nextAd > DateTime.UtcNow)
        {
            // This is the fix for "the app does nothing until the first ad happens". Without a red countdown to hand off from, there was previously no way for the green phase to ever start on its own, even though the real target was sitting right there from the very first poll.
            Logger.Log("[TWITCH]", $"No cycle running, but Twitch's schedule shows a next ad at {nextAd.ToLocalTime():HH:mm:ss}, starting the ad-free countdown now instead of waiting for that ad to actually happen.");

            var newCts = new CancellationTokenSource();
            lock (_lock)
            {
                _cycleCts?.Cancel();
                _cycleCts = newCts;
            }

            _ = RunAdFreeFromScheduleAsync(nextAd, newCts.Token);
        }
    }

    private void AdjustAdFreeTarget(DateTime? nextAdAtUtc)
    {
        if (_phase != Phase.AdFree) return;
        if (nextAdAtUtc is null) return;

        DateTime newTarget = nextAdAtUtc.Value;
        DateTime oldTarget;
        lock (_targetLock) { oldTarget = _adFreeTargetUtc; }

        double diffSeconds = Math.Abs((newTarget - oldTarget).TotalSeconds);
        if (diffSeconds < AdjustThresholdSeconds) return;

        lock (_targetLock) { _adFreeTargetUtc = newTarget; }
        NextAdEstimatedUtc = newTarget;

        int remainingSeconds = Math.Max(MinimumAdFreeSeconds, (int)(newTarget - DateTime.UtcNow).TotalSeconds);
        Logger.Log("[TWITCH]", $"Ad schedule poll adjusted the ad-free countdown by {diffSeconds:F0}s, now targeting {newTarget.ToLocalTime():HH:mm:ss} ({remainingSeconds}s remaining).");

        FireGo(_getTarget(), remainingSeconds, AdFreeColor);
    }

    // The normal path: a real ad break happened, red first, then green.
    private async Task RunAdCycleAsync(int adDurationSeconds, CancellationToken token)
    {
        try
        {
            AppSettings settings = _getSettings();
            AdSequencerTarget target = _getTarget();

            _phase = Phase.Ad;
            FireGo(target, adDurationSeconds, AdColor);
            await Task.Delay(TimeSpan.FromSeconds(adDurationSeconds), token);

            if (settings.AdBufferSeconds > 0)
            {
                Logger.Log("[TWITCH]", $"Ad countdown finished, waiting {settings.AdBufferSeconds}s before the ad-free countdown.");
                await Task.Delay(TimeSpan.FromSeconds(settings.AdBufferSeconds), token);
            }

            // Try the real answer immediately, before showing anything, avoids showing a guess that then visibly jumps once corrected.
            AdScheduleData? fresh = null;
            if (_schedulePoller != null)
            {
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeoutCts.CancelAfter(ImmediatePollTimeout);
                    fresh = await _schedulePoller.PollNowAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    // Just the timeout, not the cycle being cancelled.
                }
            }

            DateTime targetUtc;
            int adFreeSeconds;

            if (fresh?.NextAdAtUtc is { } freshNext && freshNext > DateTime.UtcNow)
            {
                targetUtc = freshNext;
                adFreeSeconds = Math.Max(MinimumAdFreeSeconds, (int)(targetUtc - DateTime.UtcNow).TotalSeconds);
                Logger.Log("[TWITCH]", $"Got a fresh schedule reading immediately, starting ad-free countdown for {adFreeSeconds}s targeting {targetUtc.ToLocalTime():HH:mm:ss}, no guess needed.");
            }
            else
            {
                int rawAdFreeSeconds = settings.AdFreeSeconds - adDurationSeconds - settings.AdBufferSeconds;
                adFreeSeconds = Math.Max(MinimumAdFreeSeconds, rawAdFreeSeconds);

                if (rawAdFreeSeconds < MinimumAdFreeSeconds)
                    Logger.Log("[ERROR]", $"Configured cycle length ({settings.AdFreeSeconds}s) is too short for a {adDurationSeconds}s ad plus a {settings.AdBufferSeconds}s buffer, flooring to {MinimumAdFreeSeconds}s.");

                targetUtc = DateTime.UtcNow.AddSeconds(adFreeSeconds);
                Logger.Log("[TWITCH]", $"Starting ad-free countdown for {adFreeSeconds}s (local estimate, no fresh schedule reading yet, the background poller will correct within 30s if needed).");
            }

            await EnterAdFreePhaseAsync(target, targetUtc, token);
        }
        catch (OperationCanceledException)
        {
            _phase = Phase.Idle;
            Logger.Log("[TWITCH]", "Cycle interrupted (a new ad break arrived, or the sequencer was stopped).");
        }
        catch (Exception ex)
        {
            _phase = Phase.Idle;
            Logger.Log("[ERROR]", $"Ad sequencer cycle crashed unexpectedly: {ex}");
        }
    }

    // The new path: no red countdown happened here, jumping straight into the green phase using a real schedule reading, this is what covers app-just-opened or went-live-while-open.
    private async Task RunAdFreeFromScheduleAsync(DateTime targetUtc, CancellationToken token)
    {
        try
        {
            await EnterAdFreePhaseAsync(_getTarget(), targetUtc, token);
        }
        catch (OperationCanceledException)
        {
            _phase = Phase.Idle;
            Logger.Log("[TWITCH]", "Schedule-started cycle interrupted (a real ad break arrived, or the sequencer was stopped).");
        }
        catch (Exception ex)
        {
            _phase = Phase.Idle;
            Logger.Log("[ERROR]", $"Schedule-started ad-free cycle crashed unexpectedly: {ex}");
        }
    }

    // Shared by both paths above: sets the target, fires the green go, waits it out in short steps so AdjustAdFreeTarget can move the target without a full restart, then clears to idle once it's actually reached with nothing new having arrived.
    private async Task EnterAdFreePhaseAsync(AdSequencerTarget target, DateTime targetUtc, CancellationToken token)
    {
        lock (_targetLock) { _adFreeTargetUtc = targetUtc; }
        NextAdEstimatedUtc = targetUtc;
        _phase = Phase.AdFree;

        int adFreeSeconds = Math.Max(MinimumAdFreeSeconds, (int)(targetUtc - DateTime.UtcNow).TotalSeconds);
        FireGo(target, adFreeSeconds, AdFreeColor);

        while (true)
        {
            DateTime currentTarget;
            lock (_targetLock) { currentTarget = _adFreeTargetUtc; }

            TimeSpan remaining = currentTarget - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            double stepMs = Math.Max(50, Math.Min(1000, remaining.TotalMilliseconds));
            await Task.Delay(TimeSpan.FromMilliseconds(stepMs), token);
        }

        _phase = Phase.Idle;
        NextAdEstimatedUtc = null;
        Logger.Log("[TWITCH]", "Ad-free countdown finished with no new ad break, clearing overlay to idle.");
        FireStop(target);
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