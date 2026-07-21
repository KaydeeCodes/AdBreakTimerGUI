using System.Net;
using AdBreakTimerGUI.Config;

namespace AdBreakTimerGUI.Server;

// Owns the HttpListener and its background task, with a proper Start/Stop I can call from the GUI's button.
public class WebServerHost
{
    public event Action<bool>? StatusChanged; // true = running, false = stopped, fires on a background thread

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenLoopTask;

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }

    // Tries the saved port, walks upward if it's taken, saves whichever one actually worked.
    public void Start(int preferredPort)
    {
        if (IsRunning) return;

        int port = preferredPort;
        HttpListener? listener = null;

        for (int attempt = 0; attempt < 50; attempt++)
        {
            listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                listener.Start();
                break;
            }
            catch (HttpListenerException)
            {
                listener.Close();
                listener = null;
                port++;
            }
        }

        if (listener == null)
            throw new Exception("Could not find a free port after 50 attempts. That's almost certainly not actually a port problem, something else is wrong.");

        if (port != preferredPort)
            Logger.Log("[SERVER]", $"Port {preferredPort} was taken, using {port} instead.");

        _listener = listener;
        Port = port;
        _cts = new CancellationTokenSource();
        IsRunning = true;

        Logger.Log("[SERVER]", $"Listening on http://localhost:{port}/");
        StatusChanged?.Invoke(true);

        // Background task, not awaited, so Start() returns immediately rather than blocking the button click that called it.
        _listenLoopTask = Task.Run(() => ListenLoopAsync(_listener, _cts.Token));
    }

    private async Task ListenLoopAsync(HttpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync();
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                // Expected shutdown path, Stop() below calls listener.Stop() which makes this throw on purpose.
                break;
            }
            catch (Exception ex)
            {
                // A genuine unexpected error, not a shutdown. Logged and the loop keeps going, but with a short delay first, without it a persistent error here would spin as a tight CPU-burning loop instead of backing off.
                Logger.Log("[ERROR]", ex.Message);
                try { await Task.Delay(500, token); } catch (OperationCanceledException) { break; }
                continue;
            }

            // Not awaited, a slow request awaited in sequence would block every poll behind it, and the overlays poll 5x/sec.
            _ = RequestRouter.HandleRequest(ctx);
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;

        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();

        IsRunning = false;
        Logger.Log("[SERVER]", "Stopped.");
        StatusChanged?.Invoke(false);
    }
}