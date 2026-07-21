using System.Text.Json.Serialization;

namespace AdBreakTimerGUI.Twitch;

public class TwitchTokenData
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = "";

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("login")]
    public string Login { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    // Twitch's profile picture URL, shown next to "Connected as..." on the Twitch tab. Fetched once at connect time, carried forward on refresh rather than re-fetched every time.
    [JsonPropertyName("profileImageUrl")]
    public string ProfileImageUrl { get; set; } = "";

    [JsonIgnore]
    public bool IsExpiredOrExpiringSoon => DateTime.UtcNow >= ExpiresAt.AddMinutes(-2);
}