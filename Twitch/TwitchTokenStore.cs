using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdBreakTimerGUI.Config;

namespace AdBreakTimerGUI.Twitch;

// Saves/loads the Twitch token encrypted with Windows DPAPI, tied to my Windows account, not a fixed key baked into the exe that anyone could pull back out.
public static class TwitchTokenStore
{
    private static readonly string TokenFile = Path.Combine(Paths.ConfigDir, "twitch.token");

    // Was unguarded before, unlike Load, a failed write here used to throw straight up into the auth flow.
    public static void Save(TwitchTokenData token)
    {
        try
        {
            Paths.EnsureConfigDirExists();
            string json = JsonSerializer.Serialize(token);
            byte[] plainBytes = Encoding.UTF8.GetBytes(json);
            byte[] encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(TokenFile, encrypted);
        }
        catch (Exception ex)
        {
            Logger.Log("[ERROR]", $"Failed to save Twitch token: {ex.Message}");
        }
    }

    public static TwitchTokenData? Load()
    {
        if (!File.Exists(TokenFile)) return null;
        try
        {
            byte[] encrypted = File.ReadAllBytes(TokenFile);
            byte[] plainBytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            string json = Encoding.UTF8.GetString(plainBytes);
            return JsonSerializer.Deserialize<TwitchTokenData>(json);
        }
        catch (Exception ex)
        {
            // Corrupt file, or encrypted under a different Windows account (DPAPI fails to unprotect), either way treated as "never connected".
            Logger.Log("[ERROR]", $"Failed to load Twitch token: {ex.Message}");
            return null;
        }
    }

    public static void Delete()
    {
        try
        {
            if (File.Exists(TokenFile)) File.Delete(TokenFile);
        }
        catch (Exception ex)
        {
            Logger.Log("[ERROR]", $"Failed to delete Twitch token: {ex.Message}");
        }
    }

    public static bool HasSavedToken => File.Exists(TokenFile);
}