namespace AdBreakTimerGUI.Twitch;

// Tied to the actual app I registered on dev.twitch.tv/console/apps,
// not something a streamer using the built app would ever need to see
// or touch themselves.
public static class TwitchConstants
{
    public const string ClientId = "vjpoiglyr5t7chhrtzyhld1hxsnhb3";

    // Space separated, this is the format Twitch wants for the device
    // code request. channel:read:ads is the one scope the ad break
    // EventSub subscription actually needs, nothing broader than that.
    public const string Scopes = "channel:read:ads";
}