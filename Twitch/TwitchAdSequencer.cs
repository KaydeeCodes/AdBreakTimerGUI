using System.Collections.Specialized;
using AdBreakTimerGUI.Config;
using AdBreakTimerGUI.Engine;
using AdBreakTimerGUI.Server;

namespace AdBreakTimerGUI.Twitch;

public enum AdSequencerTarget { Bar, Radial, Both }

public class TwitchAdSequencer
{
    private const int MinimumAdFreeSeconds = 5;
    private static readonly TimeSpan ImmediatePollTimeout = TimeSpan.FromSeconds(5);
    private const double AdjustThresholdSeconds = 5;

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

        _ = RunAdCycleAsync(durationSeconds, newCts);
    }

    private void OnScheduleUpdated(AdScheduleData data)
    {
        if (_phase == Phase.AdFree)
        {
            AdjustAdFreeTarget(data.NextAdAtUtc);
            return;
        }

        if (_phase == Phase.Idle && data.NextAdAtUtc is { } nextAd && nextAd > DateTime.UtcNow)
        {
            Logger.Log("[TWITCH]", $"No cycle running, but Twitch's schedule shows a next ad at {nextAd.ToLocalTime():HH:mm:ss}, starting the ad-free countdown now instead of waiting for that ad to actually happen.");

            var newCts = new CancellationTokenSource();
            lock (_lock)
            {
                _cycleCts?.Cancel();
                _cycleCts = newCts;
            }

            _ = RunAdFreeFromScheduleAsync(nextAd, newCts);
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

        FireGo(_getTarget(), remainingSeconds, isAdPhase: false);
    }

    private bool StillOwnsCurrentCycle(CancellationTokenSource myCts)
    {
        lock (_lock)
        {
            return ReferenceEquals(_cycleCts, myCts);
        }
    }

    private async Task RunAdCycleAsync(int adDurationSeconds, CancellationTokenSource cts)
    {
        CancellationToken token = cts.Token;
        try
        {
            AppSettings settings = _getSettings();
            AdSequencerTarget target = _getTarget();

            _phase = Phase.Ad;
            FireGo(target, adDurationSeconds, isAdPhase: true);
            await Task.Delay(TimeSpan.FromSeconds(adDurationSeconds), token);

            // This pause is what gives the ad's own finish flash room to actually be seen before the next go call overwrites it, same idea as the fix below for the ad-free side, just already working here because this wait already existed for a different stated reason.
            if (settings.AdBufferSeconds > 0)
            {
                Logger.Log("[TWITCH]", $"Ad countdown finished, waiting {settings.AdBufferSeconds}s before the ad-free countdown.");
                await Task.Delay(TimeSpan.FromSeconds(settings.AdBufferSeconds), token);
            }

            AdScheduleData? fresh = null;
            if (_schedulePoller != null)
            {
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeoutCts.CancelAfter(ImmediatePollTimeout);
                    fresh = await _schedulePoller.PollNowAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            }

            DateTime targetUtc;

            if (fresh?.NextAdAtUtc is { } freshNext && freshNext > DateTime.UtcNow)
            {
                targetUtc = freshNext;
                Logger.Log("[TWITCH]", $"Got a fresh schedule reading immediately, targeting {targetUtc.ToLocalTime():HH:mm:ss}, no guess needed.");
            }
            else
            {
                int rawAdFreeSeconds = settings.AdFreeSeconds - adDurationSeconds - settings.AdBufferSeconds;
                int adFreeSeconds = Math.Max(MinimumAdFreeSeconds, rawAdFreeSeconds);

                if (rawAdFreeSeconds < MinimumAdFreeSeconds)
                    Logger.Log("[ERROR]", $"Configured cycle length ({settings.AdFreeSeconds}s) is too short for a {adDurationSeconds}s ad plus a {settings.AdBufferSeconds}s buffer, flooring to {MinimumAdFreeSeconds}s.");

                targetUtc = DateTime.UtcNow.AddSeconds(adFreeSeconds);
                Logger.Log("[TWITCH]", "Starting ad-free countdown (local estimate, no fresh schedule reading yet, the background poller will correct within 30s if needed).");
            }

            await EnterAdFreePhaseAsync(target, targetUtc, token);
        }
        catch (OperationCanceledException)
        {
            if (StillOwnsCurrentCycle(cts))
            {
                _phase = Phase.Idle;
                Logger.Log("[TWITCH]", "Cycle interrupted (a new ad break arrived, or the sequencer was stopped).");
            }
        }
        catch (Exception ex)
        {
            if (StillOwnsCurrentCycle(cts))
                _phase = Phase.Idle;
            Logger.Log("[ERROR]", $"Ad sequencer cycle crashed unexpectedly: {ex}");
        }
    }

    private async Task RunAdFreeFromScheduleAsync(DateTime targetUtc, CancellationTokenSource cts)
    {
        CancellationToken token = cts.Token;
        try
        {
            await EnterAdFreePhaseAsync(_getTarget(), targetUtc, token);
        }
        catch (OperationCanceledException)
        {
            if (StillOwnsCurrentCycle(cts))
            {
                _phase = Phase.Idle;
                Logger.Log("[TWITCH]", "Schedule-started cycle interrupted (a real ad break arrived, or the sequencer was stopped).");
            }
        }
        catch (Exception ex)
        {
            if (StillOwnsCurrentCycle(cts))
                _phase = Phase.Idle;
            Logger.Log("[ERROR]", $"Schedule-started ad-free cycle crashed unexpectedly: {ex}");
        }
    }

    private async Task EnterAdFreePhaseAsync(AdSequencerTarget target, DateTime targetUtc, CancellationToken token)
    {
        lock (_targetLock) { _adFreeTargetUtc = targetUtc; }
        NextAdEstimatedUtc = targetUtc;
        _phase = Phase.AdFree;

        int adFreeSeconds = Math.Max(MinimumAdFreeSeconds, (int)(targetUtc - DateTime.UtcNow).TotalSeconds);
        FireGo(target, adFreeSeconds, isAdPhase: false);

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

        // Deliberately not sending a stop command here any more, that was the actual bug: the overlay's own Tick() already handles running -> finished -> flash -> auto idle entirely on its own, purely from the overlay page's regular polling, completely independent of anything this class does. Forcing a stop the instant our own local timer hit the target was cutting that natural flash off before it ever had a chance to show, which is exactly what looked like the bar "vanishing" instead of flashing. Same principle as the ad phase's buffer wait above, just applied by doing nothing instead of waiting, since a real ad event should be arriving right around now anyway.
        Logger.Log("[TWITCH]", "Ad-free countdown reached its target, leaving the overlay to finish and flash naturally rather than forcing it to idle immediately.");
    }

    private static void FireGo(AdSequencerTarget target, int seconds, bool isAdPhase)
    {
        if (target is AdSequencerTarget.Bar or AdSequencerTarget.Both)
        {
            var (color, finishColor) = OverlayCommandExecutor.GetBarColors();
            var qs = new NameValueCollection { ["t"] = TimeParsing.SecsToHms(seconds), ["color"] = isAdPhase ? finishColor : color };
            LogIfError(OverlayCommandExecutor.ExecuteBar("go", qs));
        }
        if (target is AdSequencerTarget.Radial or AdSequencerTarget.Both)
        {
            var (color, finishColor) = OverlayCommandExecutor.GetRadialColors();
            var qs = new NameValueCollection { ["t"] = TimeParsing.SecsToHms(seconds), ["color"] = isAdPhase ? finishColor : color };
            LogIfError(OverlayCommandExecutor.ExecuteRadial("go", qs));
        }
    }

    private static void LogIfError(OverlayCommandExecutor.CommandResult result)
    {
        if (!result.Ok)
            Logger.Log("[ERROR]", $"Ad sequencer command failed: {result.Error}");
    }
}