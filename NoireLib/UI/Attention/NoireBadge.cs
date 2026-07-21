using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Count and dot badges: the small mark that says a thing has something waiting on it.
/// </summary>
/// <remarks>
/// Immediate and stateless. A badge is drawn over a rectangle you already have, so it composes with anything rather
/// than wrapping it: the element draws itself normally, and the badge goes on top afterwards.<br/>
/// <see cref="OnLast(int, BadgeStyle)"/> is the everyday call, because the rectangle is nearly always the widget just
/// submitted.<br/>
/// Nothing here submits an ImGui item or moves the cursor, so a badge never shifts what is around it. That is the
/// whole reason it can be dropped after any widget, a tab header included.
/// </remarks>
/// <example>
/// <code>
/// ImGui.Button("Inbox");
/// NoireBadge.OnLast(unread);                       // a count in the corner
///
/// ImGui.Button("Settings");
/// NoireBadge.DotOnLast(hasChanges);                // just a dot
///
/// NoireBadge.Count(rect, 12, new BadgeStyle { Pulse = true });
/// </code>
/// </example>
[NoireFacade]
public static class NoireBadge
{
    private static readonly BadgeStyle Default = new();

    /// <summary>
    /// Draws a count badge on the widget that was just submitted.
    /// </summary>
    /// <remarks>Nothing is drawn for a count of zero or less, so this can be called unconditionally.</remarks>
    /// <param name="count">The count to show.</param>
    /// <param name="style">How it looks. When <see langword="null"/>, the shipped defaults.</param>
    public static void OnLast(int count, BadgeStyle? style = null)
        => Count(LastItemRect(), count, style);

    /// <summary>
    /// Draws a dot badge on the widget that was just submitted.
    /// </summary>
    /// <param name="shown">Whether to draw it at all, so this can be called unconditionally.</param>
    /// <param name="style">How it looks. When <see langword="null"/>, the shipped defaults.</param>
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
    /// <param name="style">How it looks. When <see langword="null"/>, the shipped defaults.</param>
    public static void Count(UiRect target, int count, BadgeStyle? style = null)
    {
        if (count <= 0 || !NoireService.IsInitialized())
            return;

        var resolved = style ?? Default;
        var alpha = Alpha(resolved);
        var text = resolved.FormatCount(count);
        var textSizePx = resolved.ResolveTextSize();
        var textSize = NoireText.CalcSize(text, textSizePx);
        var bounds = Place(target, Measure(resolved, textSize), resolved);

        DrawPlate(bounds, resolved, alpha);

        // Centred on the plate rather than on its own line box, so a badge reads centred whichever digits are in it.
        var textAt = bounds.Center - (textSize * 0.5f);
        var color = ColorHelper.Vector4ToUint(
            ColorHelper.ScaleAlpha(resolved.TextColor ?? NoireTheme.Current.Resolve(ThemeColor.Text), alpha));

        // Written straight onto the draw list rather than through a text call, because a badge must add nothing to the
        // layout it is drawn over. An ImGui text call submits an item: it advances the cursor and grows the current
        // line's bounding box, so the widgets after it on the row shift across and the row itself changes height. The
        // font scope is still NoireText's, so the glyphs are rasterized at this size rather than resampled.
        NoireText.At(textSizePx, (textAt, color, text), static state =>
        {
            using var draw = UiDraw.Begin();

            if (!draw.List.IsNull)
                draw.List.AddText(state.textAt, state.color, state.text);
        });
    }

    /// <summary>
    /// Draws a dot badge on a rectangle, for "something changed" where the number would mean nothing.
    /// </summary>
    /// <param name="target">The element being marked, in screen pixels.</param>
    /// <param name="style">How it looks. When <see langword="null"/>, the shipped defaults.</param>
    public static void Dot(UiRect target, BadgeStyle? style = null)
    {
        if (!NoireService.IsInitialized())
            return;

        var resolved = style ?? Default;
        var size = new Vector2(resolved.Sized(resolved.DotSize));

        DrawPlate(Place(target, size, resolved), resolved, Alpha(resolved));
    }

    /// <summary>
    /// The size a count badge would occupy, for a caller reserving room for one rather than laying it over something.
    /// </summary>
    /// <param name="count">The count that would be shown.</param>
    /// <param name="style">How it would look. When <see langword="null"/>, the shipped defaults.</param>
    /// <returns>The size in real pixels, or zero when nothing would be drawn.</returns>
    public static Vector2 CountSize(int count, BadgeStyle? style = null)
    {
        if (count <= 0 || !NoireService.IsInitialized())
            return Vector2.Zero;

        var resolved = style ?? Default;

        return Measure(resolved, NoireText.CalcSize(resolved.FormatCount(count), resolved.ResolveTextSize()));
    }

    /// <summary>
    /// The size a counted badge occupies around its text: wide enough for the text and its padding, never smaller than
    /// the minimum that keeps a single digit a circle rather than a narrow slot.
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
    /// <remarks>
    /// Separated from the drawing because it is the part worth being certain about, and the only part that can be
    /// checked without an ImGui context.
    /// </remarks>
    /// <param name="target">The element being marked.</param>
    /// <param name="size">The size of the badge, in real pixels.</param>
    /// <param name="style">The style carrying the anchor and the offset.</param>
    /// <returns>The badge's own rectangle.</returns>
    /// <remarks>
    /// A badge is never moved to fit anywhere. Somewhere it may not overflow, the caller clips instead, so a badge on
    /// an element going out of view leaves with it rather than being stranded at the edge still showing a count.
    /// </remarks>
    internal static UiRect Place(UiRect target, Vector2 size, BadgeStyle style)
    {
        // The anchor picks a point on the element and the badge is centred on it, so a corner anchor straddles the
        // corner rather than sitting inside or outside it. That is what makes one badge style look right on a small
        // icon and on a wide button both.
        var anchor = target.PointAt(style.Anchor) + (NoireUI.Scaled(style.Offset) * style.Scale);

        return new UiRect(anchor - (size * 0.5f), size);
    }

    /// <summary>Draws the badge's own shape: the pill, and the ring that separates it from what is behind it.</summary>
    private static void DrawPlate(UiRect bounds, BadgeStyle style, float alpha)
    {
        var theme = NoireTheme.Current;
        var color = ColorHelper.ScaleAlpha(style.Color ?? theme.Resolve(ThemeColor.Danger), alpha);

        // Half the short side, so a wide badge is a pill and a square one is a circle without the caller choosing.
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

    /// <summary>The alpha a badge draws at, which only moves when it was asked to pulse.</summary>
    private static float Alpha(BadgeStyle style)
        => style.Pulse && !NoireUI.ReducedMotion ? NoireAnim.Pulse(style.PulsePeriod, 0.55f, 1f) : 1f;

    /// <summary>The rectangle of the widget just submitted.</summary>
    private static UiRect LastItemRect()
        => UiRect.FromBounds(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
}
