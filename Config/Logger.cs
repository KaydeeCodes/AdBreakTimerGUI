namespace AdBreakTimerGUI.Config;

// A very small file logger. I don't have a console any more now this
// is a GUI app, so this is the only record of what happened if
// something goes wrong, and it's what I ask a streamer to send me if
// they report an issue. The GUI has an "Open log file" button that
// just opens whatever this writes to.
public static class Logger
{
    // I overwrite the log fresh on every launch rather than appending
    // forever. If someone hits a problem I only ever need the latest
    // run, and a log that grows without limit is just clutter.
    public static void StartFresh()
    {
        Paths.EnsureConfigDirExists();
        try
        {
            File.WriteAllText(Paths.LogFile,
                $"Ad Break Timer log, started {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{new string('=', 60)}{Environment.NewLine}");
        }
        catch
        {
            // If I can't write the log file, I don't want that to stop
            // the app itself from starting. Worst case, there's just
            // no log this run.
        }
    }

    // Appends one line, tagged with where it came from (e.g. "[HTTP]",
    // "[BAR]") so it's easy to scan the file for the bit I care about.
    public static void Log(string tag, string message)
    {
        try
        {
            File.AppendAllText(Paths.LogFile, $"{DateTime.Now:HH:mm:ss} {tag} {message}{Environment.NewLine}");
        }
        catch
        {
            // Same reasoning as above, a failed log write shouldn't
            // take the app down with it.
        }
    }
}