using System.Text.Json;

namespace AdBreakTimerGUI.Config;

// A small wrapper around loading and saving JSON files, used for both
// settings.json and the two overlay state files. Keeping this in one
// place means I only have one spot to fix if I ever change how I
// serialise things, rather than repeating the same JsonSerializer
// calls everywhere.
public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // Returns null if the file doesn't exist yet, or if it's there but
    // I can't parse it (corrupt, hand edited badly, whatever). Either
    // way I fall back to defaults rather than crashing the app over a
    // bad config file, same as the console version did.
    public static T? Load<T>(string file) where T : class
    {
        if (!File.Exists(file)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(file), Options);
        }
        catch
        {
            return null;
        }
    }

    // Writes the object out as indented JSON, creating the containing
    // folder first if it doesn't exist yet.
    public static void Save<T>(T obj, string file)
    {
        string? dir = Path.GetDirectoryName(file);
        if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(file, JsonSerializer.Serialize(obj, Options));
    }
}