using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdBreakTimerGUI.Twitch;

// What the caller gets the moment Twitch hands back a device code, the short code and URL to go type it into.
public class DeviceCodeInfo
{
    public string UserCode { get; set; } = "";
    public string VerificationUri { get; set; } = "";
    public int ExpiresInSeconds { get; set; }
}

// Twitch's Device Code Grant Flow, no secret anywhere, that's the point of the Public client type.
public static class TwitchAuthService
{
    private static readonly HttpClient Http = new();

    // onCodeReady fires once, my cue to show the code and open the browser. Polls quietly in the background from there until approved, denied, or timed out.
    public static async Task<TwitchTokenData?> ConnectAsync(Action<DeviceCodeInfo> onCodeReady, CancellationToken cancellationToken)
    {
        DeviceCodeResponse? device;
        try
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

            device = await deviceResponse.Content.ReadFromJsonAsync<DeviceCodeResponse>(cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Was unguarded before, a network blip here used to throw all the way out to the global crash handler instead of just failing the connect attempt cleanly.
            Config.Logger.Log("[ERROR]", $"Twitch device code request threw: {ex.Message}");
            return null;
        }

        if (device is null) return null;

        onCodeReady(new DeviceCodeInfo
        {
            UserCode = device.UserCode,
            VerificationUri = device.VerificationUri,
            ExpiresInSeconds = device.ExpiresIn
        });

        // Twitch tells me how often to poll, I respect that and widen it further on slow_down.
        int intervalSeconds = Math.Max(1, device.Interval);
        DateTime deadline = DateTime.UtcNow.AddSeconds(device.ExpiresIn);

        while (DateTime.UtcNow < deadline)
        {
            if (cancellationToken.IsCancellationRequested) return null;

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);

            HttpResponseMessage tokenResponse;
            try
            {
                tokenResponse = await Http.PostAsync("https://id.twitch.tv/oauth2/token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = TwitchConstants.ClientId,
                        ["device_code"] = device.DeviceCode,
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                    }), cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // A single flaky poll shouldn't end the whole connect attempt, log it and just try again next interval.
                Config.Logger.Log("[ERROR]", $"Twitch token poll threw: {ex.Message}");
                continue;
            }

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

            // Not approved yet, expected while I haven't confirmed on Twitch's site.
            var errorBody = await tokenResponse.Content.ReadFromJsonAsync<DeviceErrorResponse>(cancellationToken: cancellationToken);
            switch (errorBody?.Message)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    intervalSeconds += 5;
                    Config.Logger.Log("[TWITCH]", $"Twitch asked to slow down polling, now every {intervalSeconds}s.");
                    continue;
                case "expired_token":
                    Config.Logger.Log("[TWITCH]", "Twitch connect attempt expired before it was approved.");
                    return null;
                case "access_denied":
                    Config.Logger.Log("[TWITCH]", "Twitch connect attempt was denied.");
                    return null;
                default:
                    Config.Logger.Log("[ERROR]", $"Twitch token poll failed: {(int)tokenResponse.StatusCode} {errorBody?.Message}");
                    return null;
            }
        }

        Config.Logger.Log("[TWITCH]", "Twitch connect attempt timed out.");
        return null;
    }

    // Used once right after getting a token, for the display name shown in the GUI and the user ID EventSub needs.
    private static async Task<(string userId, string login, string displayName)> FetchUserInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/users");
            request.Headers.Add("Authorization", $"Bearer {accessToken}");
            request.Headers.Add("Client-Id", TwitchConstants.ClientId);

            var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Config.Logger.Log("[ERROR]", $"Fetching Twitch user info failed: {(int)response.StatusCode}");
                return ("", "", "");
            }

            var body = await response.Content.ReadFromJsonAsync<HelixUsersResponse>(cancellationToken: cancellationToken);
            var user = body?.Data?.FirstOrDefault();
            return user is null ? ("", "", "") : (user.Id, user.Login, user.DisplayName);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Config.Logger.Log("[ERROR]", $"Fetching Twitch user info threw: {ex.Message}");
            return ("", "", "");
        }
    }

    // Exchanges a refresh token for a new access token, no secret needed here either.
    public static async Task<TwitchTokenData?> RefreshAsync(TwitchTokenData current, CancellationToken cancellationToken)
    {
        try
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
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Config.Logger.Log("[ERROR]", $"Twitch token refresh threw: {ex.Message}");
            return null;
        }
    }

    // Raw Twitch JSON shapes, only used for deserialising within this file.
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