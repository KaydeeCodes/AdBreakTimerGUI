namespace AdBreakTimerGUI.Config;

// Everything lives under %AppData%\AdBreakTimer, not next to the exe like the console version did, since Program Files needs admin rights to write to.
public static class Paths
{
    public static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdBreakTimer");

    public static readonly string SettingsFile = Path.Combine(ConfigDir, "settings.json");
    public static readonly string BarFile = Path.Combine(ConfigDir, "bar.json");
    public static readonly string RadialFile = Path.Combine(ConfigDir, "radial.json");
    public static readonly string LogFile = Path.Combine(ConfigDir, "latest.log");

    // Called once, early, before anything touches ConfigDir. Left unguarded on purpose, if this fails the app genuinely can't run, and Program.cs's global handler is what catches and logs that now.
    public static void EnsureConfigDirExists() => Directory.CreateDirectory(ConfigDir);
}