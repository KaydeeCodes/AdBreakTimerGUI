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

    // The colour actually on screen right now. Changes on every go call, including the automatic red/green cycle, this is not a saved preference, it's whatever's currently being displayed.
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#00ff00";

    // The saved preference from Overlay Settings, only ever written there. Added after a real bug: the automatic Twitch cycle was reading/writing the plain Color field above to show red during ads, which silently overwrote whatever custom colour someone had picked, since that field was being used for two different jobs at once. This one's never touched by go, only by the settings dialog.
    [JsonPropertyName("preferredColor")]
    public string PreferredColor { get; set; } = "#00ff00";

    [JsonPropertyName("finishColor")]
    public string FinishColor { get; set; } = "#ff0000"; // only ever set explicitly via finish=/setfinishcolor, safe from this same problem

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
    public int Size { get; set; } = 60;

    [JsonPropertyName("thickness")]
    public int Thickness { get; set; } = 7;

    [JsonPropertyName("trackColor")]
    public string TrackColor { get; set; } = "rgba(255,255,255,0.15)";

    [JsonPropertyName("rotationDegrees")]
    public int RotationDegrees { get; set; } = 0;

    public RadialState()
    {
        Direction = "cw";
    }
}