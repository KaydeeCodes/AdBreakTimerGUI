namespace AdBreakTimerGUI.Engine;

// Reading a time value in (from Streamer.bot, a text box, wherever) and
// turning it back into a readable string for display.
public static class TimeParsing
{
    // I accept hh:mm:ss, mm:ss, or a plain number of seconds, since
    // that covers everything Streamer.bot and I both actually type.
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
    
    // For text boxes rather than a trusted query string, I want proper
    // validation instead of just letting HmsToSecs quietly return 0 on
    // rubbish input. Returns false with a readable error message if the
    // text can't be understood or comes out to zero or less.
    public static bool TryParseDuration(string value, out int seconds, out string? error)
    {
        error = null;
        seconds = 0;
        if (string.IsNullOrWhiteSpace(value)) { error = "Enter a time, e.g. 01:30 or 90."; return false; }
        try
        {
            seconds = HmsToSecs(value);
        }
        catch (FormatException)
        {
            error = "Couldn't read that as mm:ss, hh:mm:ss, or a number of seconds.";
            return false;
        }
        if (seconds <= 0) { error = "Time must be greater than zero."; return false; }
        return true;
    }

    // The other direction, turning a whole number of seconds back into
    // hh:mm:ss for logging or display.
    public static string SecsToHms(int totalSeconds)
    {
        totalSeconds = Math.Max(0, totalSeconds);
        return $"{totalSeconds / 3600:D2}:{totalSeconds % 3600 / 60:D2}:{totalSeconds % 60:D2}";
    }
}