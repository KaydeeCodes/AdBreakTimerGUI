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

// Polls Twitch's Get Ad Schedule endpoint, the same data source the
// creator dashboard's "Ad starts in..." timer is built from. This is
// deliberately separate from TwitchEventSubClient, not a replacement
// for it. channel.ad_break.begin is still what actually triggers the
// red countdown, instantly, the moment a real ad starts, that part
// doesn't change. This poller's only job is keeping the green
// countdown's target honest against snoozes and Twitch's own dynamic
// pacing, things the original fixed maths had no way to notice.
public class TwitchAdSchedulePoller
{
    private static readonly HttpClient Http = new();
    private readonly Func<Task<TwitchTokenData?>> _getToken;

    public event EventHandler<AdScheduleData>? ScheduleUpdated;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public TwitchAdSchedulePoller(Func<Task<TwitchTokenData?>> getToken)
    {
        _getToken = getToken;
    }

    public void Start()
    {
        if (_loopTask is { IsCompleted: false }) return;
        Logger.Log("[TWITCH]", "Ad schedule poller starting.");
        _cts = new CancellationTokenSource();
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
            await FetchAndPublishAsync(token);

            try { await Task.Delay(TimeSpan.FromSeconds(30), token); }
            catch (OperationCanceledException) { break; }
        }
        Logger.Log("[TWITCH]", "Ad schedule poller stopped.");
    }

    // Called on demand, from TwitchAdSequencer, the moment the
    // ad-free phase actually starts. This is what lets the green
    // countdown show the real target from its very first frame
    // instead of a local guess that then visibly jumps once the next
    // scheduled 30 second poll corrects it. Genuinely optional, the
    // caller passes its own short timeout, and just falls back to the
    // old guess-then-correct behaviour if this doesn't come back in
    // time.
    public async Task<AdScheduleData?> PollNowAsync(CancellationToken token) => await FetchAndPublishAsync(token);

    // The one real fetch, used by both the background loop and the
    // on-demand call above, so there's only one place actually talking
    // to Twitch's API and only one place parsing the response.
    private async Task<AdScheduleData?> FetchAndPublishAsync(CancellationToken token)
    {
        try
        {
            TwitchTokenData? token2 = await _getToken();
            if (token2 is null) return null;

            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.twitch.tv/helix/channels/ads?broadcaster_id={token2.UserId}");
            request.Headers.Add("Authorization", $"Bearer {token2.AccessToken}");
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
            {
                // Perfectly normal when the channel's offline, not an
                // error, just nothing to correct against right now.
                return null;
            }

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
            // Either the app's shutting down, or (for PollNowAsync
            // specifically) the caller's short timeout ran out. Either
            // way, not a real error, just no fresh data in time.
            return null;
        }
        catch (Exception ex)
        {
            Logger.Log("[ERROR]", $"Ad schedule poll failed: {ex.Message}");
            return null;
        }
    }

    // Twitch's documentation shows this as a quoted RFC3339 string,
    // but real responses actually send a plain Unix timestamp number
    // instead, confirmed by Twitch staff on their own developer forum
    // as a known documentation inconsistency they don't plan to fix.
    private static DateTime? ReadFlexibleTimestamp(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop))
            return null;

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