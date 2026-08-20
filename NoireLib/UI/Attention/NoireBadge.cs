using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Draws count and dot badges over a rectangle. Stateless, and submits no ImGui item and no cursor movement,
/// so a badge can follow any widget without changing the layout around it.
/// </summary>
[NoireFacade]
public static class NoireBadge
{
    private static readonly BadgeStyle Default = new();

    /// <summary>The fault message reported when a consumer draw hook throws.</summary>
    private const string CallbackFault = "A badge hook threw.";

    /// <summary>
    /// Draws a count badge on the widget that was just submitted.
    /// </summary>
    /// <remarks>Nothing is drawn for a count of zero or less.</remarks>
    /// <param name="count">The count to show.</param>
    /// <param name="style">The badge style, or <see langword="null"/> for the defaults.</param>
    public static void OnLast(int count, BadgeStyle? style = null)
        => Count(LastItemRect(), count, style);

    /// <summary>
    /// Draws a dot badge on the widget that was just submitted.
    /// </summary>
    /// <param name="shown">Whether to draw it at all.</param>
    /// <param name="style">The badge style, or <see langword="null"/> for the defaults.</param>
    public static void DotOnLast(bool shown = true, BadgeStyle? style = null)
    {
        if (shown)
            Dot(LastItemRect(), style);
    }

    /// <summary>
    /// Draws a count badge on a rectangle.
    /// </summary>
    /// <remarks>Nothing is drawn for a count of zero or less.</remarks>
    /// <param name="target">The element being marked, in screen pixels.</param>
    /// <param name="count">The count to show.</param>
    /// <param name="style">The badge style, or <see langword="null"/> for the defaults.</param>
    public static void Count(UiRect target, int count, BadgeStyle? style = null)
    {
        if (count <= 0 || !UiDraw.Available)
            return;

        var resolved = style ?? Default;
        var alpha = Alpha(resolved);
        var text = resolved.FormatCount(count);
        var textSizePx = resolved.ResolveTextSize();
        var textSize = NoireText.CalcSize(text, textSizePx);
        var bounds = Place(target, Measure(resolved, textSize), resolved);

        if (resolved.CustomDraw != null)
        {
            InvokeCustomDraw(resolved.CustomDraw, resolved, bounds, text, textSizePx, alpha, nameof(Count));
            return;
        }

        DrawPlate(bounds, resolved, alpha);

        // Centred on the plate rather than on its own line box, so the digits sit centred whatever their metrics.
        var textAt = bounds.Center - (textSize * 0.5f);
        var color = ColorHelper.Vector4ToUint(
            ColorHelper.ScaleAlpha(resolved.TextColor ?? NoireTheme.Current.Resolve(ThemeColor.Text), alpha));

        // Written straight onto the draw list: an ImGui text call would submit an item, shifting the cursor and
        // growing the row. The NoireText font scope still applies, so glyphs rasterize at this size.
        NoireText.At(textSizePx, (textAt, color, text), static state =>
        {
            using var draw = UiDraw.Begin();

            if (!draw.List.IsNull)
                draw.List.AddText(state.textAt, state.color, state.text);
        });
    }

    /// <summary>
    /// Draws a dot badge on a rectangle.
    /// </summary>
    /// <param name="target">The element being marked, in screen pixels.</param>
    /// <param name="style">The badge style, or <see langword="null"/> for the defaults.</param>
    public static void Dot(UiRect target, BadgeStyle? style = null)
    {
        if (!UiDraw.Available)
            return;

        var resolved = style ?? Default;
        var alpha = Alpha(resolved);
        var bounds = Place(target, new Vector2(resolved.Sized(resolved.DotSize)), resolved);

        if (resolved.CustomDraw != null)
        {
            InvokeCustomDraw(resolved.CustomDraw, resolved, bounds, null, resolved.ResolveTextSize(), alpha, nameof(Dot));
            return;
        }

        DrawPlate(bounds, resolved, alpha);
    }

    /// <summary>
    /// Measures the size a count badge would occupy.
    /// </summary>
    /// <param name="count">The count that would be shown.</param>
    /// <param name="style">The badge style, or <see langword="null"/> for the defaults.</param>
    /// <returns>The size in real pixels, or zero when nothing would be drawn.</returns>
    public static Vector2 CountSize(int count, BadgeStyle? style = null)
    {
        if (count <= 0 || !UiDraw.Available)
            return Vector2.Zero;

        var resolved = style ?? Default;

        return Measure(resolved, NoireText.CalcSize(resolved.FormatCount(count), resolved.ResolveTextSize()));
    }

    /// <summary>
    /// Sizes a counted badge around its text, never below the style's minimum.
    /// </summary>
    /// <param name="style">The style being drawn with.</param>
    /// <param name="textSize">The measured text, in real pixels.</param>
    /// <returns>The badge size in real pixels.</returns>
    private static Vector2 Measure(BadgeStyle style, Vector2 textSize)
    {
        var minSize = style.Sized(style.MinSize);

        return new Vector2(
            MathF.Max(minSize, textSize.X + (style.Sized(style.PaddingX) * 2f)),
            MathF.Max(minSize, textSize.Y));
    }

    /// <summary>
    /// Works out where a badge of a given size sits against the element it marks.
    /// </summary>
    /// <remarks>A badge is never nudged to fit; overflow is left to the caller's clipping.</remarks>
    /// <param name="target">The element being marked.</param>
    /// <param name="size">The size of the badge, in real pixels.</param>
    /// <param name="style">The style carrying the anchor and the offset.</param>
    /// <returns>The badge's own rectangle.</returns>
    internal static UiRect Place(UiRect target, Vector2 size, BadgeStyle style)
    {
        // The badge is centred on the anchor point, so a corner anchor straddles the corner rather than sitting
        // inside or outside it.
        var anchor = target.PointAt(style.Anchor) + (NoireUI.Scaled(style.Offset) * style.Scale);

        return new UiRect(anchor - (size * 0.5f), size);
    }

    /// <summary>
    /// Hands the painting to a custom-draw hook, with every colour resolved and the pulse applied.
    /// </summary>
    /// <param name="customDraw">The hook to run.</param>
    /// <param name="style">The style being drawn with.</param>
    /// <param name="bounds">The badge's own rectangle.</param>
    /// <param name="text">The count as it would be shown, or <see langword="null"/> for a dot badge.</param>
    /// <param name="textSizePx">The logical text size, the badge's own scale applied.</param>
    /// <param name="alpha">The pulse multiplier.</param>
    /// <param name="source">What to blame in the fault report.</param>
    private static void InvokeCustomDraw(
        Action<UiBadgeDraw> customDraw,
        BadgeStyle style,
        UiRect bounds,
        string? text,
        float textSizePx,
        float alpha,
        string source)
    {
        using var draw = UiDraw.BeginWindow();

        if (draw.List.IsNull)
            return;

        var theme = NoireTheme.Current;
        var args = new UiBadgeDraw(
            draw.List,
            bounds,
            text,
            textSizePx,
            ColorHelper.ScaleAlpha(style.Color ?? theme.Resolve(ThemeColor.Danger), alpha),
            ColorHelper.ScaleAlpha(style.OutlineColor ?? theme.Resolve(ThemeColor.Surface), alpha),
            style.Sized(style.OutlineThickness),
            ColorHelper.ScaleAlpha(style.TextColor ?? theme.Resolve(ThemeColor.Text), alpha),
            MathF.Min(bounds.Size.X, bounds.Size.Y) * 0.5f,
            alpha);

        UiHook.Invoke(customDraw, args, source, CallbackFault);
    }

    /// <summary>Draws the badge plate and its outline ring.</summary>
    /// <param name="bounds">The badge's own rectangle.</param>
    /// <param name="style">The style being drawn with.</param>
    /// <param name="alpha">The pulse multiplier.</param>
    private static void DrawPlate(UiRect bounds, BadgeStyle style, float alpha)
    {
        var theme = NoireTheme.Current;
        var color = ColorHelper.ScaleAlpha(style.Color ?? theme.Resolve(ThemeColor.Danger), alpha);

        // Half the short side, so a wide badge rounds to a pill and a square one to a circle.
        var radius = MathF.Min(bounds.Size.X, bounds.Size.Y) * 0.5f;

        using var draw = UiDraw.BeginWindow();

        NoireShapes.On(draw.List, (bounds, color, radius, style, theme, alpha), static state =>
        {
            if (state.style.OutlineThickness > 0f)
            {
                var ring = state.style.Sized(state.style.OutlineThickness);
                var outlineColor = ColorHelper.ScaleAlpha(
                    state.style.OutlineColor ?? state.theme.Resolve(ThemeColor.Surface), state.alpha);

                NoireShapes.Rect(
                    state.bounds.Position - new Vector2(ring),
                    state.bounds.Max + new Vector2(ring),
                    outlineColor,
                    CornerShape.Rounded,
                    state.radius + ring);
            }

            NoireShapes.Rect(
                state.bounds.Position, state.bounds.Max, state.color, CornerShape.Rounded, state.radius);
        });
    }

    /// <summary>Gets the alpha a badge draws at, which only varies while it pulses.</summary>
    /// <param name="style">The style being drawn with.</param>
    /// <returns>The alpha multiplier.</returns>
    private static float Alpha(BadgeStyle style)
        => style.Pulse && !NoireUI.ReducedMotion ? NoireAnim.Pulse(style.PulsePeriod, 0.55f, 1f) : 1f;

    /// <summary>Gets the rectangle of the widget just submitted.</summary>
    /// <returns>The rectangle in screen pixels.</returns>
    private static UiRect LastItemRect()
        => UiRect.FromBounds(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
}
