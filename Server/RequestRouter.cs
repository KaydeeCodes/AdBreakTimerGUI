using System.Net;
using System.Reflection;
using System.Text;
using AdBreakTimerGUI.Config;

namespace AdBreakTimerGUI.Server;

public static class RequestRouter
{
    public static async Task HandleRequest(HttpListenerContext ctx)
    {
        HttpListenerRequest req = ctx.Request;
        HttpListenerResponse res = ctx.Response;
        string path = req.Url?.AbsolutePath ?? "/";

        try
        {
            // Only ever binds to localhost, so wide open CORS is fine, and OBS needs fresh state every poll, so no caching.
            res.AddHeader("Access-Control-Allow-Origin", "*");
            res.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate");

            if (path is "/" or "")
            {
                await SendText(res, 200, "text/html; charset=utf-8", IndexHtml());
                return;
            }

            // Exact match, not StartsWith, AbsolutePath never includes the query string so a prefix match here was doing nothing useful.
            if (path.Equals("/bar/api", StringComparison.OrdinalIgnoreCase))
            {
                var result = OverlayCommandExecutor.ExecuteBar(req.QueryString["cmd"] ?? "", req.QueryString);
                await SendText(res, 200, "application/json; charset=utf-8", result.Json);
                return;
            }

            if (path.Equals("/radial/api", StringComparison.OrdinalIgnoreCase))
            {
                var result = OverlayCommandExecutor.ExecuteRadial(req.QueryString["cmd"] ?? "", req.QueryString);
                await SendText(res, 200, "application/json; charset=utf-8", result.Json);
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

            // Browsers request this automatically on every page load, serving it properly means that harmless request stops 404ing (and wrongly flipping the traffic light amber for it), rather than treating a normal browser behaviour as an application error.
            if (path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase))
            {
                await SendFavicon(res);
                return;
            }

            // Logged, but not as [ERROR], a wrong path can still mean a typo'd OBS Browser Source URL worth spotting, but a plain 404 isn't itself an application fault, it shouldn't flip the traffic light amber the way a genuine error should.
            Logger.Log("[HTTP]", $"404 Not Found: {path}");
            await SendText(res, 404, "text/plain", $"404 Not Found: {path}");
        }
        catch (Exception ex)
        {
            Logger.Log("[ERROR]", ex.ToString());
            try { await SendText(res, 500, "text/plain", ex.Message); }
            catch { }
        }
    }

    private static async Task SendText(HttpListenerResponse res, int statusCode, string mimeType, string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        res.StatusCode = statusCode;
        res.ContentType = mimeType;
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    // Overlay pages are embedded resources, nothing on disk to move or delete. Resource name is the root namespace plus folder path with dots instead of slashes.
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

    // Same app icon already embedded for ApplicationIcon in the csproj, read back out and served raw rather than keeping a second copy anywhere.
    private static async Task SendFavicon(HttpListenerResponse res)
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        await using Stream? stream = asm.GetManifestResourceStream("AdBreakTimerGUI.Assets.app.ico");
        if (stream == null)
        {
            // Not worth failing loudly over, a missing favicon is genuinely harmless, this just means the exe was built without the icon embedded for some reason.
            res.StatusCode = 404;
            res.Close();
            return;
        }

        res.StatusCode = 200;
        res.ContentType = "image/x-icon";
        res.ContentLength64 = stream.Length;
        await stream.CopyToAsync(res.OutputStream);
        res.Close();
    }

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