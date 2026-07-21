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

        CancellationTokenSource newCts;
        lock (_lock)
        {
            _cycleCts?.Cancel();
            newCts = new CancellationTokenSource();
            _cycleCts = newCts;
            // Set here, synchronously, under the same lock as the token swap, not inside RunAdCycleAsync once it eventually starts running. A real ad break and a schedule poll fire from two genuinely separate background threads, without this, there was a small window where a poll landing right after this method returns but before the async body's first line runs would still see Idle and wrongly steal the cycle from a real ad that's only microseconds old.
            _phase = Phase.Ad;
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

        if (data.NextAdAtUtc is not { } nextAd || nextAd <= DateTime.UtcNow) return;

        CancellationTokenSource newCts;
        lock (_lock)
        {
            // Re-checked here, inside the lock, this is the actual fix. OnAdBreakBegan transitions phase under this same lock, so if a real ad break claimed the cycle a moment ago, this now correctly sees Phase.Ad and backs off instead of racing it.
            if (_phase != Phase.Idle) return;

            Logger.Log("[TWITCH]", $"No cycle running, but Twitch's schedule shows a next ad at {nextAd.ToLocalTime():HH:mm:ss}, starting the ad-free countdown now instead of waiting for that ad to actually happen.");

            _cycleCts?.Cancel();
            newCts = new CancellationTokenSource();
            _cycleCts = newCts;
            _phase = Phase.AdFree;
        }

        _ = RunAdFreeFromScheduleAsync(nextAd, newCts);
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

            // _phase is already Ad here, set synchronously by OnAdBreakBegan before this method was ever scheduled to run.
            FireGo(target, adDurationSeconds, isAdPhase: true);
            await Task.Delay(TimeSpan.FromSeconds(adDurationSeconds), token);

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
        _phase = Phase.AdFree; // already set by OnScheduleUpdated for the schedule-started path, this covers the RunAdCycleAsync path where it hasn't been set yet

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

        // Deliberately not sending a stop command, TimerEngine.Tick() handles running -> finished -> flash/static/hidden -> auto idle entirely on its own from the overlay page's regular polling. Forcing a stop here would cut that natural finish behaviour off before it gets to show.
        Logger.Log("[TWITCH]", "Ad-free countdown reached its target, leaving the overlay to finish naturally rather than forcing it to idle immediately.");
    }

    // Picks between the ad-free colour and the genuinely separate ad colour, FinishColor is purely the "idle bar" state now, unrelated to which phase just ran.
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
            Logger.Log("[ERROR]", $"Ad sequencer command failed: {result.Error}");
    }
}