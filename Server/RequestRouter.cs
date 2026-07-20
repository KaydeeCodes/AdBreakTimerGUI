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
            res.AddHeader("Access-Control-Allow-Origin", "*");
            res.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate");

            if (path is "/" or "")
            {
                await SendText(res, 200, "text/html; charset=utf-8", IndexHtml());
                return;
            }

            if (path.StartsWith("/bar/api", StringComparison.OrdinalIgnoreCase))
            {
                var result = OverlayCommandExecutor.ExecuteBar(req.QueryString["cmd"] ?? "", req.QueryString);
                await SendText(res, 200, "application/json; charset=utf-8", result.Json);
                return;
            }

            if (path.StartsWith("/radial/api", StringComparison.OrdinalIgnoreCase))
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