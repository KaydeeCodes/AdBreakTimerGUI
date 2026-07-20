using System.Net;
using AdBreakTimerGUI.Config;

namespace AdBreakTimerGUI.Server;

// The bit that owns the actual HttpListener and its background task.
// Unlike the console version, which just started this once and ran
// until the window closed, this needs a proper Start/Stop I can call
// from the GUI's button whenever I like, so I'm wrapping it up as its
// own class with state rather than leaving it as loose top level code.
public class WebServerHost
{
    // I raise this whenever the server's state changes, so the GUI can
    // update the traffic light and the tray icon without me having to
    // poll anything. Whoever's listening (MainForm) needs to remember
    // this fires on a background thread, not the UI thread, so any
    // control update on the other end needs an Invoke.
    public event Action<bool>? StatusChanged; // true = running, false = stopped

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenLoopTask;

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }

    // Tries the saved port first, and if that's already taken by
    // something else, walks upward one at a time until it finds a free
    // one. Whichever port actually ends up bound gets saved back to
    // settings so next launch remembers it.
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

        _listener = listener;
        Port = port;
        _cts = new CancellationTokenSource();
        IsRunning = true;

        Logger.Log("[SERVER]", $"Listening on http://localhost:{port}/");
        StatusChanged?.Invoke(true);

        // I run the accept loop as a background task rather than
        // blocking whatever thread called Start(). On the GUI thread
        // that would freeze the window, and I want Start() to return
        // straight away so the button click handler isn't hanging
        // around waiting for something that's meant to run forever.
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
                // This is the expected way the loop ends. Stop() below
                // calls listener.Stop(), which makes GetContextAsync
                // throw straight away. I'm catching it here specifically
                // because the cancellation token is already set, so I
                // know this is a deliberate shutdown and not a real
                // error worth logging.
                break;
            }
            catch (Exception ex)
            {
                // A genuine unexpected error, not a shutdown. I log it
                // and keep the loop going rather than letting one bad
                // request take the whole server down.
                Logger.Log("[ERROR]", ex.Message);
                continue;
            }

            // I don't await this. Firing requests off one after another
            // and awaiting each one in turn would mean a slow request
            // blocks every other one behind it, and since the overlay
            // pages poll five times a second, that adds up fast.
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