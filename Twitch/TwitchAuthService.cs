using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdBreakTimerGUI.Twitch;

// What the caller gets back the moment Twitch hands over a device code,
// this is what actually gets shown to me, the short code and the URL
// to go type it into.
public class DeviceCodeInfo
{
    public string UserCode { get; set; } = "";
    public string VerificationUri { get; set; } = "";
    public int ExpiresInSeconds { get; set; }
}

// Implements Twitch's Device Code Grant Flow. No secret anywhere in
// here, that's the whole point of registering as a Public client, see
// TwitchConstants.cs for why that's safe for a shipped desktop app.
public static class TwitchAuthService
{
    private static readonly HttpClient Http = new();

    // Kicks off the whole flow. onCodeReady fires once, as soon as
    // Twitch hands back a code, that's the caller's cue to show the
    // code on screen and open a browser to verificationUri. From there
    // this method just polls quietly in the background until I've
    // approved it (or it times out, or I cancel), and only returns
    // once there's a real answer either way.
    public static async Task<TwitchTokenData?> ConnectAsync(Action<DeviceCodeInfo> onCodeReady, CancellationToken cancellationToken)
    {
        var deviceResponse = await Http.PostAsync("https://id.twitch.tv/oauth2/device",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = TwitchConstants.ClientId,
                ["scopes"] = TwitchConstants.Scopes
            }), cancellationToken);

        if (!deviceResponse.IsSuccessStatusCode)
        {
            Config.Logger.Log("[ERROR]", $"Twitch device code request failed: {(int)deviceResponse.StatusCode} {await deviceResponse.Content.ReadAsStringAsync(cancellationToken)}");
            return null;
        }

        var device = await deviceResponse.Content.ReadFromJsonAsync<DeviceCodeResponse>(cancellationToken: cancellationToken);
        if (device is null) return null;

        onCodeReady(new DeviceCodeInfo
        {
            UserCode = device.UserCode,
            VerificationUri = device.VerificationUri,
            ExpiresInSeconds = device.ExpiresIn
        });

        // Twitch tells me how often it wants me polling, in seconds.
        // I respect that rather than picking my own interval, and
        // widen it further if Twitch ever comes back with slow_down.
        int intervalSeconds = Math.Max(1, device.Interval);
        DateTime deadline = DateTime.UtcNow.AddSeconds(device.ExpiresIn);

        while (DateTime.UtcNow < deadline)
        {
            if (cancellationToken.IsCancellationRequested) return null;

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);

            var tokenResponse = await Http.PostAsync("https://id.twitch.tv/oauth2/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = TwitchConstants.ClientId,
                    ["device_code"] = device.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                }), cancellationToken);

            if (tokenResponse.IsSuccessStatusCode)
            {
                var tokenData = await tokenResponse.Content.ReadFromJsonAsync<TokenSuccessResponse>(cancellationToken: cancellationToken);
                if (tokenData is null) return null;

                var (userId, login, displayName) = await FetchUserInfoAsync(tokenData.AccessToken, cancellationToken);

                var result = new TwitchTokenData
                {
                    AccessToken = tokenData.AccessToken,
                    RefreshToken = tokenData.RefreshToken,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(tokenData.ExpiresIn),
                    UserId = userId,
                    Login = login,
                    DisplayName = displayName
                };

                TwitchTokenStore.Save(result);
                return result;
            }

            // Not successful yet, this is expected while I haven't
            // approved it on the Twitch site. Read the error code
            // Twitch sends back to decide whether to keep polling,
            // slow down, or give up entirely.
            var errorBody = await tokenResponse.Content.ReadFromJsonAsync<DeviceErrorResponse>(cancellationToken: cancellationToken);
            switch (errorBody?.Message)
            {
                case "authorization_pending":
                    continue; // completely normal, I just haven't approved it yet
                case "slow_down":
                    intervalSeconds += 5;
                    continue;
                case "expired_token":
                    return null;
                case "access_denied":
                    return null;
                default:
                    Config.Logger.Log("[ERROR]", $"Twitch token poll failed: {(int)tokenResponse.StatusCode} {errorBody?.Message}");
                    return null;
            }
        }

        return null; // ran out of time without ever getting approved
    }

    // A separate, much smaller call, used once right after getting a
    // token so I know which channel I'm actually connected to
    // (display name for the GUI, user ID for the EventSub subscription
    // condition later).
    private static async Task<(string userId, string login, string displayName)> FetchUserInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/users");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        request.Headers.Add("Client-Id", TwitchConstants.ClientId);

        var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return ("", "", "");

        var body = await response.Content.ReadFromJsonAsync<HelixUsersResponse>(cancellationToken: cancellationToken);
        var user = body?.Data?.FirstOrDefault();
        return user is null ? ("", "", "") : (user.Id, user.Login, user.DisplayName);
    }

    // Exchanges a refresh token for a new access token, once the
    // stored one's expired or close to it. Doesn't need a secret
    // either, same as everything else in this file.
    public static async Task<TwitchTokenData?> RefreshAsync(TwitchTokenData current, CancellationToken cancellationToken)
    {
        var response = await Http.PostAsync("https://id.twitch.tv/oauth2/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = TwitchConstants.ClientId,
                ["refresh_token"] = current.RefreshToken,
                ["grant_type"] = "refresh_token"
            }), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Config.Logger.Log("[ERROR]", $"Twitch token refresh failed: {(int)response.StatusCode}");
            return null;
        }

        var tokenData = await response.Content.ReadFromJsonAsync<TokenSuccessResponse>(cancellationToken: cancellationToken);
        if (tokenData is null) return null;

        var updated = new TwitchTokenData
        {
            AccessToken = tokenData.AccessToken,
            RefreshToken = tokenData.RefreshToken,
            ExpiresAt = DateTime.UtcNow.AddSeconds(tokenData.ExpiresIn),
            UserId = current.UserId,
            Login = current.Login,
            DisplayName = current.DisplayName
        };

        TwitchTokenStore.Save(updated);
        return updated;
    }

    // ------------------------------------------------------------
    // Raw response shapes, just for deserialising Twitch's JSON.
    // Nothing outside this file should ever need to see these directly.
    // ------------------------------------------------------------
    private class DeviceCodeResponse
    {
        [JsonPropertyName("device_code")] public string DeviceCode { get; set; } = "";
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("interval")] public int Interval { get; set; }
        [JsonPropertyName("user_code")] public string UserCode { get; set; } = "";
        [JsonPropertyName("verification_uri")] public string VerificationUri { get; set; } = "";
    }

    private class TokenSuccessResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    private class DeviceErrorResponse
    {
        [JsonPropertyName("message")] public string Message { get; set; } = "";
    }

    private class HelixUsersResponse
    {
        [JsonPropertyName("data")] public List<HelixUser>? Data { get; set; }
    }

    private class HelixUser
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("login")] public string Login { get; set; } = "";
        [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    }
}