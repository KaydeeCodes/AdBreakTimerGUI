using System.Text.RegularExpressions;

namespace AdBreakTimerGUI.Engine;

// Colour parsing for query strings and text boxes.
public static class ColorParsing
{
    // Accepts hex (with or without #), CSS names, rgb()/rgba()/hsl()/hsla(), or "transparent". URL decoded first since %23 is how # arrives over the wire.
    public static string? ParseColor(string value)
    {
        value = Uri.UnescapeDataString(value).Trim();
        if (value is "" or "null") return null;
        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return "transparent";

        // Bare hex without a # gets one added back.
        if (Regex.IsMatch(value, @"^[0-9a-fA-F]{3,8}$"))
            value = "#" + value;

        if (Regex.IsMatch(value, @"^#[0-9a-fA-F]{3,8}$")) return value;

        // Not checked against a real CSS colour list, an invalid name just gets ignored by the browser, that's fine.
        if (Regex.IsMatch(value, @"^[a-zA-Z]{2,30}$")) return value;

        if (value.StartsWith("rgb(") || value.StartsWith("rgba(") || value.StartsWith("hsl(") || value.StartsWith("hsla("))
            return value;

        return null;
    }

    // Yes/no parser for flags like flash=on, keeps the fallback if the input's empty or unrecognised.
    public static bool ParseBool(string value, bool fallback) => value.ToLowerInvariant() switch
    {
        "on" or "1" or "true" or "yes" => true,
        "off" or "0" or "false" or "no" => false,
        _ => fallback
    };
}