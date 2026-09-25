using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Opens and closes effect scopes: everything drawn inside takes the <see cref="NoireGradient"/> and
/// <see cref="NoireMotion"/> effects, applied when the scope closes. The layout never moves. Scopes nest.
/// </summary>
public static class NoireEffects
{
    private const int MaxDepth = 32;

    private struct Frame
    {
        public NoireGradient? Gradient;
        public NoireMotion? Motion;
        public ImDrawListPtr List;
        public int Start;
        public Vector2 WindowMin;
        public Vector2 WindowMax;
        public float LineHeight;
        public float StartedAt;
        public string? OnceKey;
        public int FrameNumber;
        public int Serial;
    }

    private static readonly Frame[] Frames = new Frame[MaxDepth];
    private static readonly Dictionary<string, float> OnceStarts = new(StringComparer.Ordinal);

    private static int depth;
    private static int serials;

    /// <summary>How many scopes are open right now.</summary>
    public static int Depth => depth;

    /// <summary>What the last scope to close drew, before and after its effects.</summary>
    public static EffectResult LastResult { get; private set; }

    #region Opening and closing

    /// <summary>Opens a scope with a gradient, a motion, or both.</summary>
    /// <param name="gradient">The colors to apply, or <see langword="null"/> for none.</param>
    /// <param name="motion">The movement to apply, or <see langword="null"/> for none.</param>
    /// <param name="drawList">The draw list to record. The default is the one <see cref="NoireShapes"/> paints into: the
    /// current window's, unless redirected.</param>
    /// <param name="startedAt">The moment an effect with <see cref="EffectTrigger.Timed"/> starts, in
    /// <see cref="NoireUI.Time"/> seconds.</param>
    /// <param name="onceKey">The key an effect with <see cref="EffectTrigger.Once"/> runs once for.</param>
    /// <returns>The open scope. Close it with <see langword="using"/> or <see cref="EffectScope.End"/>.</returns>
    public static EffectScope Begin(NoireGradient? gradient, NoireMotion? motion = null, ImDrawListPtr drawList = default,
        float startedAt = float.NaN, string? onceKey = null)
    {
        if (!UiDraw.Available || (gradient == null && motion == null))
            return default;

        var list = drawList.IsNull ? NoireShapes.DrawList : drawList;

        if (list.IsNull)
            return default;

        var frameNumber = ImGui.GetFrameCount();

        if (depth > 0 && Frames[depth - 1].FrameNumber != frameNumber)
            DropStale();

        if (depth >= MaxDepth)
        {
            NoireUI.Diagnostics.ReportFault(nameof(NoireEffects), $"More than {MaxDepth} effect scopes are open at once; this one is ignored.", null);
            return default;
        }

        // The per-frame pass that catches scopes left open. Without a plugin host there is no frame to hook.
        if (NoireService.IsInitialized())
            NoireUI.EnsureFrameServices();

        if (onceKey != null && !OnceStarts.ContainsKey(onceKey))
            OnceStarts[onceKey] = NoireUI.Time;

        var windowMin = ImGui.GetWindowPos();

        Frames[depth] = new Frame
        {
            Gradient = gradient,
            Motion = motion,
            List = list,
            Start = list.VtxBuffer.Size,
            WindowMin = windowMin,
            WindowMax = windowMin + ImGui.GetWindowSize(),
            LineHeight = MathF.Max(1f, ImGui.GetTextLineHeight()),
            StartedAt = startedAt,
            OnceKey = onceKey,
            FrameNumber = frameNumber,
            Serial = ++serials,
        };

        return new EffectScope(depth++, serials);
    }

    /// <summary>Runs a block of drawing inside a scope with a gradient, a motion, or both.</summary>
    /// <param name="gradient">The colors to apply, or <see langword="null"/> for none.</param>
    /// <param name="motion">The movement to apply, or <see langword="null"/> for none.</param>
    /// <param name="body">The drawing.</param>
    /// <returns>What was drawn, before and after the effects.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static EffectResult With(NoireGradient? gradient, NoireMotion? motion, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return With(gradient, motion, body, static b => b());
    }

    /// <summary>
    /// Runs a block of drawing inside a scope with a gradient, a motion, or both, passing a value through.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="gradient">The colors to apply, or <see langword="null"/> for none.</param>
    /// <param name="motion">The movement to apply, or <see langword="null"/> for none.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing.</param>
    /// <returns>What was drawn, before and after the effects.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static EffectResult With<TState>(NoireGradient? gradient, NoireMotion? motion, TState state, Action<TState> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var scope = Begin(gradient, motion);

        try
        {
            UiScope.Run(nameof(NoireEffects), state, body);
        }
        finally
        {
            scope.Dispose();
        }

        return LastResult;
    }

    /// <summary>Draws a line of text with a gradient, a motion, or both.</summary>
    /// <param name="text">The text.</param>
    /// <param name="gradient">The colors to apply, or <see langword="null"/> for none.</param>
    /// <param name="motion">The movement to apply, or <see langword="null"/> for none.</param>
    /// <returns>What was drawn, before and after the effects.</returns>
    public static EffectResult Text(string text, NoireGradient? gradient, NoireMotion? motion = null)
    {
        var scope = Begin(gradient, motion);
        ImGui.TextUnformatted(text);
        return scope.End();
    }

    /// <summary>Draws text wrapped at the edge of the window, with a gradient, a motion, or both.</summary>
    /// <param name="text">The text.</param>
    /// <param name="gradient">The colors to apply, or <see langword="null"/> for none.</param>
    /// <param name="motion">The movement to apply, or <see langword="null"/> for none.</param>
    /// <returns>What was drawn, before and after the effects.</returns>
    public static EffectResult TextWrapped(string text, NoireGradient? gradient, NoireMotion? motion = null)
    {
        var scope = Begin(gradient, motion);
        ImGui.TextWrapped(text);
        return scope.End();
    }

    /// <summary>Closes every open scope, innermost first, applying their effects.</summary>
    public static void EndAll() => CloseAbove(0);

    /// <summary>Lets an effect with <see cref="EffectTrigger.Once"/> run again for a key.</summary>
    /// <param name="key">The key given to <see cref="Begin"/>.</param>
    public static void ResetOnce(string key) => OnceStarts.Remove(key);

    internal static bool IsOpen(int scopeDepth, int serial)
        => serial != 0 && scopeDepth < depth && Frames[scopeDepth].Serial == serial;

    internal static EffectResult End(int scopeDepth, int serial)
    {
        if (!IsOpen(scopeDepth, serial))
            return default;

        EffectResult result = default;

        while (depth > scopeDepth)
        {
            depth--;
            result = Close(ref Frames[depth]);
            Frames[depth] = default;
        }

        LastResult = result;
        return result;
    }

    // Closes every scope opened at or above this depth, for a window that ends with scopes its body left open.
    internal static void CloseAbove(int scopeDepth)
    {
        if (scopeDepth < depth)
            End(scopeDepth, Frames[scopeDepth].Serial);
    }

    // A scope from this frame closes with its effects. One from an earlier frame is dropped: its draw list is gone.
    internal static void DropStale()
    {
        if (depth == 0)
            return;

        NoireUI.Diagnostics.ReportFault(nameof(NoireEffects),
            $"{depth} effect scope(s) were left open by the code that drew them. Close every scope with 'using' or End().", null);

        if (UiDraw.Available && Frames[0].FrameNumber == ImGui.GetFrameCount())
        {
            EndAll();
            return;
        }

        for (var i = 0; i < depth; i++)
            Frames[i] = default;

        depth = 0;
    }

    // Whether a gradient open on this draw list needs shapes subdivided to show, and the finest cell any of them asks for.
    internal static unsafe bool WantsTessellation(ImDrawListPtr list, out float cellSize)
    {
        cellSize = float.MaxValue;

        for (var i = 0; i < depth; i++)
        {
            ref var frame = ref Frames[i];

            if (frame.Gradient is { NeedsTessellation: true, Target: not EffectTarget.Text } gradient && frame.List.Handle == list.Handle)
                cellSize = MathF.Min(cellSize, gradient.CellSize);
        }

        return cellSize < float.MaxValue;
    }

    private static EffectResult Close(ref Frame frame)
    {
        if (frame.FrameNumber != ImGui.GetFrameCount())
            return default;

        var all = frame.List.VtxBuffer.AsSpan();

        if (frame.Start >= all.Length)
            return default;

        var vertices = all[frame.Start..];
        var white = ImGui.GetFontTexUvWhitePixel();

        var (layoutMin, layoutMax) = BoundsOf(vertices);
        var pieces = CountPieces(vertices, white);

        var context = new EffectContext(
            layoutMin,
            layoutMax,
            ImGui.GetItemRectMin(),
            ImGui.GetItemRectMax(),
            frame.WindowMin,
            frame.WindowMax,
            frame.LineHeight,
            white,
            ResolveStart(frame.StartedAt, frame.OnceKey),
            NoireUI.Time,
            NoireUI.ReducedMotion,
            pieces);

        frame.Gradient?.Apply(vertices, in context);
        frame.Motion?.Apply(vertices, in context);

        var (min, max) = frame.Motion == null ? (layoutMin, layoutMax) : BoundsOf(vertices);

        return new EffectResult(layoutMin, layoutMax, min, max, vertices.Length);
    }

    private static float ResolveStart(float startedAt, string? onceKey)
        => onceKey != null && OnceStarts.TryGetValue(onceKey, out var first) ? first : startedAt;

    #endregion

    #region Shared by the effects

    internal static (Vector2 Min, Vector2 Max) BoundsOf(ReadOnlySpan<ImDrawVert> vertices)
    {
        if (vertices.IsEmpty)
            return (Vector2.Zero, Vector2.Zero);

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);

        foreach (ref readonly var vertex in vertices)
        {
            min = Vector2.Min(min, vertex.Pos);
            max = Vector2.Max(max, vertex.Pos);
        }

        return (min, max);
    }

    // A piece is one character (the four corners of its quad) or one run of untextured vertices (one shape).
    internal static int PieceEnd(ReadOnlySpan<ImDrawVert> vertices, int start, Vector2 white, out Vector2 min, out Vector2 max)
    {
        var textured = vertices[start].Uv != white;
        var end = start + 1;

        if (textured)
        {
            while (end < vertices.Length && end - start < 4 && vertices[end].Uv != white)
                end++;
        }
        else
        {
            while (end < vertices.Length && vertices[end].Uv == white)
                end++;
        }

        (min, max) = BoundsOf(vertices[start..end]);
        return end;
    }

    internal static int CountPieces(ReadOnlySpan<ImDrawVert> vertices, Vector2 white)
    {
        var count = 0;

        for (var i = 0; i < vertices.Length;)
        {
            i = PieceEnd(vertices, i, white, out _, out _);
            count++;
        }

        return count;
    }

    internal static bool Targets(EffectTarget target, bool textured) => target switch
    {
        EffectTarget.Text => textured,
        EffectTarget.Shapes => !textured,
        _ => true,
    };

    internal static void ResolveArea(EffectArea area, Vector2 rectMin, Vector2 rectMax, in EffectContext context, out Vector2 min, out Vector2 max)
    {
        switch (area)
        {
            case EffectArea.LastItem:
                min = context.ItemMin;
                max = context.ItemMax;
                break;
            case EffectArea.Window:
                min = context.WindowMin;
                max = context.WindowMax;
                break;
            case EffectArea.Rect:
                min = rectMin;
                max = rectMax;
                break;
            case EffectArea.Screen:
                min = Vector2.Zero;
                max = ImGui.GetIO().DisplaySize;
                break;
            default:
                min = context.LayoutMin;
                max = context.LayoutMax;
                break;
        }
    }

    // The time an effect's animations read, and whether they run at all. At rest the effect shows its time-zero look.
    internal static float AnimationTime(EffectTrigger trigger, float duration, bool ignoreReducedMotion, in EffectContext context,
        Vector2 areaMin, Vector2 areaMax, out bool moving)
    {
        moving = false;

        if (context.ReducedMotion && !ignoreReducedMotion)
            return 0f;

        switch (trigger)
        {
            case EffectTrigger.Hover:
                moving = ImGui.IsMouseHoveringRect(areaMin, areaMax, false);
                return moving ? context.Now : 0f;
            case EffectTrigger.Timed:
            case EffectTrigger.Once:
            {
                if (float.IsNaN(context.StartedAt))
                    return 0f;

                var elapsed = context.Now - context.StartedAt;
                moving = elapsed >= 0f && elapsed <= duration;
                return moving ? elapsed : 0f;
            }
            default:
                moving = true;
                return context.Now;
        }
    }

    /// <summary>The value of a repeating animation at a moment of its cycle.</summary>
    /// <param name="wave">The shape of the cycle.</param>
    /// <param name="cycles">How many cycles have passed. Only the fraction counts.</param>
    /// <returns>From 0 (rest) to 1 (peak).</returns>
    public static float Wave(EffectWave wave, float cycles)
    {
        var f = Frac(cycles);

        return wave switch
        {
            EffectWave.Triangle => 1f - MathF.Abs((2f * f) - 1f),
            EffectWave.Square => f < 0.5f ? 1f : 0f,
            EffectWave.Sawtooth => f,
            EffectWave.Heartbeat => f < 0.12f ? MathF.Sin(MathF.PI * f / 0.12f)
                : f >= 0.2f && f < 0.32f ? 0.7f * MathF.Sin(MathF.PI * (f - 0.2f) / 0.12f)
                : 0f,
            EffectWave.Bounce => MathF.Abs(MathF.Cos(3f * MathF.PI * f)) * (1f - f) * (1f - f),
            _ => 0.5f - (0.5f * MathF.Cos(2f * MathF.PI * f)),
        };
    }

    // Whether a burst that starts every 'every' seconds and lasts 'length' seconds is running. No period means always.
    internal static bool InBurst(float time, float every, float length)
        => every <= 0f || (time - (MathF.Floor(time / every) * every)) < length;

    internal static float Frac(float value) => value - MathF.Floor(value);

    // A repeatable pseudo-random number from 0 to 1 for a pair of integers.
    internal static float Hash(int a, int b)
    {
        unchecked
        {
            var h = (uint)a * 0x9E3779B1u;
            h ^= (uint)b * 0x85EBCA77u;
            h ^= h >> 15;
            h *= 0xC2B2AE3Du;
            h ^= h >> 13;
            return (h & 0x00FFFFFFu) / 16777216f;
        }
    }

    #endregion
}

// What every effect of one closing scope reads.
internal readonly struct EffectContext(
    Vector2 layoutMin,
    Vector2 layoutMax,
    Vector2 itemMin,
    Vector2 itemMax,
    Vector2 windowMin,
    Vector2 windowMax,
    float lineHeight,
    Vector2 white,
    float startedAt,
    float now,
    bool reducedMotion,
    int pieceCount)
{
    public readonly Vector2 LayoutMin = layoutMin;
    public readonly Vector2 LayoutMax = layoutMax;
    public readonly Vector2 ItemMin = itemMin;
    public readonly Vector2 ItemMax = itemMax;
    public readonly Vector2 WindowMin = windowMin;
    public readonly Vector2 WindowMax = windowMax;
    public readonly float LineHeight = lineHeight;
    public readonly Vector2 White = white;
    public readonly float StartedAt = startedAt;
    public readonly float Now = now;
    public readonly bool ReducedMotion = reducedMotion;
    public readonly int PieceCount = pieceCount;
}
