using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Draws tooltips as their own windows on the topmost layer, independent of the ImGui tooltip system.
/// </summary>
[NoireFacade]
public static class NoireTooltip
{
    private static readonly TooltipStyle DefaultStyle = new();

    private const string CallbackFault = "A tooltip hook threw.";

    // Keyed by reference. Ids come from UiIds as the same instance every frame.
    private static readonly Dictionary<string, (Vector2 Size, int Frame)> SizeCache = new(StringInstanceComparer.Instance);

    // ImGui does not clamp an explicitly positioned window into view. A zero alpha window is skipped and never measured.
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

    /// <summary>Shows a tooltip for the current frame when the last drawn item is hovered.</summary>
    /// <param name="content">The tooltip's content. A <see cref="string"/> converts implicitly.</param>
    /// <param name="style">The style.</param>
    /// <param name="hoveredFlags">Flags passed to <c>ImGui.IsItemHovered()</c>.</param>
    /// <param name="id">A stable id, needed only when tooltips are shown in a varying order.</param>
    public static void ShowOnItemHover(NoireContent content, TooltipStyle? style = null, ImGuiHoveredFlags hoveredFlags = ImGuiHoveredFlags.None, string? id = null)
    {
        if (ImGui.IsItemHovered(hoveredFlags))
            Show(content, style, id);
    }

    /// <summary>Shows a tooltip for the current frame. Call it every frame it stays visible.</summary>
    /// <param name="content">The tooltip's content. A <see cref="string"/> converts implicitly.</param>
    /// <param name="style">The style.</param>
    /// <param name="id">A stable id, needed only when tooltips are shown in a varying order.</param>
    public static void Show(NoireContent content, TooltipStyle? style = null, string? id = null)
    {
        if (content == null || content.IsEmpty)
            return;

        style ??= DefaultStyle;
        var windowId = id != null ? UiIds.For("###NoireTooltip_", id) : NoireUI.NextTooltipId();

        // The tooltip flag reroutes the border and background style fields. Nothing may sit between the pushes and Begin.
        using var draw = UiDraw.Begin();

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

        // An auto-resizing window only learns its size by being drawn. An unmeasured tooltip is parked off screen.
        var measured = SizeCache.TryGetValue(windowId, out var cached);
        ImGui.SetNextWindowPos(measured ? ResolveTopLeft(anchorPosition, pivot, cached.Size, style) : MeasuringPosition, ImGuiCond.Always);

        // ImGui queues the background and border at Begin.
        var custom = style.CustomDraw;

        if (custom == null && style.BackgroundOpacity.HasValue)
            ImGui.SetNextWindowBgAlpha(Math.Clamp(style.BackgroundOpacity.Value, 0f, 1f));

        using var backgroundColor = UiPush.Color(ImGuiCol.PopupBg, style.BackgroundColor ?? Vector4.One, custom == null && style.BackgroundColor.HasValue);
        using var borderColor = UiPush.Color(ImGuiCol.Border, style.BorderColor ?? Vector4.One, custom == null && style.BorderColor.HasValue);
        using var textColor = UiPush.Color(ImGuiCol.Text, style.TextColor ?? Vector4.One, style.TextColor.HasValue);

        // A tooltip's border reads PopupBorderSize and its rounding reads WindowRounding.
        using var borderSize = UiPush.Style(ImGuiStyleVar.PopupBorderSize, style.ScaledBorderSize ?? 0f, custom == null && style.BorderSize.HasValue);
        using var rounding = UiPush.Style(ImGuiStyleVar.WindowRounding, style.ScaledRounding ?? 0f, custom == null && style.Rounding.HasValue);
        using var padding = UiPush.Style(ImGuiStyleVar.WindowPadding, style.ScaledPadding ?? Vector2.Zero, style.Padding.HasValue);

        var flags = custom != null ? TooltipWindowFlags | ImGuiWindowFlags.NoBackground : TooltipWindowFlags;

        if (ImGui.Begin(windowId, flags))
        {
            // The last window to ask is in front.
            UiWindowOrder.KeepInFront();

            if (custom != null && measured)
                InvokeCustomDraw(custom, style);

            var maxWidth = style.ScaledMaxWidth;

            if (maxWidth > 0f)
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + maxWidth);

            try
            {
                content.Draw();
            }
            finally
            {
                if (maxWidth > 0f)
                    ImGui.PopTextWrapPos();
            }

            // An appearing window reports a size from content it has not measured.
            if (!ImGui.IsWindowAppearing())
                SizeCache[windowId] = (ImGui.GetWindowSize(), ImGui.GetFrameCount());
        }

        ImGui.End();

        PruneSizeCache();
    }

    private static void InvokeCustomDraw(Action<UiTooltipDraw> customDraw, TooltipStyle style)
    {
        using var draw = UiDraw.BeginWindow();

        if (draw.List.IsNull)
            return;

        var min = ImGui.GetWindowPos();
        var imStyle = ImGui.GetStyle();

        var background = style.BackgroundColor ?? imStyle.Colors[(int)ImGuiCol.PopupBg];

        if (style.BackgroundOpacity.HasValue)
            background = ColorHelper.WithAlpha(background, Math.Clamp(style.BackgroundOpacity.Value, 0f, 1f));

        var args = new UiTooltipDraw(
            draw.List,
            min,
            min + ImGui.GetWindowSize(),
            background,
            style.BorderColor ?? imStyle.Colors[(int)ImGuiCol.Border],
            style.ScaledBorderSize ?? imStyle.PopupBorderSize,
            style.ScaledRounding ?? imStyle.WindowRounding,
            style.ScaledPadding ?? imStyle.WindowPadding);

        UiHook.Invoke(customDraw, args, nameof(NoireTooltip), CallbackFault);
    }

    private static (Vector2 Position, Vector2 Pivot) ResolveAnchor(TooltipStyle style)
    {
        if (style.Placement == TooltipPlacement.Mouse)
            return (ImGui.GetMousePos() + style.ScaledMouseOffset, Vector2.Zero);

        // Must be read before Begin.
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

    private static void PruneSizeCache()
    {
        if (SizeCache.Count < 64)
            return;

        var currentFrame = ImGui.GetFrameCount();

        using var buffer = PooledBuffer<string>.Rent(SizeCache.Count);

        var stale = buffer.Span;
        var count = 0;

        foreach (var (key, value) in SizeCache)
        {
            if (currentFrame - value.Frame > 60)
                stale[count++] = key;
        }

        for (var index = 0; index < count; index++)
            SizeCache.Remove(stale[index]);
    }
}
