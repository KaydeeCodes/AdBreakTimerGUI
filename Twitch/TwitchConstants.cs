namespace AdBreakTimerGUI.Twitch;

public static class TwitchConstants
{
    public const string ClientId = "vjpoiglyr5t7chhrtzyhld1hxsnhb3";
    public const string Scopes = "channel:read:ads";

    // Real Twitch endpoints by default. For local testing against the
    // Twitch CLI's mock EventSub server, temporarily swap these two to
    // "ws://127.0.0.1:8080/ws" and "http://127.0.0.1:8080/eventsub/subscriptions",
    // then change them back once testing's done. Keeping them here in
    // one place means switching between real and mock is a two line
    // edit, not a hunt through multiple files.
    public const string EventSubWebSocketUrl = "wss://eventsub.wss.twitch.tv/ws";
    public const string EventSubSubscriptionUrl = "https://api.twitch.tv/helix/eventsub/subscriptions";
    
    //for eventsub testing.
    //public const string EventSubWebSocketUrl = "ws://127.0.0.1:8080/ws";
    //public const string EventSubSubscriptionUrl = "http://127.0.0.1:8080/eventsub/subscriptions";
}