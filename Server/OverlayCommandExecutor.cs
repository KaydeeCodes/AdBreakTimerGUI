using System.Collections.Specialized;
using AdBreakTimerGUI.Config;
using AdBreakTimerGUI.Engine;

namespace AdBreakTimerGUI.Server;

// The one place that actually executes a bar/radial command, load-tick-mutate-save, all inside a lock. RequestRouter and TwitchAdSequencer both go through this so they can't race each other.
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

            // Serialised directly against the real BarState here, not a base-typed helper, that silently dropped barHeight/barWidth once already.
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

    // Updates only whatever fields the mutator touches, against the current live state, not a stale copy, same lock as Execute* so this can't race a command either.
    public static void UpdateBarAppearance(Action<BarState> mutate)
    {
        lock (BarLock)
        {
            BarState state = JsonStore.Load<BarState>(Paths.BarFile) ?? new BarState();

            // Snapshot before mutating, so the log can say exactly what changed rather than just "something changed".
            string oldDirection = state.Direction;
            int oldHeight = state.BarHeight;
            string oldWidth = state.BarWidth;

            mutate(state);
            JsonStore.Save(state, Paths.BarFile);

            var changes = new List<string>();
            if (oldDirection != state.Direction) changes.Add($"direction {oldDirection}->{state.Direction}");
            if (oldHeight != state.BarHeight) changes.Add($"height {oldHeight}px->{state.BarHeight}px");
            if (oldWidth != state.BarWidth) changes.Add($"width {oldWidth}->{state.BarWidth}");

            Logger.Log("[BAR]", changes.Count > 0 ? $"Appearance updated: {string.Join(", ", changes)}" : "Appearance settings saved, no actual changes.");
        }
    }

    public static void UpdateRadialAppearance(Action<RadialState> mutate)
    {
        lock (RadialLock)
        {
            RadialState state = JsonStore.Load<RadialState>(Paths.RadialFile) ?? new RadialState();

            string oldDirection = state.Direction;
            int oldSize = state.Size;
            int oldThickness = state.Thickness;
            string oldTrackColor = state.TrackColor;
            int oldRotation = state.RotationDegrees;

            mutate(state);
            JsonStore.Save(state, Paths.RadialFile);

            var changes = new List<string>();
            if (oldDirection != state.Direction) changes.Add($"direction {oldDirection}->{state.Direction}");
            if (oldSize != state.Size) changes.Add($"size {oldSize}%->{state.Size}%");
            if (oldThickness != state.Thickness) changes.Add($"thickness {oldThickness}%->{state.Thickness}%");
            if (oldTrackColor != state.TrackColor) changes.Add($"trackColor {oldTrackColor}->{state.TrackColor}");
            if (oldRotation != state.RotationDegrees) changes.Add($"rotation {oldRotation}deg->{state.RotationDegrees}deg");

            Logger.Log("[RADIAL]", changes.Count > 0 ? $"Appearance updated: {string.Join(", ", changes)}" : "Appearance settings saved, no actual changes.");
        }
    }
}