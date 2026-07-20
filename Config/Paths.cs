namespace AdBreakTimerGUI.Config;

// Where everything actually lives on disk. This is the one real
// departure from how the console version worked: that one wrote its
// config folder and log file next to the exe, which is fine when I'm
// just running it from a folder I control, but breaks once it's
// installed into Program Files, since a normal user account can't
// write there.
//
// Because I've decided on a per-user install (no admin rights needed
// to install or run), I'm writing everything into %AppData%\AdBreakTimer
// instead. That's the conventional place for a Windows app to keep its
// own settings and logs, and it's writable by the user without any
// prompt.
public static class Paths
{
    // Environment.SpecialFolder.ApplicationData resolves to
    // C:\Users\<me>\AppData\Roaming on a normal Windows install.
    public static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdBreakTimer");

    public static readonly string SettingsFile = Path.Combine(ConfigDir, "settings.json");
    public static readonly string BarFile = Path.Combine(ConfigDir, "bar.json");
    public static readonly string RadialFile = Path.Combine(ConfigDir, "radial.json");
    public static readonly string LogFile = Path.Combine(ConfigDir, "latest.log");

    // I call this once, early on startup, before anything tries to
    // read or write a file in ConfigDir.
    public static void EnsureConfigDirExists() => Directory.CreateDirectory(ConfigDir);
}