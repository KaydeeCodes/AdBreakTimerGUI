using AdBreakTimerGUI.Config;

namespace AdBreakTimerGUI;

static class Program
{
    [STAThread]
    static void Main()
    {
        // ThreadException catches crashes on the UI thread, the app survives and keeps running afterward.
        Application.ThreadException += (_, e) => LogAndReportCrash(e.Exception);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        // UnhandledException catches crashes on background threads, these are always fatal regardless, this just makes sure it's logged first instead of vanishing with no trace.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogAndReportCrash(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown error"));

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
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
            // Logging itself failed, nothing further to do.
        }

        MessageBox.Show($"Something went wrong.\n\n{ex.Message}\n\nCheck the log file for detail.",
            "Ad Break Timer", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}