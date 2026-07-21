namespace AdBreakTimerGUI.Engine;

// Reading a time value in and back out again.
public static class TimeParsing
{
    // Accepts hh:mm:ss, mm:ss, or a plain number of seconds.
    public static int HmsToSecs(string value)
    {
        value = value.Trim();
        string[] parts = value.Split(':');
        if (parts.Length == 3)
            return Math.Abs(int.Parse(parts[0])) * 3600 + Math.Abs(int.Parse(parts[1])) * 60 + Math.Abs(int.Parse(parts[2]));
        if (parts.Length == 2)
            return Math.Abs(int.Parse(parts[0])) * 60 + Math.Abs(int.Parse(parts[1]));
        return int.TryParse(value, out int seconds) ? Math.Abs(seconds) : 0;
    }

    // Back to hh:mm:ss for logging and display.
    public static string SecsToHms(int totalSeconds)
    {
        totalSeconds = Math.Max(0, totalSeconds);
        return $"{totalSeconds / 3600:D2}:{totalSeconds % 3600 / 60:D2}:{totalSeconds % 60:D2}";
    }

    // Same as HmsToSecs but never throws, for text boxes and API input rather than trusted internal calls.
    public static bool TryParseDuration(string value, out int seconds, out string? error)
    {
        error = null;
        seconds = 0;
        if (string.IsNullOrWhiteSpace(value)) { error = "Enter a time, e.g. 01:30 or 90."; return false; }
        try
        {
            seconds = HmsToSecs(value);
        }
        catch
        {
            error = "Couldn't read that as mm:ss, hh:mm:ss, or a number of seconds.";
            return false;
        }
        if (seconds <= 0) { error = "Time must be greater than zero."; return false; }
        return true;
    }
}