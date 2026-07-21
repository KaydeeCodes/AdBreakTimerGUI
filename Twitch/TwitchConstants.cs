namespace AdBreakTimerGUI.Twitch;

public static class TwitchConstants
{
    public const string ClientId = "vjpoiglyr5t7chhrtzyhld1hxsnhb3";
    public const string Scopes = "channel:read:ads channel:manage:ads";

    // Real endpoints. For local testing against the Twitch CLI's mock EventSub server, swap these two for the commented ones below, then swap back.
    public const string EventSubWebSocketUrl = "wss://eventsub.wss.twitch.tv/ws";
    public const string EventSubSubscriptionUrl = "https://api.twitch.tv/helix/eventsub/subscriptions";

    //public const string EventSubWebSocketUrl = "ws://127.0.0.1:8080/ws";
    //public const string EventSubSubscriptionUrl = "http://127.0.0.1:8080/eventsub/subscriptions";
}