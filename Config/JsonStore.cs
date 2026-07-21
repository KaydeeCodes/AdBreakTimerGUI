using System.Text.Json;

namespace AdBreakTimerGUI.Config;

// Loading and saving JSON, used for settings.json and both overlay state files.
public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // Returns null on missing or corrupt files, falls back to defaults rather than crashing over a bad config file.
    public static T? Load<T>(string file) where T : class
    {
        if (!File.Exists(file)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(file), Options);
        }
        catch (Exception ex)
        {
            // Logged now rather than silently swallowed, so a "why did my settings reset" question has an actual answer in the log.
            Logger.Log("[ERROR]", $"Failed to load {file}: {ex.Message}");
            return null;
        }
    }

    // Was previously unguarded, unlike Load, a failed write here used to throw straight up to whatever UI event triggered it.
    public static void Save<T>(T obj, string file)
    {
        try
        {
            string? dir = Path.GetDirectoryName(file);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(file, JsonSerializer.Serialize(obj, Options));
        }
        catch (Exception ex)
        {
            Logger.Log("[ERROR]", $"Failed to save {file}: {ex.Message}");
        }
    }
}