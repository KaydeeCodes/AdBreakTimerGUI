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

    [JsonPropertyName("autoDetectTarget")]
    public string AutoDetectTarget { get; set; } = "Both";

    // Drives LocalAdSequencer, a basic self looping cycle for when Twitch isn't connected (or Auto detect ads is switched off). Twitch mode always takes priority when both could apply, this only actually runs in the gap where Twitch mode can't.
    [JsonPropertyName("localLoopEnabled")]
    public bool LocalLoopEnabled { get; set; } = false;
}