using System.Text.RegularExpressions;

namespace AdBreakTimerGUI.Engine;

// Everything to do with reading a colour value in from a query string or
// a text box and turning it into something the overlay's CSS can use
// directly.
public static class ColorParsing
{
    // I accept hex (with or without the leading #), CSS named colours,
    // rgb()/rgba()/hsl()/hsla(), or the literal word "transparent". I
    // URL decode first because %23ff0000 is how a # actually turns up
    // on the wire from a browser or Streamer.bot, and I'd rather handle
    // that here once than make every caller remember to do it.
    public static string? ParseColor(string value)
    {
        value = Uri.UnescapeDataString(value).Trim();
        if (value is "" or "null") return null;
        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return "transparent";

        // If someone's typed ff0000 without the #, I just add it back in
        // rather than making them get the encoding exactly right.
        if (Regex.IsMatch(value, @"^[0-9a-fA-F]{3,8}$"))
            value = "#" + value;

        if (Regex.IsMatch(value, @"^#[0-9a-fA-F]{3,8}$")) return value;

        // Not checked against a real list of CSS colour names. If it's
        // not a genuine one, the browser just ignores it, which is good
        // enough for me here.
        if (Regex.IsMatch(value, @"^[a-zA-Z]{2,30}$")) return value;

        if (value.StartsWith("rgb(") || value.StartsWith("rgba(") || value.StartsWith("hsl(") || value.StartsWith("hsla("))
            return value;

        // I couldn't make sense of it, so I return null and let the
        // caller decide what to do (usually: keep whatever colour was
        // already set and report an error).
        return null;
    }

    // A small yes/no parser for query string flags like flash=on. I keep
    // a fallback value so an empty or unrecognised input doesn't change
    // whatever was already set.
    public static bool ParseBool(string value, bool fallback) => value.ToLowerInvariant() switch
    {
        "on" or "1" or "true" or "yes" => true,
        "off" or "0" or "false" or "no" => false,
        _ => fallback
    };
}