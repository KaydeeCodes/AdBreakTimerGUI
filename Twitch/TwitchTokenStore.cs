using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdBreakTimerGUI.Config;

namespace AdBreakTimerGUI.Twitch;

// Saves and loads the Twitch token to disk, encrypted with Windows
// DPAPI so it's not sitting around as plain text. DPAPI ties the
// encryption to my actual Windows user account, only the same person
// logged into the same machine can decrypt it back. I picked this over
// something like a fixed encryption key baked into the app, which
// wouldn't actually protect anything, anyone could pull the key back
// out by decompiling the exe.
public static class TwitchTokenStore
{
    private static readonly string TokenFile = Path.Combine(Paths.ConfigDir, "twitch.token");

    public static void Save(TwitchTokenData token)
    {
        Paths.EnsureConfigDirExists();
        string json = JsonSerializer.Serialize(token);
        byte[] plainBytes = Encoding.UTF8.GetBytes(json);

        // CurrentUser rather than LocalMachine, this ties it to my
        // specific Windows account rather than anyone who can log into
        // this machine at all.
        byte[] encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(TokenFile, encrypted);
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
        catch
        {
            // Corrupt file, or encrypted under a different Windows
            // account than the one running now (DPAPI fails to
            // unprotect in that case), either way I can't use it, so I
            // treat it the same as "never connected" rather than
            // crashing over it.
            return null;
        }
    }

    public static void Delete()
    {
        if (File.Exists(TokenFile)) File.Delete(TokenFile);
    }

    public static bool HasSavedToken => File.Exists(TokenFile);
}