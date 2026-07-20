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
                s.LastTick = s.LastTick.Value.AddSeconds(elapsedSeconds);
            }
            return;
        }

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
    public static bool HandleCommon(string cmd, NameValueCollection qs, OverlayState s, out string? error)
    {
        error = null;
        string Get(string key) => qs[key] ?? "";

        switch (cmd)
        {
            case "go":
                {
                    string timeValue = Get("t");
                    if (string.IsNullOrEmpty(timeValue)) { error = "go requires t= (e.g. t=01:00:00)."; return true; }
                    if (!TimeParsing.TryParseDuration(timeValue, out int seconds, out error)) return true;

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
                return true;

            default:
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
            case "setrotation":
                {
                    // Only accepting multiples of 90, that's what the
                    // settings window's dropdown offers, no reason to
                    // silently accept an odd angle like 45 that nothing
                    // in the GUI would ever actually let someone pick.
                    if (!int.TryParse(Get("v"), out int rotation) || rotation % 90 != 0)
                    {
                        error = "Rotation must be 0, 90, 180, or 270.";
                        return true;
                    }
                    // Folding anything outside 0-359 back into range,
                    // and 360 down to 0, they're visually identical, no
                    // reason to store two values that mean the same
                    // thing.
                    s.RotationDegrees = ((rotation % 360) + 360) % 360;
                    return true;
                }
            default:
                error = $"Unknown command: \"{cmd}\".";
                return true;
        }
    }
}