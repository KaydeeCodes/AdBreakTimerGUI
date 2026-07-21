using AdBreakTimerGUI.Config;

namespace AdBreakTimerGUI;

static class Program
{
    // A unique name for the whole system to recognise, not tied to any window or process ID, just a name every launch of this app tries to claim.
    private const string SingleInstanceMutexName = "AdBreakTimerGUI_SingleInstance_9f3a2b7e";

    [STAThread]
    static void Main()
    {
        // initiallyOwned true means this launch tries to claim it immediately. isFirstInstance tells me whether that actually succeeded, or whether another running copy already owns it.
        using var mutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, createdNew: out bool isFirstInstance);

        if (!isFirstInstance)
        {
            MessageBox.Show("Ad Break Timer is already running. Check the system tray.",
                "Ad Break Timer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.ThreadException += (_, e) => LogAndReportCrash(e.Exception);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogAndReportCrash(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown error"));

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());

        // The mutex is held for this entire method's lifetime (it's a using declared at the top), which is exactly what needs to happen, it has to stay claimed for as long as the app's actually running, and releases automatically the moment Main exits, whether that's a normal close or a crash.
    }

    private static void LogAndReportCrash(Exception ex)
    {
        try
        {
            Paths.EnsureConfigDirExists();
            Logger.Log("[ERROR]", $"Unhandled crash: {ex}");
        }
        catch
        {
        }

        MessageBox.Show($"Something went wrong.\n\n{ex.Message}\n\nCheck the log file for detail.",
            "Ad Break Timer", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}