using System.Text.Json.Serialization;

namespace AdBreakTimerGUI.Config;

// Everything the GUI's settings section actually edits, plus the port
// and the two tray/startup checkboxes. I've dropped the wizard only
// fields the console version had (setupComplete, overlayChoice), since
// there's no wizard here for them to mean anything to any more.
public class AppSettings
{
    // Whichever port the server actually ended up bound to. I save
    // this back after every launch, since WebServerHost walks upward
    // from here if the saved port's already taken by something else.
    [JsonPropertyName("port")]
    public int Port { get; set; } = 37000;

    // Default ad break length, in seconds. 90 seconds matches what I
    // had in the mockup (01:30).
    [JsonPropertyName("adBreakSeconds")]
    public int AdBreakSeconds { get; set; } = 90;

    // Default ad free interval, in seconds. 480 seconds is 08:00.
    [JsonPropertyName("adFreeSeconds")]
    public int AdFreeSeconds { get; set; } = 480;

    // How many seconds to wait after the red ad break bar finishes
    // before starting the green ad free countdown. This is for the
    // Twitch auto detect flow, gives the flash on finish a moment to
    // actually be seen before it switches straight into the next
    // countdown. Not used by anything yet, the ad sequencer that'll
    // read this doesn't exist until the Twitch piece is built, but the
    // setting itself is small enough to add now while I'm already in
    // this file.
    [JsonPropertyName("adBufferSeconds")]
    public int AdBufferSeconds { get; set; } = 5;

    // Whether the web service should start itself the moment the app
    // launches, rather than me having to click Start every time.
    [JsonPropertyName("startAutomatically")]
    public bool StartAutomatically { get; set; } = true;

    // Whether closing the window minimises to the tray instead of
    // actually quitting. I want this on by default, since the whole
    // point is for this to sit quietly in the background while I'm
    // streaming.
    [JsonPropertyName("minimizeToTrayOnClose")]
    public bool MinimizeToTrayOnClose { get; set; } = true;

    // Whether the app should watch Twitch's EventSub feed and fire
    // overlays automatically, rather than me needing Streamer.bot (or
    // anything else) sending commands manually. Not wired up to
    // anything yet, but this is what should flip to true automatically
    // the moment a Twitch account gets connected, and stay a plain
    // checkbox the streamer can still turn back off if they want
    // manual control instead.
    [JsonPropertyName("autoDetectAds")]
    public bool AutoDetectAds { get; set; } = false;
}