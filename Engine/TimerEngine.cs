using System.Collections.Specialized;

namespace AdBreakTimerGUI.Engine;

// This is the actual heart of the app: the countdown maths and the
// command handling that both overlays share. I've kept the method
// names and structure as close as I can to how the console version
// did it, since I already know that logic works and I don't want to
// second guess it while I'm restructuring things around a GUI.
public static class TimerEngine
{
    // ------------------------------------------------------------
    // Tick
    // ------------------------------------------------------------
    // I don't run a background timer loop anywhere. Instead every
    // state object stores LastTick (when I last did this calculation)
    // and I work out how much time has actually passed based on the
    // wall clock whenever something asks. That means the countdown
    // stays accurate even if the overlay page isn't polling for a
    // while, or the app itself gets minimised or the machine sleeps,
    // since I'm never relying on a timer callback firing on schedule.
    public static void Tick(OverlayState s)
    {
        if (s.Status == "running" && s.LastTick is not null)
        {
            int elapsedSeconds = (int)Math.Floor((DateTime.UtcNow - s.LastTick.Value).TotalSeconds);
            if (elapsedSeconds <= 0) return;

            s.Remaining -= elapsedSeconds;
            if (s.Remaining <= 0)
            {
                s.Remaining = 0;
                s.Status = "finished";
                s.LastTick = null;
                s.FinishedAt = DateTime.UtcNow;
            }
            else
            {
                // I'm deliberately advancing LastTick by exactly the
                // whole seconds I just counted, rather than resetting
                // it to "now". Math.Floor above always throws away a
                // fraction of a second on every check, and resetting
                // to "now" instead of "the last whole second I counted"
                // means that fraction is lost for good, every single
                // cycle. Over a long countdown, polled several times a
                // second, that adds up to real drift. This was a bug
                // in the console version and I'm not reintroducing it
                // here.
                s.LastTick = s.LastTick.Value.AddSeconds(elapsedSeconds);
            }
            return;
        }

        // Once something's finished, I want it to flash for
        // FlashDuration seconds and then quietly go back to idle by
        // itself, so the overlay never gets stuck lit up forever if
        // the next command is late arriving. This runs on every check
        // (including plain status polls), so the switch back to idle
        // happens on schedule regardless of whether a new command
        // ever turns up.
        if (s.Status == "finished" && s.FinishedAt is not null)
        {
            double secondsSinceFinish = (DateTime.UtcNow - s.FinishedAt.Value).TotalSeconds;
            if (secondsSinceFinish >= s.FlashDuration)
            {
                s.Status = "idle";
                s.FinishedAt = null;
            }
        }
    }

    // ------------------------------------------------------------
    // Commands shared by both overlays
    // ------------------------------------------------------------
    // Returns true if I handled this command here (whether it worked
    // or produced an error), false if it's not one I know about, in
    // which case the caller checks its own overlay specific commands
    // next (setbarheight, setsize, and so on).
    public static bool HandleCommon(string cmd, NameValueCollection qs, OverlayState s, out string? error)
    {
        error = null;
        string Get(string key) => qs[key] ?? "";

        switch (cmd)
        {
            // This is the one command I actually use day to day. One
            // request sets whatever's given and starts the countdown
            // straight away, instead of chaining several calls
            // together from Streamer.bot.
            case "go":
                {
                    string timeValue = Get("t");
                    if (string.IsNullOrEmpty(timeValue)) { error = "go requires t= (e.g. t=01:00:00)."; return true; }
                    int seconds = TimeParsing.HmsToSecs(timeValue);
                    if (seconds <= 0) { error = "Time must be greater than zero."; return true; }

                    if (!string.IsNullOrEmpty(Get("color")))
                    {
                        string? color = ColorParsing.ParseColor(Get("color"));
                        if (color != null) s.Color = color;
                    }
                    if (!string.IsNullOrEmpty(Get("finish")))
                    {
                        string? finishColor = ColorParsing.ParseColor(Get("finish"));
                        if (finishColor != null) s.FinishColor = finishColor;
                    }
                    if (!string.IsNullOrEmpty(Get("dir"))) s.Direction = Get("dir").ToLowerInvariant();
                    if (!string.IsNullOrEmpty(Get("flash"))) s.FlashOnFinish = ColorParsing.ParseBool(Get("flash"), s.FlashOnFinish);
                    if (!string.IsNullOrEmpty(Get("flashfor")) && int.TryParse(Get("flashfor"), out int flashSeconds) && flashSeconds >= 0)
                        s.FlashDuration = flashSeconds;

                    s.Remaining = seconds;
                    s.InitialTime = seconds;
                    s.Status = "running";
                    s.LastTick = DateTime.UtcNow;
                    s.FinishedAt = null;
                    return true;
                }

            case "start":
                if (s.Remaining <= 0) { error = "No time set, use settime or go first."; return true; }
                if (s.InitialTime <= 0) s.InitialTime = s.Remaining;
                s.Status = "running";
                s.LastTick = DateTime.UtcNow;
                s.FinishedAt = null;
                return true;

            case "pause":
                if (s.Status == "running") { s.Status = "paused"; s.LastTick = null; }
                return true;

            case "stop":
                s.Status = "idle";
                s.Remaining = 0;
                s.LastTick = null;
                s.FinishedAt = null;
                return true;

            case "reset":
                s.Status = "idle";
                s.Remaining = s.InitialTime;
                s.LastTick = null;
                s.FinishedAt = null;
                return true;

            case "settime":
                {
                    string timeValue = Get("t");
                    if (string.IsNullOrEmpty(timeValue)) { error = "Missing t= value (e.g. t=01:30:00)."; return true; }
                    int seconds = TimeParsing.HmsToSecs(timeValue);
                    if (seconds <= 0) { error = "Time must be greater than zero."; return true; }
                    s.Remaining = seconds;
                    s.InitialTime = seconds;
                    s.Status = "idle";
                    s.LastTick = null;
                    s.FinishedAt = null;
                    return true;
                }

            case "addtime":
                {
                    if (!int.TryParse(Get("s"), out int secondsToAdd)) { error = "Missing s= value."; return true; }
                    s.Remaining += Math.Abs(secondsToAdd);
                    if (s.InitialTime <= 0) s.InitialTime = s.Remaining;
                    // If it had already finished and I'm topping up time,
                    // I bring it back to paused rather than leaving it
                    // stuck showing the finished flash with time left on
                    // the clock again.
                    if (s.Status == "finished" && s.Remaining > 0)
                    {
                        s.Status = "paused";
                        s.FinishedAt = null;
                    }
                    return true;
                }

            case "subtime":
                {
                    if (!int.TryParse(Get("s"), out int secondsToSubtract)) { error = "Missing s= value."; return true; }
                    s.Remaining = Math.Max(0, s.Remaining - Math.Abs(secondsToSubtract));
                    if (s.Remaining == 0 && s.Status == "running")
                    {
                        s.Status = "finished";
                        s.LastTick = null;
                        s.FinishedAt = DateTime.UtcNow;
                    }
                    return true;
                }

            case "setcolor":
                {
                    string? color = ColorParsing.ParseColor(Get("v"));
                    if (color == null) { error = "Invalid colour."; return true; }
                    s.Color = color;
                    return true;
                }

            case "setfinishcolor":
                {
                    string? color = ColorParsing.ParseColor(Get("v"));
                    if (color == null) { error = "Invalid colour."; return true; }
                    s.FinishColor = color;
                    return true;
                }

            case "setbgcolor":
                {
                    string? color = ColorParsing.ParseColor(Get("v"));
                    if (color == null) { error = "Invalid colour."; return true; }
                    s.BgColor = color;
                    return true;
                }

            case "setflash":
                s.FlashOnFinish = ColorParsing.ParseBool(Get("v"), s.FlashOnFinish);
                return true;

            case "setflashduration":
                if (!int.TryParse(Get("v"), out int newFlashDuration) || newFlashDuration < 0)
                {
                    error = "Missing or invalid v= (seconds).";
                    return true;
                }
                s.FlashDuration = newFlashDuration;
                return true;

            case "status":
            case "":
                // Nothing to do, this is just the 5x a second poll from
                // the overlay page asking for the current state back.
                return true;

            default:
                // Not one of mine, let the caller check its own bar or
                // radial specific commands next.
                return false;
        }
    }

    // ------------------------------------------------------------
    // Bar specific commands
    // ------------------------------------------------------------
    public static bool HandleBarSpecific(string cmd, NameValueCollection qs, BarState s, out string? error)
    {
        error = null;
        string Get(string key) => qs[key] ?? "";

        switch (cmd)
        {
            case "setdirection":
                {
                    string direction = Get("v").ToLowerInvariant();
                    if (direction is not ("drain" or "fill")) { error = "Direction must be drain or fill."; return true; }
                    s.Direction = direction;
                    return true;
                }
            case "setbarheight":
                {
                    if (!int.TryParse(Get("v"), out int height) || height <= 0)
                    {
                        error = "Height must be a positive number of pixels.";
                        return true;
                    }
                    s.BarHeight = height;
                    return true;
                }
            case "setbarwidth":
                {
                    string width = Get("v").Trim();
                    if (width == "") { error = "Missing v= value."; return true; }
                    s.BarWidth = width;
                    return true;
                }
            default:
                error = $"Unknown command: \"{cmd}\".";
                return true;
        }
    }

    // ------------------------------------------------------------
    // Radial specific commands
    // ------------------------------------------------------------
    public static bool HandleRadialSpecific(string cmd, NameValueCollection qs, RadialState s, out string? error)
    {
        error = null;
        string Get(string key) => qs[key] ?? "";

        switch (cmd)
        {
            case "setdirection":
                {
                    string direction = Get("v").ToLowerInvariant();
                    if (direction is not ("cw" or "ccw")) { error = "Direction must be cw or ccw."; return true; }
                    s.Direction = direction;
                    return true;
                }
            case "setsize":
                {
                    if (!int.TryParse(Get("v"), out int size) || size is < 5 or > 100)
                    {
                        error = "Size must be a number from 5 to 100 (percent of the smaller viewport side).";
                        return true;
                    }
                    s.Size = size;
                    return true;
                }
            case "setthickness":
                {
                    if (!int.TryParse(Get("v"), out int thickness) || thickness is < 1 or > 50)
                    {
                        error = "Thickness must be a number from 1 to 50 (percent of the diameter).";
                        return true;
                    }
                    s.Thickness = thickness;
                    return true;
                }
            case "settrackcolor":
                {
                    string? color = ColorParsing.ParseColor(Get("v"));
                    if (color == null) { error = "Invalid colour."; return true; }
                    s.TrackColor = color;
                    return true;
                }
            default:
                error = $"Unknown command: \"{cmd}\".";
                return true;
        }
    }
}