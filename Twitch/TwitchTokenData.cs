using System.Text.Json.Serialization;

namespace AdBreakTimerGUI.Twitch;

// Everything I need to remember about a connected Twitch account
// between launches. This class is just the shape of the data,
// TwitchTokenStore is what actually saves and loads it encrypted.
public class TwitchTokenData
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = "";

    // Stored as an absolute point in time rather than "expires in N
    // seconds", so I only need DateTime.UtcNow to know if it's still
    // good, rather than also remembering when it was issued.
    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("login")]
    public string Login { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    // A small buffer so I refresh a little before the token actually
    // expires, rather than right on the edge and risking a request
    // failing mid-flight because it expired a second too early.
    [JsonIgnore]
    public bool IsExpiredOrExpiringSoon => DateTime.UtcNow >= ExpiresAt.AddMinutes(-2);
}