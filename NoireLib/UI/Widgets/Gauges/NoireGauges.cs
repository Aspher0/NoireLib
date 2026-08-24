using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Small readouts that show a number as a shape: rings, bars, pips and countdowns. Immediate and stateless.
/// </summary>
[NoireFacade]
public static partial class NoireGauges
{
    private static readonly RingStyle DefaultRingStyle = new();
    private static readonly BarStyle DefaultBarStyle = new();
    private static readonly PipStyle DefaultPipStyle = new();

    private const string CallbackFault = "A gauge hook threw.";

    #region Ring

    /// <summary>
    /// Draws a ring filled clockwise from the top.
    /// </summary>
    /// <param name="value">The fraction filled, from 0 to 1, clamped.</param>
    /// <param name="style">How to draw it, or <see langword="null"/> for the default ring.</param>
    public static void Ring(float value, RingStyle? style = null)
    {
        style ??= DefaultRingStyle;
        Ring(value, style, style.Label);
    }

    private static void Ring(float value, RingStyle style, string? label)
    {
        using var draw = UiDraw.Begin();

        var size = MathF.Max(style.ScaledSize, 1f);
        var origin = ImGui.GetCursorScreenPos();
        var centre = origin + new Vector2(size * 0.5f);
        var outer = size * 0.5f;
        var inner = MathF.Max(0f, outer - MathF.Max(style.ScaledThickness, 1f));

        var fraction = Math.Clamp(value, 0f, 1f);
        var sweep = style.SweepTurns * (style.Clockwise ? 1f : -1f);
        var fill = ResolveFillColor(fraction, style.Thresholds, style.Color);

        if (style.CustomDraw != null)
        {
            if (!draw.List.IsNull)
            {
                var args = new UiRingDraw(
                    draw.List, centre, inner, outer, style.StartTurns, sweep, fraction,
                    style.ResolveTrackColor(), fill, label, style.LabelSize, style.LabelColor ?? fill);
                UiHook.Invoke(style.CustomDraw, args, nameof(Ring), CallbackFault);
            }

            ImGui.Dummy(new Vector2(size, size));
            return;
        }

        NoireShapes.Wedge(centre, inner, outer, style.StartTurns, style.StartTurns + sweep, style.ResolveTrackColor());

        if (fraction > 0f)
        {
            NoireShapes.Wedge(
                centre, inner, outer, style.StartTurns, style.StartTurns + (sweep * fraction), fill);
        }

        ImGui.Dummy(new Vector2(size, size));

        if (!string.IsNullOrEmpty(label))
            DrawCentredLabel(label, style.LabelSize, style.LabelColor ?? fill, centre, LabelFitWidth(inner, size));
    }

    // The hole a ring's centre label sits in, less a hair so it does not touch the band.
    private static float LabelFitWidth(float inner, float size)
        => inner > 0f ? MathF.Max(0f, (inner * 2f) - NoireUI.Scaled(4f)) : size;

    #endregion

    #region Bar

    /// <summary>
    /// Draws a horizontal bar, with optional threshold colours, hairline marks and a label over it.
    /// </summary>
    /// <param name="value">The fraction filled, from 0 to 1, clamped.</param>
    /// <param name="style">How to draw it, or <see langword="null"/> for the default bar.</param>
    public static void Bar(float value, BarStyle? style = null)
    {
        style ??= DefaultBarStyle;
        Bar(value, style, style.Label);
    }

    private static void Bar(float value, BarStyle style, string? label)
    {
        using var draw = UiDraw.Begin();

        var width = style.Width > 0f ? style.ScaledWidth : NoireLayout.ContentWidth();
        var height = MathF.Max(style.ScaledHeight, 1f);

        if (width <= 0f)
            return;

        var origin = ImGui.GetCursorScreenPos();
        var max = origin + new Vector2(width, height);
        var rounding = style.ScaledRounding;
        var fraction = Math.Clamp(value, 0f, 1f);
        var fill = ResolveFillColor(fraction, style.Thresholds, style.Color);

        if (style.CustomDraw != null)
        {
            if (!draw.List.IsNull)
            {
                var args = new UiBarDraw(
                    draw.List, origin, max, fraction, rounding, style.ResolveTrackColor(), fill, style.ColorTo,
                    style.Marks, style.ResolveMarkColor(), label, style.LabelSize, style.LabelAlign,
                    style.LabelColor ?? NoireTheme.Current.Resolve(ThemeColor.Text));
                UiHook.Invoke(style.CustomDraw, args, nameof(Bar), CallbackFault);
            }

            ImGui.Dummy(new Vector2(width, height));
            return;
        }

        NoireShapes.Rect(origin, max, style.ResolveTrackColor(), CornerShape.Rounded, rounding);

        if (fraction > 0f)
        {
            var fillMax = new Vector2(origin.X + (width * fraction), max.Y);

            // Clipped to the track's rounded shape rather than rounded itself, so a short fill does not float as a
            // lozenge inside the track and a full one shows no seam at the right end.
            // Taken from the window's own list: resolving it the way a shape does would read back the redirect this
            // call establishes and no-op.
            using var inner = UiDraw.BeginWindow();

            NoireShapes.On(inner.List, (origin, max, fillMax, fill, style, rounding), static state =>
            {
                ImGui.PushClipRect(state.origin, state.fillMax, true);

                if (state.style.ColorTo is { } to)
                {
                    NoireShapes.GradientRect(
                        state.origin, state.max, state.fill, to, GradientAxis.Horizontal,
                        CornerShape.Rounded, state.rounding);
                }
                else
                {
                    NoireShapes.Rect(state.origin, state.max, state.fill, CornerShape.Rounded, state.rounding);
                }

                ImGui.PopClipRect();
            });
        }

        DrawMarks(style, origin, width, height);
        ImGui.Dummy(new Vector2(width, height));

        if (!string.IsNullOrEmpty(label))
        {
            DrawBarLabel(
                label, style.LabelSize, style.LabelAlign,
                style.LabelColor ?? NoireTheme.Current.Resolve(ThemeColor.Text), origin, width, height);
        }
    }

    // Draws a hairline at each of the bar's mark fractions.
    private static void DrawMarks(BarStyle style, Vector2 origin, float width, float height)
    {
        if (style.Marks == null || style.Marks.Count == 0)
            return;

        var color = style.ResolveMarkColor();

        foreach (var mark in style.Marks)
        {
            var x = MathF.Round(origin.X + (width * Math.Clamp(mark, 0f, 1f)));
            NoireShapes.Rect(new Vector2(x, origin.Y), new Vector2(x + 1f, origin.Y + height), color);
        }
    }

    // align is where the label sits along the bar, from 0 (left) to 1 (right).
    internal static void DrawBarLabel(
        string text, TextSize size, float align, Vector4 color, Vector2 origin, float width, float height)
    {
        var sizePx = FitTextSize(text, NoireTheme.Current.ResolveTextSize(size), width);
        var measured = NoireText.CalcSize(text, sizePx);
        var x = origin.X + ((width - measured.X) * Math.Clamp(align, 0f, 1f));
        var y = origin.Y + ((height - measured.Y) * 0.5f);

        NoireText.DrawAt(new Vector2(x, y), color, text, sizePx);
    }

    #endregion

    #region Pips

    /// <summary>Draws a row of pips.</summary>
    /// <param name="filled">How many pips are filled, clamped to the total.</param>
    /// <param name="total">How many pips there are.</param>
    /// <param name="style">How to draw them, or <see langword="null"/> for the default pips.</param>
    public static void Pips(int filled, int total, PipStyle? style = null)
    {
        style ??= DefaultPipStyle;

        if (total <= 0)
            return;

        using var draw = UiDraw.Begin();

        var size = MathF.Max(style.ScaledSize, 1f);
        var spacing = style.ScaledSpacing;
        var origin = ImGui.GetCursorScreenPos();
        var rounding = style.Shape == CornerShape.Rounded ? size * 0.5f : size * 0.25f;

        var on = Math.Clamp(filled, 0, total);
        var color = style.ResolveColor();
        var empty = style.ResolveEmptyColor();
        var list = draw.List;

        for (var i = 0; i < total; i++)
        {
            var min = new Vector2(origin.X + (i * (size + spacing)), origin.Y);
            var max = min + new Vector2(size);

            if (style.CustomDraw != null)
            {
                if (!list.IsNull)
                {
                    var isOn = i < on;
                    var args = new UiPipDraw(
                        list, min, max, i, total, isOn, isOn ? color : empty,
                        !isOn && style.OutlineEmpty, style.Shape, rounding);
                    UiHook.Invoke(style.CustomDraw, args, nameof(Pips), CallbackFault);
                }

                continue;
            }

            if (i < on)
                NoireShapes.Rect(min, max, color, style.Shape, rounding);
            else if (style.OutlineEmpty)
                NoireShapes.RectOutline(min, max, empty, 1f, style.Shape, rounding);
            else
                NoireShapes.Rect(min, max, empty, style.Shape, rounding);
        }

        ImGui.Dummy(new Vector2((total * (size + spacing)) - spacing, size));
    }

    #endregion

    #region Timers

    /// <summary>
    /// Draws a ring counting down, labelled with the time left.
    /// </summary>
    /// <param name="remaining">How much time is left.</param>
    /// <param name="total">How long the countdown started at.</param>
    /// <param name="style">How to draw it, or <see langword="null"/> for the default ring.</param>
    public static void Timer(TimeSpan remaining, TimeSpan total, RingStyle? style = null)
    {
        style ??= DefaultRingStyle;
        Ring(TimerFraction(remaining, total), style, style.Label ?? Remaining(remaining));
    }

    /// <summary>
    /// Draws a bar counting down, labelled with the time left.
    /// </summary>
    /// <param name="remaining">How much time is left.</param>
    /// <param name="total">How long the countdown started at.</param>
    /// <param name="style">How to draw it, or <see langword="null"/> for the default bar.</param>
    public static void Timer(TimeSpan remaining, TimeSpan total, BarStyle? style)
    {
        style ??= DefaultBarStyle;
        Bar(TimerFraction(remaining, total), style, style.Label ?? Remaining(remaining));
    }

    // Reads 0s rather than counting past zero.
    private static string Remaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
            return ZeroRemaining;

        return UiValueText.Duration(TimeSpan.FromSeconds(MathF.Ceiling((float)remaining.TotalSeconds)));
    }

    // Held as a constant so an expired timer allocates nothing.
    private const string ZeroRemaining = "0s";

    /// <summary>
    /// How full a countdown is, from 1 at the start to 0 when it runs out.
    /// </summary>
    /// <param name="remaining">How much time is left.</param>
    /// <param name="total">How long the countdown started at.</param>
    /// <returns>The fraction remaining, from 0 to 1.</returns>
    public static float TimerFraction(TimeSpan remaining, TimeSpan total)
    {
        if (total <= TimeSpan.Zero)
            return 0f;

        return Math.Clamp((float)(remaining.TotalSeconds / total.TotalSeconds), 0f, 1f);
    }

    #endregion

    #region Shared

    /// <summary>
    /// Works out what colour a gauge fills with at a given value.
    /// </summary>
    /// <param name="value">The fraction being drawn, from 0 to 1.</param>
    /// <param name="thresholds">The thresholds to consider, or <see langword="null"/> for none.</param>
    /// <param name="baseColor">The colour to use when no threshold applies, or <see langword="null"/> for the theme accent.</param>
    /// <returns>The colour to fill with.</returns>
    public static Vector4 ResolveFillColor(
        float value,
        IReadOnlyList<GaugeThreshold>? thresholds,
        Vector4? baseColor)
    {
        var fallback = baseColor ?? NoireTheme.Current.Resolve(ThemeColor.Accent);

        if (thresholds == null || thresholds.Count == 0)
            return fallback;

        var bestValue = float.MaxValue;
        var best = fallback;
        var matched = false;

        foreach (var threshold in thresholds)
        {
            if (value > threshold.Value || threshold.Value > bestValue)
                continue;

            bestValue = threshold.Value;
            best = threshold.Color;
            matched = true;
        }

        return matched ? best : fallback;
    }

    // fitWidth is the width the text must fit inside, or zero to draw it at its own size.
    internal static void DrawCentredLabel(string text, TextSize size, Vector4 color, Vector2 centre, float fitWidth = 0f)
    {
        var sizePx = FitTextSize(text, NoireTheme.Current.ResolveTextSize(size), fitWidth);
        var measured = NoireText.CalcSize(text, sizePx);

        NoireText.DrawAt(centre - (measured * 0.5f), color, text, sizePx);
    }

    // The size a label is drawn at so it fits a given width, never larger than the size asked for. A fitWidth of zero
    // means no limit.
    internal static float FitTextSize(string text, float sizePx, float fitWidth)
    {
        if (fitWidth <= 0f || string.IsNullOrEmpty(text))
            return sizePx;

        var size = sizePx;

        // Stepped down and measured again rather than scaled once by the ratio. Width is only roughly proportional to
        // size: glyph advances land on whole pixels, a font is rasterized per size, and an unbuilt size is measured
        // through a stretched stand-in. One ratio can therefore still overshoot, and overshooting here is a label
        // drawn straight through the ring around it.
        for (var attempt = 0; attempt < MaxFitAttempts; attempt++)
        {
            var measured = NoireText.CalcSize(text, size).X;

            if (measured <= fitWidth || measured <= 0f)
                return size;

            var next = MathF.Floor(size * fitWidth / measured);

            // A ratio that does not actually reduce the size would loop forever at the same measurement.
            if (next >= size)
                next = size - 1f;

            if (next < MinFittedLabelSize)
                return MinFittedLabelSize;

            size = next;
        }

        return size;
    }

    // The smallest a label is shrunk to before it is simply allowed to overflow.
    internal const float MinFittedLabelSize = 7f;

    // How many times a label may be re-measured on its way down to a size that fits.
    private const int MaxFitAttempts = 4;

    #endregion
}
