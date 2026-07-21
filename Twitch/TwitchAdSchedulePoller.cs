using System.Globalization;
using System.Text.Json;
using AdBreakTimerGUI.Config;

namespace AdBreakTimerGUI.Twitch;

public class AdScheduleData
{
    public DateTime? NextAdAtUtc { get; init; }
    public DateTime? LastAdAtUtc { get; init; }
    public int DurationSeconds { get; init; }
    public int SnoozeCount { get; init; }
}

// Polls Twitch's Get Ad Schedule endpoint, same data the dashboard's countdown uses. Doesn't replace TwitchEventSubClient, that's still the instant red-bar trigger, this only keeps the green countdown's target honest.
public class TwitchAdSchedulePoller
{
    private const int PollIntervalSeconds = 30;
    private static readonly HttpClient Http = new();
    private readonly Func<Task<TwitchTokenData?>> _getToken;

    public event EventHandler<AdScheduleData>? ScheduleUpdated;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    // Shared between the background loop and on-demand polls, so an immediate poll pushes the next scheduled one back rather than firing again a few seconds later.
    private readonly object _scheduleLock = new();
    private DateTime _nextScheduledPollUtc = DateTime.MinValue;

    public TwitchAdSchedulePoller(Func<Task<TwitchTokenData?>> getToken)
    {
        _getToken = getToken;
    }

    public void Start()
    {
        if (_loopTask is { IsCompleted: false }) return;
        Logger.Log("[TWITCH]", "Ad schedule poller starting.");
        _cts = new CancellationTokenSource();
        lock (_scheduleLock) { _nextScheduledPollUtc = DateTime.UtcNow; } // poll immediately on start
        _loopTask = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Stop()
    {
        if (_cts == null) return;
        Logger.Log("[TWITCH]", "Ad schedule poller stopping.");
        _cts.Cancel();
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            DateTime nextPoll;
            lock (_scheduleLock) { nextPoll = _nextScheduledPollUtc; }

            TimeSpan wait = nextPoll - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                try { await Task.Delay(wait, token); }
                catch (OperationCanceledException) { break; }
            }

            await FetchAndPublishAsync(token);
        }
        Logger.Log("[TWITCH]", "Ad schedule poller stopped.");
    }

    // Called on demand by TwitchAdSequencer the instant the ad-free phase starts, so the green bar shows the real target from its first frame instead of a local guess corrected later.
    public Task<AdScheduleData?> PollNowAsync(CancellationToken token) => FetchAndPublishAsync(token);

    private async Task<AdScheduleData?> FetchAndPublishAsync(CancellationToken token)
    {
        // Pushed forward on every fetch, on-demand or scheduled, so the two paths never land back to back.
        lock (_scheduleLock) { _nextScheduledPollUtc = DateTime.UtcNow.AddSeconds(PollIntervalSeconds); }

        try
        {
            TwitchTokenData? twitchToken = await _getToken();
            if (twitchToken is null) return null;

            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.twitch.tv/helix/channels/ads?broadcaster_id={twitchToken.UserId}");
            request.Headers.Add("Authorization", $"Bearer {twitchToken.AccessToken}");
            request.Headers.Add("Client-Id", TwitchConstants.ClientId);

            var response = await Http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Log("[ERROR]", $"Ad schedule request failed: {(int)response.StatusCode}");
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(token);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: token);

            if (!doc.RootElement.TryGetProperty("data", out var dataArray) || dataArray.GetArrayLength() == 0)
                return null; // channel offline or nothing scheduled, not an error

            JsonElement entry = dataArray[0];
            var data = new AdScheduleData
            {
                NextAdAtUtc = ReadFlexibleTimestamp(entry, "next_ad_at"),
                LastAdAtUtc = ReadFlexibleTimestamp(entry, "last_ad_at"),
                DurationSeconds = ReadFlexibleInt(entry, "duration"),
                SnoozeCount = ReadFlexibleInt(entry, "snooze_count")
            };

            Logger.Log("[TWITCH]", $"Ad schedule poll: next_ad_at={(data.NextAdAtUtc is { } n ? n.ToLocalTime().ToString("HH:mm:ss") : "none")}, snoozes={data.SnoozeCount}");

            ScheduleUpdated?.Invoke(this, data);
            return data;
        }
        catch (OperationCanceledException)
        {
            return null; // app shutting down, or an on-demand poll's short timeout ran out
        }
        catch (Exception ex)
        {
            Logger.Log("[ERROR]", $"Ad schedule poll failed: {ex.Message}");
            return null;
        }
    }

    // Twitch's docs show this as a quoted RFC3339 string, real responses send a plain Unix number instead, confirmed by Twitch staff as a doc/reality mismatch they won't fix.
    private static DateTime? ReadFlexibleTimestamp(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop)) return null;

        if (prop.ValueKind == JsonValueKind.Number)
            return DateTimeOffset.FromUnixTimeSeconds(prop.GetInt64()).UtcDateTime;

        if (prop.ValueKind == JsonValueKind.String)
        {
            string raw = prop.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                return dto.UtcDateTime;
            if (long.TryParse(raw, out long unixSeconds))
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
        }

        return null;
    }

    private static int ReadFlexibleInt(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop)) return 0;
        if (prop.ValueKind == JsonValueKind.String) return int.TryParse(prop.GetString(), out int v) ? v : 0;
        if (prop.ValueKind == JsonValueKind.Number) return prop.GetInt32();
        return 0;
    }
}