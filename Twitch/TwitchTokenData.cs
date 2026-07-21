using System.Text.Json.Serialization;

namespace AdBreakTimerGUI.Twitch;

// The shape of what I remember about a connected account between launches, TwitchTokenStore is what actually saves/loads it encrypted.
public class TwitchTokenData
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = "";

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; } // absolute time, not "expires in N seconds", so I only need UtcNow to check it

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("login")]
    public string Login { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    // Refreshes a couple of minutes early rather than right on the edge.
    [JsonIgnore]
    public bool IsExpiredOrExpiringSoon => DateTime.UtcNow >= ExpiresAt.AddMinutes(-2);
}