using System.Text.Json.Serialization;

namespace AdBreakTimerGUI.Config;

public class AppSettings
{
    [JsonPropertyName("port")]
    public int Port { get; set; } = 37000; // whichever port actually bound, walked upward if this one's taken

    [JsonPropertyName("adBreakSeconds")]
    public int AdBreakSeconds { get; set; } = 90;

    [JsonPropertyName("adFreeSeconds")]
    public int AdFreeSeconds { get; set; } = 480; // full cycle length, same as Twitch's "every X minutes", not just the gap

    [JsonPropertyName("adBufferSeconds")]
    public int AdBufferSeconds { get; set; } = 5;

    [JsonPropertyName("startAutomatically")]
    public bool StartAutomatically { get; set; } = true;

    [JsonPropertyName("minimizeToTrayOnClose")]
    public bool MinimizeToTrayOnClose { get; set; } = true;

    [JsonPropertyName("autoDetectAds")]
    public bool AutoDetectAds { get; set; } = false;

    [JsonPropertyName("autoDetectTarget")]
    public string AutoDetectTarget { get; set; } = "Both"; // "Bar", "Radial", or "Both", stored as a string so the JSON stays hand-readable
}