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

    [JsonPropertyName("preferredColor")]
    public string PreferredColor { get; set; } = "#00ff00";

    [JsonPropertyName("adColor")]
    public string AdColor { get; set; } = "#ff0000";

    [JsonPropertyName("finishColor")]
    public string FinishColor { get; set; } = "#ff0000";

    [JsonPropertyName("bgColor")]
    public string BgColor { get; set; } = "transparent";

    [JsonPropertyName("finishStyle")]
    public string FinishStyle { get; set; } = "flash";

    // Setter only, deliberately no getter, this is the actual fix. With a getter present, this property got written back out on every single save (derived from FinishStyle at that moment), and then re-applied on the very next load, silently overriding "hidden" (or anything but flash/static) back to a two-state value. A setter-only property can still read an old file's genuine "flashOnFinish" key during deserialization, but System.Text.Json has nothing to read here, so it's never written out again, no more self-poisoning round trip.
    [JsonPropertyName("flashOnFinish")]
    public bool LegacyFlashOnFinish
    {
        set => FinishStyle = value ? "flash" : "static";
    }

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