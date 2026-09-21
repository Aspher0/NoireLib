using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Coach marks: the screen dims, one widget stays lit, and a card explains it.<br/>
/// Widgets are targeted by the key they are marked with while they draw. Draw thread only.
/// </summary>
public static class NoireTour
{
    private static readonly Dictionary<string, MarkedTarget> Targets = new(StringComparer.Ordinal);

    private static string tooltip = string.Empty;

    private static int tooltipFrame;

    /// <summary>
    /// The surface that draws the running tour, registered the first time it is asked for.
    /// </summary>
    public static NoireTourHost Host => NoireTourHost.Instance;

    /// <summary>
    /// Whether a tour is running.
    /// </summary>
    public static bool IsRunning => ActiveRun != null;

    /// <summary>
    /// The identifier of the running tour, or <see langword="null"/> when none is running.
    /// </summary>
    public static string? Id => ActiveRun?.Id;

    /// <summary>
    /// The step the running tour is on, or <see langword="null"/> when none is running.
    /// </summary>
    public static TourStep? Current => ActiveRun?.CurrentStep;

    /// <summary>
    /// The index of the step the running tour is on, or -1 when none is running.
    /// </summary>
    public static int StepIndex => ActiveRun?.Index ?? -1;

    /// <summary>
    /// How many steps the running tour holds, zero when none is running.
    /// </summary>
    public static int StepCount => ActiveRun?.Steps.Count ?? 0;

    internal static TourRun? ActiveRun { get; private set; }

    /// <summary>Starts building a tour. It runs once <see cref="TourBuilder.Start"/> is called.</summary>
    /// <param name="id">The tour's id, reported by <see cref="Id"/>.</param>
    /// <returns>The builder to add steps to.</returns>
    public static TourBuilder Create(string id) => new(id);

    /// <summary>
    /// Runs a tour, replacing the one already running.
    /// </summary>
    /// <param name="id">An identifier of the tour, reported by <see cref="Id"/>.</param>
    /// <param name="steps">The steps, in the order they are shown.</param>
    /// <param name="options">How the tour looks and behaves, the defaults being used when <see langword="null"/>.</param>
    public static void Start(string id, IReadOnlyList<TourStep> steps, TourOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(steps);

        Stop();

        if (steps.Count == 0)
            return;

        var run = new TourRun(id, steps, options ?? new TourOptions());

        if (NoireService.IsInitialized())
            _ = Host;
        ActiveRun = run;

        if (IsSkipped(run.CurrentStep))
        {
            MoveTo(run, 1);
            return;
        }

        run.Options.OnStepChanged?.Invoke(run.Index);
    }

    /// <summary>
    /// Ends the running tour where it stands, raising <see cref="TourOptions.OnStopped"/>.
    /// </summary>
    public static void Stop()
    {
        var run = ActiveRun;

        if (run == null)
            return;

        ActiveRun = null;
        Leave(run);
        run.Options.OnStopped?.Invoke();
    }

    /// <summary>Moves the running tour to the next step, ending it past the last one.</summary>
    /// <returns>True when a step follows.</returns>
    public static bool Next()
    {
        var run = ActiveRun;
        return run != null && MoveTo(run, 1);
    }

    /// <summary>Moves the running tour back one step.</summary>
    /// <returns>True when a step precedes.</returns>
    public static bool Previous()
    {
        var run = ActiveRun;
        return run != null && MoveTo(run, -1);
    }

    /// <summary>
    /// Marks the widget that was drawn last as the one a step points at, along with the area its window clips it to.
    /// </summary>
    /// <param name="target">The key the step names in <see cref="TourStep.Target"/>.</param>
    public static void Mark(string target)
    {
        if (!UiDraw.Available)
            return;

        Mark(target, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
    }

    /// <summary>
    /// Marks a screen rectangle as the one a step points at, clipped to the window it is drawn in.
    /// </summary>
    /// <param name="target">The key the step names in <see cref="TourStep.Target"/>.</param>
    /// <param name="min">The top left corner, in screen coordinates.</param>
    /// <param name="max">The bottom right corner, in screen coordinates.</param>
    public static void Mark(string target, Vector2 min, Vector2 max)
        => Mark(target, min, max, CurrentClip());

    /// <summary>
    /// Marks a screen rectangle as the one a step points at, with the area it is visible in.
    /// </summary>
    /// <param name="target">The key the step names in <see cref="TourStep.Target"/>.</param>
    /// <param name="min">The top left corner, in screen coordinates.</param>
    /// <param name="max">The bottom right corner, in screen coordinates.</param>
    /// <param name="clip">The rectangle the widget is visible in, min in xy and max in zw.</param>
    public static void Mark(string target, Vector2 min, Vector2 max, Vector4 clip)
    {
        if (string.IsNullOrEmpty(target))
            return;

        Targets[target] = new MarkedTarget(new Vector4(min.X, min.Y, max.X, max.Y), clip, CurrentWindow(), NoireUI.FrameCount);
    }

    /// <summary>Whether a widget may be used. While a tour runs, only the step's target and <see cref="TourStep.AlsoInteractive"/> may.</summary>
    /// <param name="target">The key the widget is marked with.</param>
    /// <returns>True when the widget may be used.</returns>
    public static bool IsInteractive(string target)
    {
        var run = ActiveRun;

        if (run == null || !run.Options.BlockOtherWidgets)
            return true;

        var step = run.CurrentStep;

        if (step == null)
            return true;

        if (string.Equals(step.Target, target, StringComparison.Ordinal))
            return true;

        var allowed = step.AlsoInteractive;

        if (allowed == null)
            return false;

        foreach (var key in allowed)
        {
            if (string.Equals(key, target, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Reads where a marked widget was drawn this frame.</summary>
    /// <param name="target">The key the widget is marked with.</param>
    /// <param name="rect">The screen rectangle, min in xy and max in zw.</param>
    /// <returns>True when the widget was marked on this frame or the one before.</returns>
    public static bool TryGetTarget(string target, out Vector4 rect)
        => TryGetTarget(target, out rect, out _);

    /// <summary>Reads where a marked widget was drawn this frame and the area its window clips it to.</summary>
    /// <param name="target">The key the widget is marked with.</param>
    /// <param name="rect">The screen rectangle, min in xy and max in zw.</param>
    /// <param name="clip">The visible rectangle, min in xy and max in zw.</param>
    /// <returns>True when the widget was marked on this frame or the one before.</returns>
    public static bool TryGetTarget(string target, out Vector4 rect, out Vector4 clip)
    {
        rect = default;
        clip = default;

        if (string.IsNullOrEmpty(target) || !Targets.TryGetValue(target, out var marked))
            return false;

        if (NoireUI.FrameCount - marked.Frame > 1)
            return false;

        rect = marked.Rect;
        clip = marked.Clip;
        return true;
    }

    /// <summary>
    /// Forgets every marked widget.
    /// </summary>
    public static void ClearTargets() => Targets.Clear();

    /// <summary>
    /// Shows a tooltip, drawn above the dimmed screen while a tour runs and by ImGui itself otherwise.
    /// </summary>
    /// <param name="text">The text of the tooltip.</param>
    public static void Tooltip(string text)
    {
        if (!IsRunning)
        {
            if (UiDraw.Available)
                ImGui.SetTooltip(text);

            return;
        }

        tooltip = text;
        tooltipFrame = NoireUI.FrameCount;
    }

    internal static bool TryTakeTooltip(out string text)
    {
        text = tooltip;
        tooltip = string.Empty;

        return text.Length > 0 && NoireUI.FrameCount - tooltipFrame <= 1;
    }

    /// <summary>
    /// Draws the running tour, for a plugin that draws its surfaces itself.
    /// </summary>
    public static void Draw() => Host.Draw();

    internal static bool TryResolveTarget(TourStep step, out Vector4 rect, out Vector4 clip)
    {
        clip = default;

        if (step.Rect != null)
        {
            rect = step.Rect();
            return rect.Z > rect.X && rect.W > rect.Y;
        }

        return TryGetTarget(step.Target, out rect, out clip);
    }

    internal static unsafe void KeepTargetWindowInFront(TourStep step)
    {
        if (!UiDraw.Available || step.Target.Length == 0 || !Targets.TryGetValue(step.Target, out var marked))
            return;

        if (marked.Window == 0 || NoireUI.FrameCount - marked.Frame > 1)
            return;

        ImGuiP.BringWindowToDisplayFront(new ImGuiWindowPtr((ImGuiWindow*)marked.Window));
    }

    private static unsafe nint CurrentWindow()
    {
        if (!UiDraw.Available)
            return 0;

        var window = ImGuiP.GetCurrentWindow();
        return window.IsNull ? 0 : (nint)window.RootWindow.Handle;
    }

    private static Vector4 CurrentClip()
    {
        if (!UiDraw.Available)
            return default;

        using var draw = UiDraw.BeginWindow();
        var list = draw.List;

        if (list.IsNull)
            return default;

        var min = list.GetClipRectMin();
        var max = list.GetClipRectMax();

        return new Vector4(min.X, min.Y, max.X, max.Y);
    }

    internal static void Complete()
    {
        var run = ActiveRun;

        if (run == null)
            return;

        ActiveRun = null;
        Leave(run);
        run.Options.OnCompleted?.Invoke();
    }

    private static bool MoveTo(TourRun run, int direction)
    {
        var index = run.Index + direction;

        while (index >= 0 && index < run.Steps.Count && IsSkipped(run.Steps[index]))
            index += direction;

        if (index >= run.Steps.Count)
        {
            run.Index = run.Steps.Count - 1;
            Complete();
            return false;
        }

        if (index < 0 || index == run.Index)
            return false;

        Leave(run);
        run.Index = index;
        run.AdvanceAtFrame = 0;
        run.Settling = false;
        run.SettleFraction = 0f;
        run.Options.OnStepChanged?.Invoke(index);
        return true;
    }

    private static void Leave(TourRun run)
    {
        if (!run.Entered)
            return;

        run.Entered = false;
        run.CurrentStep?.OnLeave?.Invoke();
    }

    private static bool IsSkipped(TourStep? step) => step?.IsSkipped?.Invoke() == true;

    private readonly record struct MarkedTarget(Vector4 Rect, Vector4 Clip, nint Window, int Frame);
}
