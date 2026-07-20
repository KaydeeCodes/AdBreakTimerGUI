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
}