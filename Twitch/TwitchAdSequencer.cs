using System.Collections.Specialized;
using AdBreakTimerGUI.Config;
using AdBreakTimerGUI.Engine;
using AdBreakTimerGUI.Server;

namespace AdBreakTimerGUI.Twitch;

public enum AdSequencerTarget { Bar, Radial, Both }

// Turns TwitchEventSubClient's AdBreakBegan into the actual red-then-green cycle, and keeps the green target honest against the schedule poller.
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

    // Keeping a reference, not just subscribing, so RunCycleAsync can ask for one immediate reading rather than only reacting passively to the background loop.
    public void AttachSchedulePoller(TwitchAdSchedulePoller poller)
    {
        _schedulePoller = poller;
        poller.ScheduleUpdated += (_, data) => AdjustAdFreeTarget(data.NextAdAtUtc);
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

        _ = RunCycleAsync(durationSeconds, newCts.Token);
    }

    // Fired by the background poller, only means anything while the ad-free phase is running, the real ad event owns the red phase entirely.
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

    private async Task RunCycleAsync(int adDurationSeconds, CancellationToken token)
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

            // Try the real answer first, before showing anything, this is what stops the old "shows a guess, then jumps" jump.
            // _phase is still Phase.Ad here, so if this poll's ScheduleUpdated fires, AdjustAdFreeTarget's phase check ignores it, no double-fire against the target set below.
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
                    // Just the timeout, not the cycle being cancelled, fall through to the local estimate below.
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

            lock (_targetLock) { _adFreeTargetUtc = targetUtc; }
            NextAdEstimatedUtc = targetUtc;
            _phase = Phase.AdFree;

            FireGo(target, adFreeSeconds, AdFreeColor);

            // Waiting in short steps against a target that can move, rather than one long delay, so AdjustAdFreeTarget can change the wait without a full cycle restart.
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

    private static void FireGo(AdSequencerTarget target, int seconds, string color)
    {
        var qs = new NameValueCollection
        {
            ["t"] = TimeParsing.SecsToHms(seconds),
            ["color"] = color
            // No dir=, that leaves whatever's already saved (drain/fill, cw/ccw) untouched.
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