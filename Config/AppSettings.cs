using System.Text.Json.Serialization;

namespace AdBreakTimerGUI.Config;

public class AppSettings
{
    [JsonPropertyName("port")]
    public int Port { get; set; } = 37000;

    [JsonPropertyName("adBreakSeconds")]
    public int AdBreakSeconds { get; set; } = 90;

    [JsonPropertyName("adFreeSeconds")]
    public int AdFreeSeconds { get; set; } = 480;

    [JsonPropertyName("adBufferSeconds")]
    public int AdBufferSeconds { get; set; } = 5;

    [JsonPropertyName("startAutomatically")]
    public bool StartAutomatically { get; set; } = true;

    [JsonPropertyName("minimizeToTrayOnClose")]
    public bool MinimizeToTrayOnClose { get; set; } = true;

    [JsonPropertyName("autoDetectAds")]
    public bool AutoDetectAds { get; set; } = false;

    // Which overlay(s) the Twitch auto detection should drive: "Bar",
    // "Radial", or "Both". Stored as a plain string rather than a C#
    // enum directly, keeps settings.json readable if I ever open it by
    // hand, and sidesteps any enum serialisation quirks.
    [JsonPropertyName("autoDetectTarget")]
    public string AutoDetectTarget { get; set; } = "Both";
}