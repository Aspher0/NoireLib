using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using NoireLib.Helpers;
using System;
using System.Globalization;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Sliders drawn by the library rather than by ImGui, so they can be restyled to the last pixel.
/// </summary>
/// <remarks>
/// ImGui's slider is drawn from its own style and cannot be replaced: a track and a rectangular grab, coloured from
/// four style entries and no more. A design that wants a hairline track with a lit diamond running along it has no way
/// to ask for one. So the drawing is ours, and it goes through the same style-plus-custom-draw shape as the buttons and
/// toggles: the widget keeps the arithmetic, the hook paints.<br/>
/// The label column matches <see cref="NoireInputs"/>, so a slider between two number fields lines up with them.
/// </remarks>
/// <example>
/// <code>
/// NoireSliders.Int("Visible options", ref config.VisibleOptions, 1, 20,
///     new SliderStyle { Grab = SliderGrab.Diamond });
/// </code>
/// </example>
public static class NoireSliders
{
    /// <summary>
    /// Draws a slider over a range of whole numbers.
    /// </summary>
    /// <param name="label">The row's label. Anything after "###" is the stable id, as in ImGui.</param>
    /// <param name="value">The value, written back as it is dragged.</param>
    /// <param name="min">The low end of the range.</param>
    /// <param name="max">The high end of the range.</param>
    /// <param name="style">The slider's look. When <see langword="null"/>, the theme's.</param>
    /// <returns>True on the frames the value changes.</returns>
    public static bool Int(string label, ref int value, int min, int max, SliderStyle? style = null)
    {
        var resolved = style ?? DefaultStyle;
        float asFloat = value;

        // Rounded on the way back rather than snapped inside the drag, so the handle sits exactly on the value the
        // caller now holds instead of a whole pixel away from it.
        var changed = Draw(label, ref asFloat, min, max, resolved, resolved.ValueFormat ?? "0", whole: true);

        if (changed)
            value = (int)MathF.Round(asFloat);

        return changed;
    }

    /// <summary>
    /// Draws a slider over a range of real numbers.
    /// </summary>
    /// <param name="label">The row's label. Anything after "###" is the stable id, as in ImGui.</param>
    /// <param name="value">The value, written back as it is dragged.</param>
    /// <param name="min">The low end of the range.</param>
    /// <param name="max">The high end of the range.</param>
    /// <param name="style">The slider's look. When <see langword="null"/>, the theme's.</param>
    /// <returns>True on the frames the value changes.</returns>
    public static bool Float(string label, ref float value, float min, float max, SliderStyle? style = null)
    {
        var resolved = style ?? DefaultStyle;
        return Draw(label, ref value, min, max, resolved, resolved.ValueFormat ?? "0.##", whole: false);
    }

    /// <summary>
    /// The whole of a slider: the label, the hit box, the drag, and the painting.
    /// </summary>
    private static bool Draw(string label, ref float value, float min, float max, SliderStyle style, string format, bool whole)
    {
        NoireUI.EnsureFrameServices();

        string id;

        if (max < min)
            (min, max) = (max, min);

        var rowWidth = NoireLayout.ContentWidth();
        var rowStart = ImGui.GetCursorPosX();

        float column;

        using (style.LabelColor is { } labelColor ? ImRaii.PushColor(ImGuiCol.Text, labelColor) : null)
            column = NoireInputs.BeginRow(label, 0f, out id, sizeField: false, labelWidth: style.LabelWidth);

        var height = ImGui.GetFrameHeight();
        var valueWidth = style.ShowValue ? NoireUI.Scaled(style.ValueWidth) + NoireUI.Scaled(8f) : 0f;
        var grab = NoireUI.Scaled(style.GrabSize);

        // The track stops half a handle short of each end, so the handle's own edges land on the ends of the track
        // rather than hanging past them at 0 and 100 percent.
        var width = MathF.Max(grab + NoireUI.Scaled(8f), rowWidth - column - valueWidth);
        var origin = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, height);

        var pressed = ImGui.InvisibleButton($"###NoireSlider_{id}", size);
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();

        var trackY = MathF.Floor(origin.Y + (height * 0.5f));
        var trackMin = new Vector2(origin.X + (grab * 0.5f), trackY);
        var trackMax = new Vector2(origin.X + width - (grab * 0.5f), trackY);
        var span = MathF.Max(1f, trackMax.X - trackMin.X);

        var changed = false;

        if (held || pressed)
        {
            var target = ResolveValue(ImGui.GetIO().MousePos.X, trackMin.X, span, min, max, whole);

            if (MathF.Abs(target - value) > float.Epsilon)
            {
                value = target;
                changed = true;
            }
        }

        var fraction = ResolveFraction(value, min, max);
        var grabCenter = new Vector2(trackMin.X + (span * fraction), trackY);

        var args = new UiSliderDraw(origin, origin + size, trackMin, trackMax, grabCenter, fraction, value, hovered, held, style);

        if (style.CustomDraw != null)
            Invoke(() => style.CustomDraw(args), nameof(NoireSliders));
        else
            Paint(args);

        if (hovered || held)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (style.ShowValue)
            DrawValue(value, format, style, origin, width, height);

        // Put back where a row expects to be left, so whatever comes next starts on its own line at the margin.
        ImGui.SetCursorPosX(rowStart);

        return changed;
    }

    /// <summary>
    /// The shipped drawing: a track, the filled part of it, and a handle.
    /// </summary>
    private static void Paint(UiSliderDraw args)
    {
        var theme = NoireTheme.Current;
        var style = args.Style;
        var thickness = MathF.Max(1f, NoireUI.Scaled(style.TrackThickness));
        var half = thickness * 0.5f;

        var track = style.TrackColor ?? theme.Resolve(ThemeColor.SurfaceSunken);
        var fill = style.FillColor ?? theme.Resolve(ThemeColor.Accent);
        var rounding = thickness * 0.5f;

        NoireShapes.Rect(
            new Vector2(args.TrackMin.X, args.TrackMin.Y - half),
            new Vector2(args.TrackMax.X, args.TrackMax.Y + half),
            track, CornerShape.Rounded, rounding);

        if (args.Fraction > 0f)
        {
            var from = new Vector2(args.TrackMin.X, args.TrackMin.Y - half);
            var to = new Vector2(args.GrabCenter.X, args.TrackMin.Y + half);

            if (style.FillTo is { } end)
                NoireShapes.Gradient(from, to, fill, end, (from, to, rounding), static s => NoireShapes.Rect(s.from, s.to, Vector4.One, CornerShape.Rounded, s.rounding));
            else
                NoireShapes.Rect(from, to, fill, CornerShape.Rounded, rounding);
        }

        PaintGrab(args, theme);
    }

    /// <summary>
    /// Draws the handle in whichever shape the style asks for.
    /// </summary>
    private static void PaintGrab(UiSliderDraw args, NoireTheme theme)
    {
        var style = args.Style;
        var size = NoireUI.Scaled(style.GrabSize);

        // Lifted a little while it is being used, which is the whole of the feedback a slider needs and reads as the
        // handle being picked up rather than as a colour change nobody notices mid-drag.
        if (args.Held)
            size *= 1.12f;

        var half = size * 0.5f;
        var centre = args.GrabCenter;
        var color = style.GrabColor ?? theme.Resolve(ThemeColor.Accent);

        if (args.Held)
            color = theme.Active(color);
        else if (args.Hovered)
            color = theme.Hover(color);

        var min = centre - new Vector2(half, half);
        var max = centre + new Vector2(half, half);

        if (style.Grab == SliderGrab.Diamond)
        {
            // The halo follows the diamond rather than its bounding box. Growing the rectangle instead leaves a lit
            // square sitting behind the handle, which is the tell that the glow knows nothing about what it is lighting.
            if (style.GlowColor is { } diamondGlow)
            {
                Span<Vector2> path = stackalloc Vector2[4];
                var count = NoireShapes.DiamondPath(centre, half, path);

                if (count > 0)
                    NoireShapes.GlowPath(path[..count], diamondGlow, NoireUI.Scaled(style.GlowSpread));
            }

            // Painted white inside a gradient scope when it ramps, because the scope recolours whatever the body
            // emitted: a flat white shape is what gives the ramp the full range to work over.
            if (style.GrabColorTo is { } to)
                NoireShapes.Gradient(min, max, GradientAxis.Vertical, color, to, (centre, half), static s => NoireShapes.Diamond(s.centre, s.half, Vector4.One));
            else
                NoireShapes.Diamond(centre, half, color);

            return;
        }

        if (style.GlowColor is { } glow)
        {
            NoireShapes.Glow(
                min, max, glow, NoireUI.Scaled(style.GlowSpread),
                style.Grab == SliderGrab.Circle ? CornerShape.Rounded : CornerShape.Square,
                half);
        }

        var shape = style.Grab == SliderGrab.Square ? CornerShape.Square : CornerShape.Rounded;
        var corner = style.Grab switch
        {
            SliderGrab.Circle => half,
            SliderGrab.Rounded => MathF.Max(1f, half * 0.4f),
            _ => 0f,
        };

        if (style.GrabColorTo is { } gradientTo)
            NoireShapes.Gradient(min, max, GradientAxis.Vertical, color, gradientTo, (min, max, shape, corner), static s => NoireShapes.Rect(s.min, s.max, Vector4.One, s.shape, s.corner));
        else
            NoireShapes.Rect(min, max, color, shape, corner);
    }

    /// <summary>
    /// Writes the value at the end of the row, in a column reserved whatever it reads.
    /// </summary>
    private static void DrawValue(float value, string format, SliderStyle style, Vector2 origin, float width, float height)
    {
        var theme = NoireTheme.Current;
        var text = style.ValueText is { } words
            ? words(value) ?? string.Empty
            : value.ToString(format, CultureInfo.CurrentCulture);
        var column = NoireUI.Scaled(style.ValueWidth);
        var measured = NoireText.CalcSize(text);

        // Right-aligned inside its column, so the digits line up down a stack of sliders instead of wandering with the
        // width of the number.
        var at = new Vector2(
            origin.X + width + NoireUI.Scaled(8f) + column - measured.X,
            origin.Y + (height * 0.5f) - NoireText.CenterOffset());

        ImGui.SetCursorScreenPos(at);

        using var pushed = ImRaii.PushColor(ImGuiCol.Text, style.ValueColor ?? theme.Resolve(ThemeColor.TextMuted));
        ImGui.PushTextWrapPos(-1f);
        NoireText.Draw(text);
        ImGui.PopTextWrapPos();
    }

    /// <summary>
    /// What value a pointer position on the track means.
    /// </summary>
    /// <remarks>
    /// Taken from where the pointer is rather than from how far it has moved, which is the decision worth being sure
    /// about: a drag driven by mouse delta accumulates a drift away from the cursor over a long gesture, and a click on
    /// the track then jumps by that drift rather than to where it was aimed. Reading the position outright makes a
    /// click and a drag the same operation and leaves nothing to accumulate.<br/>
    /// Clamped rather than refused past the ends, because dragging off the end of a track means "as far as it goes".
    /// </remarks>
    /// <param name="pointerX">Where the pointer is, in screen pixels.</param>
    /// <param name="trackX">Where the track starts, in screen pixels.</param>
    /// <param name="span">How long the track is, in pixels.</param>
    /// <param name="min">The low end of the range.</param>
    /// <param name="max">The high end of the range.</param>
    /// <param name="whole">Whether the value is rounded to a whole number.</param>
    /// <returns>The value the pointer is asking for.</returns>
    internal static float ResolveValue(float pointerX, float trackX, float span, float min, float max, bool whole)
    {
        if (span <= 0f)
            return min;

        var at = Math.Clamp((pointerX - trackX) / span, 0f, 1f);
        var value = min + (at * (max - min));

        if (whole)
            value = MathF.Round(value);

        return Math.Clamp(value, min, max);
    }

    /// <summary>
    /// How far along its range a value sits, which is where the handle goes and how much of the track is filled.
    /// </summary>
    /// <remarks>
    /// A range of no width answers zero rather than dividing by it: a slider whose two ends are the same number is not
    /// an error worth throwing over, it is a slider with nowhere to go.
    /// </remarks>
    /// <param name="value">The value.</param>
    /// <param name="min">The low end of the range.</param>
    /// <param name="max">The high end of the range.</param>
    /// <returns>A fraction from 0 to 1.</returns>
    internal static float ResolveFraction(float value, float min, float max)
    {
        var range = max - min;
        return range > float.Epsilon ? Math.Clamp((value - min) / range, 0f, 1f) : 0f;
    }

    /// <summary>
    /// Runs a consumer callback, reporting anything it throws rather than letting it escape into the frame.
    /// </summary>
    private static void Invoke(Action callback, string source)
    {
        try
        {
            callback();
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(source, "A slider callback threw.", ex);
        }
    }

    private static readonly SliderStyle DefaultStyle = new();
}
