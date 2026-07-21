using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AdBreakTimerGUI.Config;

namespace AdBreakTimerGUI.Twitch;

public class AdBreakBeganEventArgs : EventArgs
{
    public int DurationSeconds { get; init; }
    public bool IsAutomatic { get; init; }
}

// Keeps a live WebSocket to Twitch's EventSub service, raises AdBreakBegan on channel.ad_break.begin. Owns its own reconnect logic, drops are normal for any long lived WebSocket.
public class TwitchEventSubClient
{
    private static readonly string DefaultUrl = TwitchConstants.EventSubWebSocketUrl;
    private static readonly HttpClient Http = new();

    // Supplies a fresh, valid token on demand rather than a fixed string, since the connection can outlive one token's lifetime.
    private readonly Func<Task<TwitchTokenData?>> _getToken;

    public event EventHandler<AdBreakBeganEventArgs>? AdBreakBegan;

    private CancellationTokenSource? _runCts;
    private Task? _runTask;

    public TwitchEventSubClient(Func<Task<TwitchTokenData?>> getToken)
    {
        _getToken = getToken;
    }

    public void Start()
    {
        if (_runTask is { IsCompleted: false })
        {
            Logger.Log("[TWITCH]", "EventSub Start() called but it's already running, ignoring.");
            return;
        }
        Logger.Log("[TWITCH]", "EventSub client starting.");
        _runCts = new CancellationTokenSource();
        _runTask = Task.Run(() => RunForeverAsync(_runCts.Token));
    }

    public void Stop()
    {
        if (_runCts == null) return;
        Logger.Log("[TWITCH]", "EventSub client stopping.");
        _runCts.Cancel();
    }

    private async Task RunForeverAsync(CancellationToken token)
    {
        string url = DefaultUrl;
        while (!token.IsCancellationRequested)
        {
            try
            {
                string? nextUrl = await RunOneConnectionAsync(url, token);
                // A graceful reconnect_url keeps the existing subscription, anything else is a genuine drop and needs a fresh session that resubscribes.
                url = nextUrl ?? DefaultUrl;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Log("[ERROR]", $"Twitch EventSub connection error: {ex.Message}");
                url = DefaultUrl;
            }

            if (token.IsCancellationRequested) break;
            Logger.Log("[TWITCH]", "EventSub connection ended, reconnecting shortly.");
            try { await Task.Delay(TimeSpan.FromSeconds(5), token); }
            catch (OperationCanceledException) { break; }
        }
        Logger.Log("[TWITCH]", "EventSub run loop stopped.");
    }

    private async Task<string?> RunOneConnectionAsync(string url, CancellationToken token)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(url), token);
        Logger.Log("[TWITCH]", $"EventSub connected to {url}");

        bool isFreshSession = url == DefaultUrl;
        string? reconnectUrl = null;
        var buffer = new byte[8192];

        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            using var messageStream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                messageStream.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close) break;

            string json = Encoding.UTF8.GetString(messageStream.ToArray());

            try
            {
                reconnectUrl = await HandleMessageAsync(json, isFreshSession);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // A malformed or unexpected message shouldn't kill the whole connection, that used to force a full reconnect and resubscribe over a single bad message. Log it and keep reading.
                Logger.Log("[ERROR]", $"Failed to handle EventSub message: {ex.Message}");
                continue;
            }

            if (reconnectUrl != null) break;
        }

        if (socket.State == WebSocketState.Open)
        {
            try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); }
            catch { }
        }

        return reconnectUrl;
    }

    private async Task<string?> HandleMessageAsync(string json, bool isFreshSession)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        string messageType = doc.RootElement.GetProperty("metadata").GetProperty("message_type").GetString() ?? "";

        switch (messageType)
        {
            case "session_welcome":
                {
                    string sessionId = doc.RootElement.GetProperty("payload").GetProperty("session").GetProperty("id").GetString() ?? "";
                    Logger.Log("[TWITCH]", $"EventSub session {sessionId} ready.");
                    if (isFreshSession)
                        await SubscribeToAdBreaksAsync(sessionId);
                    return null;
                }

            case "session_keepalive":
                return null; // just a heartbeat, not worth logging every one

            case "session_reconnect":
                {
                    string url = doc.RootElement.GetProperty("payload").GetProperty("session").GetProperty("reconnect_url").GetString() ?? "";
                    Logger.Log("[TWITCH]", "EventSub asked to reconnect to a new session.");
                    return url;
                }

            case "revocation":
                {
                    string reason = "unknown";
                    if (doc.RootElement.TryGetProperty("payload", out var p) &&
                        p.TryGetProperty("subscription", out var s) &&
                        s.TryGetProperty("status", out var st))
                        reason = st.GetString() ?? "unknown";
                    Logger.Log("[ERROR]", $"Twitch revoked the ad break subscription: {reason}");
                    return null;
                }

            case "notification":
                {
                    string subscriptionType = doc.RootElement.GetProperty("metadata").GetProperty("subscription_type").GetString() ?? "";
                    Logger.Log("[TWITCH]", $"EventSub notification received: {subscriptionType}");

                    if (subscriptionType == "channel.ad_break.begin")
                    {
                        JsonElement eventEl = doc.RootElement.GetProperty("payload").GetProperty("event");
                        int duration = eventEl.TryGetProperty("duration_seconds", out var d) ? ReadFlexibleInt(d) : 0;
                        bool isAutomatic = eventEl.TryGetProperty("is_automatic", out var a) && ReadFlexibleBool(a);

                        Logger.Log("[TWITCH]", $"Ad break began: {duration}s, automatic={isAutomatic}. Raising AdBreakBegan.");
                        AdBreakBegan?.Invoke(this, new AdBreakBeganEventArgs { DurationSeconds = duration, IsAutomatic = isAutomatic });
                    }
                    return null;
                }

            default:
                Logger.Log("[TWITCH]", $"EventSub received an unhandled message type: {messageType}");
                return null;
        }
    }

    private static int ReadFlexibleInt(JsonElement el) =>
        el.ValueKind == JsonValueKind.String ? int.Parse(el.GetString() ?? "0") : el.GetInt32();

    private static bool ReadFlexibleBool(JsonElement el) =>
        el.ValueKind == JsonValueKind.String ? bool.Parse(el.GetString() ?? "false") : el.GetBoolean();

    private async Task SubscribeToAdBreaksAsync(string sessionId)
    {
        TwitchTokenData? token = await _getToken();
        if (token is null)
        {
            Logger.Log("[ERROR]", "Couldn't subscribe to ad breaks, no valid Twitch token.");
            return;
        }

        try
        {
            var body = new
            {
                type = "channel.ad_break.begin",
                version = "1",
                condition = new { broadcaster_user_id = token.UserId },
                transport = new { method = "websocket", session_id = sessionId }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, TwitchConstants.EventSubSubscriptionUrl);
            request.Headers.Add("Authorization", $"Bearer {token.AccessToken}");
            request.Headers.Add("Client-Id", TwitchConstants.ClientId);
            request.Content = JsonContent.Create(body);

            var response = await Http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                Logger.Log("[TWITCH]", $"Subscribed to channel.ad_break.begin for broadcaster {token.UserId}.");
            }
            else
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                Logger.Log("[ERROR]", $"Failed to subscribe to ad breaks: {(int)response.StatusCode} {responseBody}");
            }
        }
        catch (Exception ex)
        {
            Logger.Log("[ERROR]", $"Subscribing to ad breaks threw: {ex.Message}");
        }
    }
}