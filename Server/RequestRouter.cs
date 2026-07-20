using System.Net;
using System.Reflection;
using System.Text;
using AdBreakTimerGUI.Config;
using AdBreakTimerGUI.Engine;

namespace AdBreakTimerGUI.Server;

// Everything to do with turning one incoming HTTP request into a
// response. This is deliberately just routing and glue, all the real
// decisions about what a command does live in TimerEngine, not here.
// That split matters for later: when the Twitch EventSub piece exists,
// it'll call straight into TimerEngine the same way this does, rather
// than needing its own copy of the command logic.
public static class RequestRouter
{
    // One lock per overlay, not a single shared lock for both. Bar and
    // radial commands never touch each other's files, so there's no
    // reason to make a bar command wait behind a radial one, they're
    // genuinely independent. Found the need for these during a full
    // review, not from it actually breaking, WebServerHost fires
    // requests off without waiting for the previous one to finish (on
    // purpose, so a slow request can't block the 5x/sec polling behind
    // it), which means two requests for the same overlay really can
    // run at the same moment. Without a lock, that's a genuine race on
    // the load, tick, mutate, save sequence below, whichever one saves
    // last silently wins and the other's change is just lost.
    private static readonly object BarLock = new();
    private static readonly object RadialLock = new();

    public static async Task HandleRequest(HttpListenerContext ctx)
    {
        HttpListenerRequest req = ctx.Request;
        HttpListenerResponse res = ctx.Response;
        string path = req.Url?.AbsolutePath ?? "/";

        try
        {
            // Wide open CORS and no caching. This only ever binds to
            // localhost, so I'm not worried about exposing it to
            // randoms, and OBS needs fresh state on every single poll
            // anyway, caching would just show stale numbers.
            res.AddHeader("Access-Control-Allow-Origin", "*");
            res.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate");

            if (path is "/" or "")
            {
                await SendText(res, 200, "text/html; charset=utf-8", IndexHtml());
                return;
            }

            if (path.StartsWith("/bar/api", StringComparison.OrdinalIgnoreCase))
            {
                string result = HandleBarCommand(req.QueryString["cmd"] ?? "", req.QueryString);
                await SendText(res, 200, "application/json; charset=utf-8", result);
                return;
            }

            if (path.StartsWith("/radial/api", StringComparison.OrdinalIgnoreCase))
            {
                string result = HandleRadialCommand(req.QueryString["cmd"] ?? "", req.QueryString);
                await SendText(res, 200, "application/json; charset=utf-8", result);
                return;
            }

            if (path is "/bar" or "/bar/")
            {
                await SendEmbedded(res, "bar.html");
                return;
            }

            if (path is "/radial" or "/radial/")
            {
                await SendEmbedded(res, "radial.html");
                return;
            }

            await SendText(res, 404, "text/plain", $"404 Not Found: {path}");
        }
        catch (Exception ex)
        {
            Logger.Log("[ERROR]", ex.ToString());
            try { await SendText(res, 500, "text/plain", ex.Message); }
            catch
            {
                // The connection's probably already gone at this point,
                // nothing useful I can do about that.
            }
        }
    }

    // ------------------------------------------------------------
    // Bar command handling
    // ------------------------------------------------------------
    private static string HandleBarCommand(string cmd, System.Collections.Specialized.NameValueCollection qs)
    {
        // Everything from load through save happens inside the lock,
        // not just the save. Locking only the save wouldn't help, two
        // threads could still both load the same "before" state, both
        // mutate their own copy, and whichever saves second still wins.
        // The whole read-modify-write sequence needs to be one atomic
        // unit.
        lock (BarLock)
        {
            BarState state = JsonStore.Load<BarState>(Paths.BarFile) ?? new BarState();

            TimerEngine.Tick(state);

            bool handled = TimerEngine.HandleCommon(cmd, qs, state, out string? error);
            if (!handled)
                TimerEngine.HandleBarSpecific(cmd, qs, state, out error);

            JsonStore.Save(state, Paths.BarFile);

            if (cmd is not ("status" or ""))
                Logger.Log("[BAR]", error != null ? $"FAILED {cmd}: {error}" : cmd);

            // I'm serialising directly here rather than through a shared
            // helper. state is a genuine BarState at this exact point,
            // and the JSON serialiser decides what fields to include
            // based on the declared type of the variable it's handed,
            // not what the object actually is underneath. A shared
            // helper typed as the base OverlayState silently dropped
            // barHeight/barWidth from the response, which is exactly
            // what made the bar collapse to zero height on the overlay
            // page. Learned that one the hard way.
            if (error != null)
                return System.Text.Json.JsonSerializer.Serialize(new { ok = false, error });
            return System.Text.Json.JsonSerializer.Serialize(new { ok = true, cmd, state });
        }
    }

    // ------------------------------------------------------------
    // Radial command handling
    // ------------------------------------------------------------
    private static string HandleRadialCommand(string cmd, System.Collections.Specialized.NameValueCollection qs)
    {
        lock (RadialLock)
        {
            RadialState state = JsonStore.Load<RadialState>(Paths.RadialFile) ?? new RadialState();

            // Clamping these here as a safety net, in case an old or
            // hand edited config file has a value outside the range
            // the overlay page actually expects. I'd rather quietly
            // pull it back into range than have the ring render at
            // some broken size.
            state.Size = Math.Clamp(state.Size, 5, 100);
            state.Thickness = Math.Clamp(state.Thickness, 1, 50);

            TimerEngine.Tick(state);

            bool handled = TimerEngine.HandleCommon(cmd, qs, state, out string? error);
            if (!handled)
                TimerEngine.HandleRadialSpecific(cmd, qs, state, out error);

            JsonStore.Save(state, Paths.RadialFile);

            if (cmd is not ("status" or ""))
                Logger.Log("[RADIAL]", error != null ? $"FAILED {cmd}: {error}" : cmd);

            if (error != null)
                return System.Text.Json.JsonSerializer.Serialize(new { ok = false, error });
            return System.Text.Json.JsonSerializer.Serialize(new { ok = true, cmd, state });
        }
    }

    // ------------------------------------------------------------
    // Response helpers
    // ------------------------------------------------------------
    private static async Task SendText(HttpListenerResponse res, int statusCode, string mimeType, string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        res.StatusCode = statusCode;
        res.ContentType = mimeType;
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    // The overlay pages are baked into the exe as embedded resources
    // (see the csproj), so there's nothing on disk a streamer could
    // accidentally move or delete. The resource name is the project's
    // root namespace plus the folder path with dots instead of
    // slashes, that's just a .NET convention, not something I chose.
    private static async Task SendEmbedded(HttpListenerResponse res, string fileName)
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        string resourceName = $"AdBreakTimerGUI.Web.{fileName}";
        await using Stream? stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            await SendText(res, 500, "text/plain", $"Embedded resource not found: {resourceName}. This means the exe wasn't built correctly.");
            return;
        }
        using var reader = new StreamReader(stream);
        string html = await reader.ReadToEndAsync();
        await SendText(res, 200, "text/html; charset=utf-8", html);
    }

    // A small landing page for anyone who hits the base URL in a
    // browser rather than pointing OBS at the actual overlay paths.
    private static string IndexHtml() => """
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><title>Ad Break Timer</title>
        <style>body{font-family:sans-serif;background:#111;color:#eee;padding:2rem;}</style>
        </head><body>
        <h2>Ad Break Timer is running</h2>
        <p>Add these as OBS Browser Sources:</p>
        <ul><li>/bar/</li><li>/radial/</li></ul>
        </body></html>
        """;
}