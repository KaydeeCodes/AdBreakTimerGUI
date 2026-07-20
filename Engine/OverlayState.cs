using System.Text.Json.Serialization;

namespace AdBreakTimerGUI.Engine;

// I'm keeping this identical in shape to the console version's OverlayState.
// That's deliberate: if I ever load an old bar.json or radial.json from the
// console app, I want it to just work here without any migration step.
public class OverlayState
{
    // How many seconds are left on the clock right now.
    [JsonPropertyName("remaining")]
    public int Remaining { get; set; }

    // The value Remaining started at for this run. I use this if I ever
    // need to work out a percentage for a progress bar, or reset back to
    // where I started.
    [JsonPropertyName("initialTime")]
    public int InitialTime { get; set; }

    // One of: idle, running, paused, finished. I'm keeping this as a
    // plain string rather than an enum so it serialises to JSON exactly
    // as the overlay pages expect, with no converter needed.
    [JsonPropertyName("status")]
    public string Status { get; set; } = "idle";

    // The wall clock time I last did a tick calculation from. Only set
    // while running, null otherwise. See TimerEngine.Tick() for why I
    // advance this by whole seconds rather than resetting it to "now"
    // on every check, that's the fix for the drift bug from the console
    // version and I don't want to reintroduce it here.
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

    // How many seconds the overlay sits in the finish colour before it
    // quietly reverts to idle by itself.
    [JsonPropertyName("flashDuration")]
    public int FlashDuration { get; set; } = 30;

    // The instant Status flipped to "finished". I use this alongside
    // FlashDuration to work out when it's time to go back to idle.
    [JsonPropertyName("finishedAt")]
    public DateTime? FinishedAt { get; set; }

    // "drain" or "fill" for the bar, "cw" or "ccw" for the radial ring.
    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "drain";
}

// The bottom progress bar overlay. Everything it needs beyond the shared
// fields above is just its own size, in pixels and a CSS width string.
public class BarState : OverlayState
{
    [JsonPropertyName("barHeight")]
    public int BarHeight { get; set; } = 5;

    [JsonPropertyName("barWidth")]
    public string BarWidth { get; set; } = "100%";
}

// The circular ring overlay. Size and thickness are both percentages
// rather than fixed pixels, so the ring scales properly if someone
// resizes the OBS Browser Source instead of looking wrong at odd sizes.
public class RadialState : OverlayState
{
    // Diameter as a percentage of the smaller side of the viewport.
    [JsonPropertyName("size")]
    public int Size { get; set; } = 60;

    // Stroke width as a percentage of the diameter, so the line stays
    // proportional as the ring itself scales.
    [JsonPropertyName("thickness")]
    public int Thickness { get; set; } = 7;

    [JsonPropertyName("trackColor")]
    public string TrackColor { get; set; } = "rgba(255,255,255,0.15)";

    // The ring's natural default is clockwise, unlike the bar which
    // drains by default. I set that here rather than in the base class
    // since it's specific to how a ring reads visually.
    public RadialState()
    {
        Direction = "cw";
    }
}