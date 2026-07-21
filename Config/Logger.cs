namespace AdBreakTimerGUI.Config;

// File logger. No console in a GUI app, this is the whole record of what happened, and what I ask a streamer to send me for support.
public static class Logger
{
    // Fires on any [ERROR] log, MainForm listens so the traffic light can go amber the moment something's actually wrong.
    public static event Action? ErrorLogged;

    // More than one request can be in flight at once, so every write goes through this lock.
    private static readonly object WriteLock = new();

    // Overwritten fresh every launch, only the latest run ever matters for support.
    public static void StartFresh()
    {
        Paths.EnsureConfigDirExists();
        try
        {
            lock (WriteLock)
            {
                File.WriteAllText(Paths.LogFile,
                    $"Ad Break Timer log, started {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{new string('=', 60)}{Environment.NewLine}");
            }
        }
        catch
        {
            // A failed log write shouldn't stop the app starting, worst case there's just no log this run.
        }
    }

    public static void Log(string tag, string message)
    {
        try
        {
            lock (WriteLock)
            {
                File.AppendAllText(Paths.LogFile, $"{DateTime.Now:HH:mm:ss} {tag} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Same reasoning as StartFresh.
        }

        // Fired outside the lock and outside the try/catch, so the GUI hears about an error even if the write itself failed.
        if (tag == "[ERROR]")
            ErrorLogged?.Invoke();
    }
}