using System.Collections.Specialized;
using AdBreakTimerGUI.Config;
using AdBreakTimerGUI.Engine;

namespace AdBreakTimerGUI.Server;

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
            LogCommand("[BAR]", cmd, error, state);

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
            LogCommand("[RADIAL]", cmd, error, state);

            string json = error != null
                ? System.Text.Json.JsonSerializer.Serialize(new { ok = false, error })
                : System.Text.Json.JsonSerializer.Serialize(new { ok = true, cmd, state });

            return new CommandResult(error is null, error, json);
        }
    }

    private static void LogCommand(string tag, string cmd, string? error, OverlayState state)
    {
        if (cmd is "status" or "") return;

        string detail = error != null
            ? $"FAILED {cmd}: {error}"
            : $"{cmd} (status={state.Status}, color={state.Color}, preferredColor={state.PreferredColor}, finishColor={state.FinishColor}, remaining={TimeParsing.SecsToHms(state.Remaining)})";

        Logger.Log(tag, detail);
    }

    public static void UpdateBarAppearance(Action<BarState> mutate)
    {
        lock (BarLock)
        {
            BarState state = JsonStore.Load<BarState>(Paths.BarFile) ?? new BarState();

            string oldDirection = state.Direction;
            int oldHeight = state.BarHeight;
            string oldWidth = state.BarWidth;
            string oldLiveColor = state.Color;
            string oldPreferredColor = state.PreferredColor;
            string oldFinishColor = state.FinishColor;
            string oldBgColor = state.BgColor;
            bool oldFlash = state.FlashOnFinish;
            int oldFlashDuration = state.FlashDuration;

            mutate(state);

            // If the bar was already showing the running colour (not an active ad's finish colour override), pushing the new preference live is safe, this is what makes Save feel instant rather than "changed, but nothing visibly happened until some later countdown". If it wasn't showing the running colour, that almost certainly means an ad's mid-flight right now, so I deliberately leave the live display alone and let the new preference take effect on the next real transition instead of interrupting it.
            bool wasShowingRunningColor = oldLiveColor == oldPreferredColor;
            bool applyLiveNow = wasShowingRunningColor && state.PreferredColor != oldPreferredColor;
            if (applyLiveNow)
                state.Color = state.PreferredColor;

            JsonStore.Save(state, Paths.BarFile);

            var changes = new List<string>();
            if (oldDirection != state.Direction) changes.Add($"direction {oldDirection}->{state.Direction}");
            if (oldHeight != state.BarHeight) changes.Add($"height {oldHeight}px->{state.BarHeight}px");
            if (oldWidth != state.BarWidth) changes.Add($"width {oldWidth}->{state.BarWidth}");
            if (oldPreferredColor != state.PreferredColor) changes.Add($"preferredColor {oldPreferredColor}->{state.PreferredColor}{(applyLiveNow ? " (applied live immediately)" : " (will apply next countdown, an ad's likely showing right now)")}");
            if (oldFinishColor != state.FinishColor) changes.Add($"finishColor {oldFinishColor}->{state.FinishColor}");
            if (oldBgColor != state.BgColor) changes.Add($"bgColor {oldBgColor}->{state.BgColor}");
            if (oldFlash != state.FlashOnFinish) changes.Add($"flashOnFinish {oldFlash}->{state.FlashOnFinish}");
            if (oldFlashDuration != state.FlashDuration) changes.Add($"flashDuration {oldFlashDuration}s->{state.FlashDuration}s");

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
            string oldLiveColor = state.Color;
            string oldPreferredColor = state.PreferredColor;
            string oldFinishColor = state.FinishColor;
            string oldBgColor = state.BgColor;
            bool oldFlash = state.FlashOnFinish;
            int oldFlashDuration = state.FlashDuration;

            mutate(state);

            bool wasShowingRunningColor = oldLiveColor == oldPreferredColor;
            bool applyLiveNow = wasShowingRunningColor && state.PreferredColor != oldPreferredColor;
            if (applyLiveNow)
                state.Color = state.PreferredColor;

            JsonStore.Save(state, Paths.RadialFile);

            var changes = new List<string>();
            if (oldDirection != state.Direction) changes.Add($"direction {oldDirection}->{state.Direction}");
            if (oldSize != state.Size) changes.Add($"size {oldSize}%->{state.Size}%");
            if (oldThickness != state.Thickness) changes.Add($"thickness {oldThickness}%->{state.Thickness}%");
            if (oldTrackColor != state.TrackColor) changes.Add($"trackColor {oldTrackColor}->{state.TrackColor}");
            if (oldRotation != state.RotationDegrees) changes.Add($"rotation {oldRotation}deg->{state.RotationDegrees}deg");
            if (oldPreferredColor != state.PreferredColor) changes.Add($"preferredColor {oldPreferredColor}->{state.PreferredColor}{(applyLiveNow ? " (applied live immediately)" : " (will apply next countdown, an ad's likely showing right now)")}");
            if (oldFinishColor != state.FinishColor) changes.Add($"finishColor {oldFinishColor}->{state.FinishColor}");
            if (oldBgColor != state.BgColor) changes.Add($"bgColor {oldBgColor}->{state.BgColor}");
            if (oldFlash != state.FlashOnFinish) changes.Add($"flashOnFinish {oldFlash}->{state.FlashOnFinish}");
            if (oldFlashDuration != state.FlashDuration) changes.Add($"flashDuration {oldFlashDuration}s->{state.FlashDuration}s");

            Logger.Log("[RADIAL]", changes.Count > 0 ? $"Appearance updated: {string.Join(", ", changes)}" : "Appearance settings saved, no actual changes.");
        }
    }

    public static (string color, string finishColor) GetBarColors()
    {
        lock (BarLock)
        {
            BarState state = JsonStore.Load<BarState>(Paths.BarFile) ?? new BarState();
            return (state.PreferredColor, state.FinishColor);
        }
    }

    public static (string color, string finishColor) GetRadialColors()
    {
        lock (RadialLock)
        {
            RadialState state = JsonStore.Load<RadialState>(Paths.RadialFile) ?? new RadialState();
            return (state.PreferredColor, state.FinishColor);
        }
    }
}