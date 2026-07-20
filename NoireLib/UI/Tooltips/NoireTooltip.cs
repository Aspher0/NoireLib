using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Draws custom tooltips that are independent from the regular ImGui tooltip system.<br/>
/// A custom tooltip is rendered as its own window on the topmost display layer, so it can be shown
/// <b>at the same time</b> as a regular <c>ImGui.SetTooltip()</c>, and can contain any mix of text, icons and images (see <see cref="NoireContent"/>).<br/>
/// The background transparency is fully customizable, from 0% to 100%. See <see cref="TooltipStyle"/>.
/// </summary>
public static class NoireTooltip
{
    private static readonly TooltipStyle DefaultStyle = new();
    private static readonly Dictionary<string, (Vector2 Size, int Frame)> SizeCache = new();

    /// <summary>
    /// Where a tooltip is parked for the frame or two it takes to measure it, before its real position can be worked out.<br/>
    /// Far outside any viewport, and ImGui leaves a window given an explicit position exactly where it is put rather than
    /// clamping it back into view, so the tooltip is measured in full while never being seen. Hiding it by drawing it at
    /// zero alpha instead would not work: ImGui refuses to process a window whose style alpha is zero when it begins, so
    /// the size it is being hidden to discover would never be measured at all.
    /// </summary>
    private static readonly Vector2 MeasuringPosition = new(-10000f, -10000f);

    private const ImGuiWindowFlags TooltipWindowFlags =
        ImGuiWindowFlags.Tooltip |
        ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoResize |
        ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoScrollWithMouse |
        ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.AlwaysAutoResize |
        ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoInputs |
        ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoNav;

    /// <summary>
    /// Shows a custom tooltip for the current frame if the last drawn ImGui item is hovered.<br/>
    /// Call this right after drawing the item, like you would call <c>ImGui.SetTooltip()</c>.
    /// </summary>
    /// <param name="content">The content of the tooltip. A plain <see cref="string"/> is implicitly converted.</param>
    /// <param name="style">Optional visual and placement options.</param>
    /// <param name="hoveredFlags">Optional hover detection flags passed to <c>ImGui.IsItemHovered()</c>.</param>
    /// <param name="id">
    /// Optional stable id. Only needed when tooltips are shown in a varying order: an id left <see langword="null"/> is
    /// assigned from the order tooltips are shown in each frame, and a tooltip is placed and clamped against the size
    /// remembered under its id, so an id landing on a differently sized tooltip from one frame to the next misplaces it
    /// until it is measured again.
    /// </param>
    public static void ShowOnItemHover(NoireContent content, TooltipStyle? style = null, ImGuiHoveredFlags hoveredFlags = ImGuiHoveredFlags.None, string? id = null)
    {
        if (ImGui.IsItemHovered(hoveredFlags))
            Show(content, style, id);
    }

    /// <summary>
    /// Shows a custom tooltip for the current frame, unconditionally.<br/>
    /// Call this every frame the tooltip should stay visible.
    /// </summary>
    /// <param name="content">The content of the tooltip. A plain <see cref="string"/> is implicitly converted.</param>
    /// <param name="style">Optional visual and placement options.</param>
    /// <param name="id">
    /// Optional stable id. Only needed when tooltips are shown in a varying order: an id left <see langword="null"/> is
    /// assigned from the order tooltips are shown in each frame, and a tooltip is placed and clamped against the size
    /// remembered under its id, so an id landing on a differently sized tooltip from one frame to the next misplaces it
    /// until it is measured again.
    /// </param>
    public static void Show(NoireContent content, TooltipStyle? style = null, string? id = null)
    {
        if (content == null || content.IsEmpty)
            return;

        style ??= DefaultStyle;
        var windowId = id != null ? $"###NoireTooltip_{id}" : NoireUI.NextTooltipId();

        try
        {
            DrawTooltipWindow(windowId, content, style);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to draw custom tooltip '{windowId}'.", "NoireTooltip");
        }
    }

    private static void DrawTooltipWindow(string windowId, NoireContent content, TooltipStyle style)
    {
        var (anchorPosition, pivot) = ResolveAnchor(style);

        // Placing a tooltip means resolving it against its own size, and an auto-resizing ImGui window only learns its
        // size by being drawn once. Until that has happened there is nowhere sensible to put this one, so it is parked
        // off screen for the frame or two that measuring takes. Showing it anyway would put it somewhere wrong first and
        // visibly move it into place after.
        var measured = SizeCache.TryGetValue(windowId, out var cached);
        ImGui.SetNextWindowPos(measured ? ResolveTopLeft(anchorPosition, pivot, cached.Size, style) : MeasuringPosition, ImGuiCond.Always);

        if (style.BackgroundOpacity.HasValue)
            ImGui.SetNextWindowBgAlpha(Math.Clamp(style.BackgroundOpacity.Value, 0f, 1f));

        using var backgroundColor = ImRaii.PushColor(ImGuiCol.PopupBg, style.BackgroundColor ?? Vector4.One, style.BackgroundColor.HasValue);
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, style.BorderColor ?? Vector4.One, style.BorderColor.HasValue);
        using var textColor = ImRaii.PushColor(ImGuiCol.Text, style.TextColor ?? Vector4.One, style.TextColor.HasValue);

        // A tooltip's border thickness comes from PopupBorderSize, not from WindowBorderSize: ImGui picks the style
        // field by flag, and this window carries the tooltip flag. Pushing the window one is what made a styled border
        // never appear, whatever the style asked for.
        // Rounding is picked by flag as well, but there the tooltip flag is not part of the popup branch, so that one
        // genuinely is the window field.
        using var borderSize = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, style.ScaledBorderSize ?? 0f, style.BorderSize.HasValue);
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, style.ScaledRounding ?? 0f, style.Rounding.HasValue);
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, style.ScaledPadding ?? Vector2.Zero, style.Padding.HasValue);

        if (ImGui.Begin(windowId, TooltipWindowFlags))
        {
            // A tooltip shares the top draw layer with any always-on-top element it might overlap, and within a layer
            // the last window to ask is the one in front. Asking here, after the thing being annotated has drawn, is
            // what keeps the tooltip above it rather than behind.
            UiWindowOrder.KeepInFront();

            content.Draw();

            // The size an appearing window reports is derived from a content size it has not measured yet, so it is
            // recorded only from the second frame on, once it describes the content actually inside.
            if (!ImGui.IsWindowAppearing())
                SizeCache[windowId] = (ImGui.GetWindowSize(), ImGui.GetFrameCount());
        }

        ImGui.End();

        PruneSizeCache();
    }

    /// <summary>
    /// Resolves the anchor point the tooltip hangs from, and which of its own corners hangs there.
    /// </summary>
    /// <param name="style">The style carrying the placement, the gap and the offsets.</param>
    /// <returns>The anchor position in screen coordinates, and the normalized pivot of the tooltip pinned to it.</returns>
    private static (Vector2 Position, Vector2 Pivot) ResolveAnchor(TooltipStyle style)
    {
        if (style.Placement == TooltipPlacement.Mouse)
            return (ImGui.GetMousePos() + style.ScaledMouseOffset, Vector2.Zero);

        // Item-relative placements use the rect of the last drawn ImGui item,
        // so they must be resolved before beginning the tooltip window.
        var itemMin = ImGui.GetItemRectMin();
        var itemMax = ImGui.GetItemRectMax();
        var itemCenter = (itemMin + itemMax) / 2f;

        var (position, pivot) = style.Placement switch
        {
            TooltipPlacement.AboveItem => (new Vector2(itemCenter.X, itemMin.Y - style.ScaledItemGap), new Vector2(0.5f, 1f)),
            TooltipPlacement.BelowItem => (new Vector2(itemCenter.X, itemMax.Y + style.ScaledItemGap), new Vector2(0.5f, 0f)),
            TooltipPlacement.LeftOfItem => (new Vector2(itemMin.X - style.ScaledItemGap, itemCenter.Y), new Vector2(1f, 0.5f)),
            TooltipPlacement.RightOfItem => (new Vector2(itemMax.X + style.ScaledItemGap, itemCenter.Y), new Vector2(0f, 0.5f)),
            _ => (itemCenter, new Vector2(0.5f, 0.5f)),
        };

        return (position + style.ScaledItemOffset, pivot);
    }

    /// <summary>
    /// Turns an anchor and a pivot into the top left corner of a tooltip of the given size, clamping it into the viewport
    /// when the style asks for it.
    /// </summary>
    /// <remarks>
    /// The pivot is applied here rather than handed to <c>ImGui.SetNextWindowPos</c>, which takes one: ImGui defers a
    /// non-zero pivot until it knows the window size, and an auto-resizing window does not know its size on the frame it
    /// appears. The tooltip would spawn at the raw anchor and visibly settle into place on the next frame, which is why
    /// only the item-relative placements ever showed it (the mouse placement pivots on its top left corner, so there is
    /// nothing to defer). Resolving it against the size the tooltip had the last time it was drawn places it correctly on
    /// the first frame instead.
    /// </remarks>
    /// <param name="anchorPosition">The anchor position in screen coordinates.</param>
    /// <param name="pivot">The normalized point of the tooltip pinned to the anchor.</param>
    /// <param name="size">The size of the tooltip.</param>
    /// <param name="style">The style carrying the clamping preference.</param>
    /// <returns>The top left corner of the tooltip in screen coordinates.</returns>
    private static Vector2 ResolveTopLeft(Vector2 anchorPosition, Vector2 pivot, Vector2 size, TooltipStyle style)
    {
        var topLeft = anchorPosition - (pivot * size);

        if (!style.ClampToViewport)
            return topLeft;

        var viewport = ImGui.GetMainViewport();
        var max = viewport.Pos + viewport.Size - size;

        return new Vector2(
            MathF.Max(viewport.Pos.X, MathF.Min(topLeft.X, max.X)),
            MathF.Max(viewport.Pos.Y, MathF.Min(topLeft.Y, max.Y)));
    }


    /// <summary>
    /// Drops remembered sizes of tooltips that have not been drawn for a while, once enough of them have piled up to be
    /// worth bounding.<br/>
    /// A remembered size is what places a tooltip on the frame it reappears, so one is worth keeping long after the
    /// tooltip was last shown, and evicting one only costs the couple of invisible frames it takes to measure it again.
    /// </summary>
    private static void PruneSizeCache()
    {
        if (SizeCache.Count < 64)
            return;

        var currentFrame = ImGui.GetFrameCount();
        var stale = new List<string>();

        foreach (var (key, value) in SizeCache)
        {
            if (currentFrame - value.Frame > 60)
                stale.Add(key);
        }

        foreach (var key in stale)
            SizeCache.Remove(key);
    }
}
