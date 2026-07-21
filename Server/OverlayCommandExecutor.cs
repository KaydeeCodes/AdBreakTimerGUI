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
            : $"{cmd} (status={state.Status}, color={state.Color}, preferredColor={state.PreferredColor}, adColor={state.AdColor}, finishColor={state.FinishColor}, finishStyle={state.FinishStyle}, remaining={TimeParsing.SecsToHms(state.Remaining)})";

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
            string oldAdColor = state.AdColor;
            string oldFinishColor = state.FinishColor;
            string oldBgColor = state.BgColor;
            string oldFinishStyle = state.FinishStyle;
            int oldFlashDuration = state.FlashDuration;

            mutate(state);

            // Only push the new ad-free colour live if the bar was already showing it (not an active ad's colour), an ad's likely mid-flight otherwise, so the new preference takes effect on the next real transition instead of interrupting it.
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
            if (oldAdColor != state.AdColor) changes.Add($"adColor {oldAdColor}->{state.AdColor}");
            if (oldFinishColor != state.FinishColor) changes.Add($"finishColor {oldFinishColor}->{state.FinishColor}");
            if (oldBgColor != state.BgColor) changes.Add($"bgColor {oldBgColor}->{state.BgColor}");
            if (oldFinishStyle != state.FinishStyle) changes.Add($"finishStyle {oldFinishStyle}->{state.FinishStyle}");
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
            string oldAdColor = state.AdColor;
            string oldFinishColor = state.FinishColor;
            string oldBgColor = state.BgColor;
            string oldFinishStyle = state.FinishStyle;
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
            if (oldAdColor != state.AdColor) changes.Add($"adColor {oldAdColor}->{state.AdColor}");
            if (oldFinishColor != state.FinishColor) changes.Add($"finishColor {oldFinishColor}->{state.FinishColor}");
            if (oldBgColor != state.BgColor) changes.Add($"bgColor {oldBgColor}->{state.BgColor}");
            if (oldFinishStyle != state.FinishStyle) changes.Add($"finishStyle {oldFinishStyle}->{state.FinishStyle}");
            if (oldFlashDuration != state.FlashDuration) changes.Add($"flashDuration {oldFlashDuration}s->{state.FlashDuration}s");

            Logger.Log("[RADIAL]", changes.Count > 0 ? $"Appearance updated: {string.Join(", ", changes)}" : "Appearance settings saved, no actual changes.");
        }
    }

    // Returns all three now, ad-free colour, ad colour, and finish colour, instead of just two.
    public static (string adFreeColor, string adColor, string finishColor) GetBarColors()
    {
        lock (BarLock)
        {
            BarState state = JsonStore.Load<BarState>(Paths.BarFile) ?? new BarState();
            return (state.PreferredColor, state.AdColor, state.FinishColor);
        }
    }

    public static (string adFreeColor, string adColor, string finishColor) GetRadialColors()
    {
        lock (RadialLock)
        {
            RadialState state = JsonStore.Load<RadialState>(Paths.RadialFile) ?? new RadialState();
            return (state.PreferredColor, state.AdColor, state.FinishColor);
        }
    }
}