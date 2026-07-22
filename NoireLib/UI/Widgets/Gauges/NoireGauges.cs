using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Small readouts that show a number as a shape: rings, bars, pips and countdowns. Immediate and stateless, so they
/// compose anywhere a widget can go, including inside a table cell, on a world label, or on a button.
/// </summary>
/// <remarks>
/// Every gauge takes a fraction from 0 to 1 and draws at the cursor, reserving exactly the space it used, so it sits
/// in a layout like any other item. Nothing is registered, nothing is remembered, and drawing one twice draws two.<br/>
/// Colours left <see langword="null"/> resolve through <see cref="NoireTheme"/>, and every size is a logical pixel
/// value at 100%. See <see cref="NoireUI.Scale"/>.
/// </remarks>
/// <example>
/// <code>
/// NoireGauges.Bar(hp / (float)maxHp, new BarStyle
/// {
///     Label = $"{hp} / {maxHp}",
///     Thresholds = [new GaugeThreshold(0.25f, theme.Resolve(ThemeColor.Danger))],
/// });
/// </code>
/// </example>
[NoireFacade]
public static partial class NoireGauges
{
    private static readonly RingStyle DefaultRingStyle = new();
    private static readonly BarStyle DefaultBarStyle = new();
    private static readonly PipStyle DefaultPipStyle = new();

    #region Ring

    /// <summary>
    /// Draws a ring filled clockwise from the top, the shape a cooldown or a percentage reads best as.
    /// </summary>
    /// <param name="value">The fraction filled, from 0 to 1. Values outside that range are clamped.</param>
    /// <param name="style">How to draw it, or <see langword="null"/> for the default ring.</param>
    public static void Ring(float value, RingStyle? style = null)
    {
        style ??= DefaultRingStyle;
        Ring(value, style, style.Label);
    }

    /// <summary>
    /// Draws a ring with its label already worked out.
    /// </summary>
    /// <remarks>
    /// The label is passed rather than read off the style so that a countdown can put the time left on a caller's style
    /// without copying it. Copying it per frame is what a countdown used to cost.
    /// </remarks>
    /// <param name="value">The fraction filled, from 0 to 1.</param>
    /// <param name="style">How to draw it.</param>
    /// <param name="label">The text in the middle, or <see langword="null"/> for none.</param>
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

        NoireShapes.Wedge(centre, inner, outer, style.StartTurns, style.StartTurns + sweep, style.ResolveTrackColor());

        if (fraction > 0f)
        {
            NoireShapes.Wedge(
                centre, inner, outer, style.StartTurns, style.StartTurns + (sweep * fraction), fill);
        }

        ImGui.Dummy(new Vector2(size, size));

        if (!string.IsNullOrEmpty(label))
            DrawCentredLabel(label, style.LabelSize, style.LabelColor ?? fill, centre);
    }

    #endregion

    #region Bar

    /// <summary>
    /// Draws a horizontal bar, with optional threshold colours, hairline marks and a label over it.
    /// </summary>
    /// <param name="value">The fraction filled, from 0 to 1. Values outside that range are clamped.</param>
    /// <param name="style">How to draw it, or <see langword="null"/> for the default bar.</param>
    public static void Bar(float value, BarStyle? style = null)
    {
        style ??= DefaultBarStyle;
        Bar(value, style, style.Label);
    }

    /// <summary>
    /// Draws a bar with its label already worked out.
    /// </summary>
    /// <remarks>
    /// See the ring's private overload for why the label is passed rather than read off the style.
    /// </remarks>
    /// <param name="value">The fraction filled, from 0 to 1.</param>
    /// <param name="style">How to draw it.</param>
    /// <param name="label">The text over the bar, or <see langword="null"/> for none.</param>
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

        NoireShapes.Rect(origin, max, style.ResolveTrackColor(), CornerShape.Rounded, rounding);

        if (fraction > 0f)
        {
            var fillMax = new Vector2(origin.X + (width * fraction), max.Y);

            // The fill is clipped to the track's own rounded shape rather than rounded itself: a short bar with its own
            // rounding is a lozenge floating inside the track, and a full one has a visible seam at the right end.
            // Taken from the window's own list because this call establishes a redirect, and resolving the list the way
            // a shape does would read back a redirect already in force and make the call a no-op.
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
            DrawBarLabel(style, label, origin, width, height);
    }

    /// <summary>
    /// Draws the hairlines at the fractions the bar was told always matter.
    /// </summary>
    /// <param name="style">The bar style.</param>
    /// <param name="origin">The top left of the bar.</param>
    /// <param name="width">The bar width in real pixels.</param>
    /// <param name="height">The bar height in real pixels.</param>
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

    /// <summary>
    /// Draws the label over a bar, aligned along it.
    /// </summary>
    /// <param name="style">The bar style.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="origin">The top left of the bar.</param>
    /// <param name="width">The bar width in real pixels.</param>
    /// <param name="height">The bar height in real pixels.</param>
    private static void DrawBarLabel(BarStyle style, string text, Vector2 origin, float width, float height)
    {
        var measured = NoireText.CalcSize(text, style.LabelSize);
        var x = origin.X + ((width - measured.X) * Math.Clamp(style.LabelAlign, 0f, 1f));
        var y = origin.Y + ((height - measured.Y) * 0.5f);

        DrawTextAt(text, style.LabelSize, style.LabelColor ?? NoireTheme.Current.Resolve(ThemeColor.Text),
            new Vector2(x, y));
    }

    #endregion

    #region Pips

    /// <summary>
    /// Draws a row of pips, the readout for a count small enough to be seen rather than read: stacks, charges, lives.
    /// </summary>
    /// <param name="filled">How many pips are filled. Clamped to the total.</param>
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

        for (var i = 0; i < total; i++)
        {
            var min = new Vector2(origin.X + (i * (size + spacing)), origin.Y);
            var max = min + new Vector2(size);

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
    /// <remarks>
    /// The ring empties as the time runs out rather than filling, because a countdown that fills reads as progress
    /// toward something rather than as time being spent.
    /// </remarks>
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

    /// <summary>
    /// Writes the time left on a countdown, never as a negative.
    /// </summary>
    /// <remarks>
    /// A countdown that has run out reads <c>0s</c> rather than counting up past zero, and the text is remembered for
    /// as long as the second lasts: a countdown redraws sixty times for each value it shows.
    /// </remarks>
    /// <param name="remaining">How much time is left.</param>
    /// <returns>The time left, in shorthand.</returns>
    private static string Remaining(TimeSpan remaining)
        => UiValueText.Duration(remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining);

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
    /// <remarks>
    /// The lowest threshold the value has fallen to or below wins, so a bar can carry a warning band and a critical
    /// one at once and always paint the more urgent of the two. With no thresholds the base colour is used, and with
    /// no base colour the theme's accent is.
    /// </remarks>
    /// <param name="value">The fraction being drawn, from 0 to 1.</param>
    /// <param name="thresholds">The thresholds to consider, or <see langword="null"/> for none.</param>
    /// <param name="baseColor">The colour to use when no threshold applies.</param>
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

    /// <summary>
    /// Draws a label centred on a point, used for the text in the middle of a ring.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    /// <param name="color">The colour to draw it in.</param>
    /// <param name="centre">The point to centre it on, in screen pixels.</param>
    private static void DrawCentredLabel(string text, TextSize size, Vector4 color, Vector2 centre)
    {
        var measured = NoireText.CalcSize(text, size);
        DrawTextAt(text, size, color, centre - (measured * 0.5f));
    }

    /// <summary>
    /// Draws text at an exact screen position without disturbing the layout the gauge already reserved.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    /// <param name="color">The colour to draw it in.</param>
    /// <param name="position">Where to put its top left corner, in screen pixels.</param>
    private static void DrawTextAt(string text, TextSize size, Vector4 color, Vector2 position)
    {
        var restore = ImGui.GetCursorScreenPos();

        ImGui.SetCursorScreenPos(position);
        NoireText.Colored(color, text, size);
        ImGui.SetCursorScreenPos(restore);
    }

    #endregion
}
