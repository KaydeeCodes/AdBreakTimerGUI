using System.Collections.Specialized;
using AdBreakTimerGUI.Config;
using AdBreakTimerGUI.Engine;

namespace AdBreakTimerGUI.Server;

// The one place that actually executes a bar or radial command,
// load, tick, mutate, save, all inside a lock. Both RequestRouter
// (HTTP requests from OBS/Streamer.bot) and TwitchAdSequencer
// (automatic commands from EventSub) go through this, rather than
// either one touching bar.json/radial.json directly, that's what
// keeps the two from racing each other.
public static class OverlayCommandExecutor
{
    public record CommandResult(bool Ok, string? Error, string Json);

    private static readonly object BarLock = new();
    private static readonly object RadialLock = new();

    public static CommandResult ExecuteBar(string cmd, NameValueCollection qs)
    {
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

            // Serialising directly here rather than through a shared
            // helper typed as the base OverlayState, state is a
            // genuine BarState at this point, and the JSON serialiser
            // only includes fields visible on the declared type. A
            // base-typed helper silently dropped barHeight/barWidth
            // once already, learned that one the hard way.
            string json = error != null
                ? System.Text.Json.JsonSerializer.Serialize(new { ok = false, error })
                : System.Text.Json.JsonSerializer.Serialize(new { ok = true, cmd, state });

            return new CommandResult(error is null, error, json);
        }
    }

    public static CommandResult ExecuteRadial(string cmd, NameValueCollection qs)
    {
        lock (RadialLock)
        {
            RadialState state = JsonStore.Load<RadialState>(Paths.RadialFile) ?? new RadialState();

            state.Size = Math.Clamp(state.Size, 5, 100);
            state.Thickness = Math.Clamp(state.Thickness, 1, 50);

            TimerEngine.Tick(state);

            bool handled = TimerEngine.HandleCommon(cmd, qs, state, out string? error);
            if (!handled)
                TimerEngine.HandleRadialSpecific(cmd, qs, state, out error);

            JsonStore.Save(state, Paths.RadialFile);

            if (cmd is not ("status" or ""))
                Logger.Log("[RADIAL]", error != null ? $"FAILED {cmd}: {error}" : cmd);

            string json = error != null
                ? System.Text.Json.JsonSerializer.Serialize(new { ok = false, error })
                : System.Text.Json.JsonSerializer.Serialize(new { ok = true, cmd, state });

            return new CommandResult(error is null, error, json);
        }
    }
}