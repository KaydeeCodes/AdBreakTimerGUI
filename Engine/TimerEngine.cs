using System.Collections.Specialized;

namespace AdBreakTimerGUI.Engine;

// The countdown maths and command handling both overlays share.
public static class TimerEngine
{
    // No background timer loop, I diff against the wall clock whenever something asks, so it stays accurate through sleep/minimise/unload.
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
                // Advancing by exactly the whole seconds counted, not resetting to "now", that's the old drift bug's fix.
                s.LastTick = s.LastTick.Value.AddSeconds(elapsedSeconds);
            }
            return;
        }

        // Once finished, flash for FlashDuration then quietly go idle on its own, so nothing's ever stuck lit up.
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

    // Returns false if cmd isn't one of mine, so the caller checks bar/radial specific commands next.
    public static bool HandleCommon(string cmd, NameValueCollection qs, OverlayState s, out string? error)
    {
        error = null;
        string Get(string key) => qs[key] ?? "";

        switch (cmd)
        {
            // The one I use day to day: sets colour/direction/duration and starts, one request instead of chaining several.
            case "go":
                {
                    string timeValue = Get("t");
                    if (string.IsNullOrEmpty(timeValue)) { error = "go requires t= (e.g. t=01:00:00)."; return true; }
                    if (!TimeParsing.TryParseDuration(timeValue, out int seconds, out error)) return true;

                    // Deliberately lenient here, unlike setcolor/setfinishcolor below: a bad colour just gets skipped rather than failing the whole countdown, since go is the live "start it now" command.
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
                    if (!TimeParsing.TryParseDuration(timeValue, out int seconds, out error)) return true;
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
                    // Topping up a finished countdown brings it back to paused rather than leaving it stuck on the finish flash.
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
                return true; // the 5x/sec poll, nothing to do

            default:
                return false;
        }
    }

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
            case "setrotation":
                {
                    // Only multiples of 90, that's all the settings dropdown offers.
                    if (!int.TryParse(Get("v"), out int rotation) || rotation % 90 != 0)
                    {
                        error = "Rotation must be 0, 90, 180, or 270.";
                        return true;
                    }
                    // Folds 360 down to 0, they look identical.
                    s.RotationDegrees = ((rotation % 360) + 360) % 360;
                    return true;
                }
            default:
                error = $"Unknown command: \"{cmd}\".";
                return true;
        }
    }
}