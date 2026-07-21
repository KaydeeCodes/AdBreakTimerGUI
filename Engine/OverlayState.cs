using System.Text.Json.Serialization;

namespace AdBreakTimerGUI.Engine;

// Same shape as the console app's OverlayState so old bar.json/radial.json files still load.
public class OverlayState
{
    [JsonPropertyName("remaining")]
    public int Remaining { get; set; }

    [JsonPropertyName("initialTime")]
    public int InitialTime { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "idle"; // idle, running, paused, finished

    [JsonPropertyName("lastTick")]
    public DateTime? LastTick { get; set; }

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#00ff00";

    [JsonPropertyName("finishColor")]
    public string FinishColor { get; set; } = "#ff0000";

    [JsonPropertyName("bgColor")]
    public string BgColor { get; set; } = "transparent";

    [JsonPropertyName("flashOnFinish")]
    public bool FlashOnFinish { get; set; } = true;

    [JsonPropertyName("flashDuration")]
    public int FlashDuration { get; set; } = 30;

    [JsonPropertyName("finishedAt")]
    public DateTime? FinishedAt { get; set; }

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "drain";
}

public class BarState : OverlayState
{
    [JsonPropertyName("barHeight")]
    public int BarHeight { get; set; } = 5;

    [JsonPropertyName("barWidth")]
    public string BarWidth { get; set; } = "100%";
}

public class RadialState : OverlayState
{
    [JsonPropertyName("size")]
    public int Size { get; set; } = 60; // % of the smaller viewport side

    [JsonPropertyName("thickness")]
    public int Thickness { get; set; } = 7; // % of the diameter

    [JsonPropertyName("trackColor")]
    public string TrackColor { get; set; } = "rgba(255,255,255,0.15)";

    // Extra rotation on top of the ring's 12 o'clock start, 0/90/180/270 only, per the settings dropdown.
    [JsonPropertyName("rotationDegrees")]
    public int RotationDegrees { get; set; } = 0;

    public RadialState()
    {
        Direction = "cw";
    }
}