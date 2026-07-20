namespace AdBreakTimerGUI.Config;

// A very small file logger. I don't have a console any more now this
// is a GUI app, so this is the only record of what happened if
// something goes wrong, and it's what I ask a streamer to send me if
// they report an issue. The GUI has an "Open log file" button that
// just opens whatever this writes to.
public static class Logger
{
    // Raised whenever something logs an [ERROR] line. MainForm listens
    // to this so the traffic light can go amber the moment something
    // actually goes wrong, rather than needing separate error tracking
    // wired up in every place that could fail. This can fire from a
    // background thread (a request handler, the server's listen loop),
    // so anything subscribing needs to marshal back onto the UI thread
    // before touching a control, same as WebServerHost.StatusChanged.
    public static event Action? ErrorLogged;

    // The overlay pages poll five times a second each, and commands can
    // arrive from more than one source close together (a browser tab
    // I'm testing with, Streamer.bot, and so on), so more than one
    // request can genuinely be in flight at the same time. Writing to
    // the same file from two threads at once isn't safe without
    // something like this, found during a full review rather than it
    // actually breaking on me, but it's a real risk under load, so
    // every write goes through this lock.
    private static readonly object WriteLock = new();

    // I overwrite the log fresh on every launch rather than appending
    // forever. If someone hits a problem I only ever need the latest
    // run, and a log that grows without limit is just clutter.
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
            lock (WriteLock)
            {
                File.AppendAllText(Paths.LogFile, $"{DateTime.Now:HH:mm:ss} {tag} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Same reasoning as above, a failed log write shouldn't
            // take the app down with it.
        }

        // Firing this outside the try/catch above and outside the lock,
        // on purpose. I want the GUI to hear about an error even if the
        // log file itself couldn't be written to, and I don't want an
        // exception thrown by whatever's listening to this event to
        // ever be blamed on the logger itself.
        if (tag == "[ERROR]")
            ErrorLogged?.Invoke();
    }
}