using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Draws custom tooltips that are independent from the regular ImGui tooltip system.<br/>
/// A custom tooltip is rendered as its own window on the topmost display layer, so it can be shown
/// <b>at the same time</b> as a regular <c>ImGui.SetTooltip()</c>, and can contain any mix of text, icons and images (see <see cref="TooltipContent"/>).<br/>
/// The background transparency is fully customizable, from 0% to 100%. See <see cref="TooltipStyle"/>.
/// </summary>
public static class NoireTooltip
{
    private static readonly TooltipStyle DefaultStyle = new();
    private static readonly Dictionary<string, (Vector2 Size, int Frame)> SizeCache = new();

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
    /// <param name="id">Optional stable id. Only needed when tooltips are shown in a varying order and <see cref="TooltipStyle.ClampToViewport"/> matters.</param>
    public static void ShowOnItemHover(TooltipContent content, TooltipStyle? style = null, ImGuiHoveredFlags hoveredFlags = ImGuiHoveredFlags.None, string? id = null)
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
    /// <param name="id">Optional stable id. Only needed when tooltips are shown in a varying order and <see cref="TooltipStyle.ClampToViewport"/> matters.</param>
    public static void Show(TooltipContent content, TooltipStyle? style = null, string? id = null)
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

    private static void DrawTooltipWindow(string windowId, TooltipContent content, TooltipStyle style)
    {
        var (anchorPosition, pivot) = ResolveAnchor(style);
        SetupNextWindowPosition(windowId, anchorPosition, pivot, style);

        if (style.BackgroundOpacity.HasValue)
            ImGui.SetNextWindowBgAlpha(Math.Clamp(style.BackgroundOpacity.Value, 0f, 1f));

        using var backgroundColor = ImRaii.PushColor(ImGuiCol.PopupBg, style.BackgroundColor ?? Vector4.One, style.BackgroundColor.HasValue);
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, style.BorderColor ?? Vector4.One, style.BorderColor.HasValue);
        using var textColor = ImRaii.PushColor(ImGuiCol.Text, style.TextColor ?? Vector4.One, style.TextColor.HasValue);

        using var borderSize = ImRaii.PushStyle(ImGuiStyleVar.WindowBorderSize, style.BorderSize ?? 0f, style.BorderSize.HasValue);
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, style.Rounding ?? 0f, style.Rounding.HasValue);
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, style.Padding ?? Vector2.Zero, style.Padding.HasValue);

        if (ImGui.Begin(windowId, TooltipWindowFlags))
        {
            content.Draw();
            SizeCache[windowId] = (ImGui.GetWindowSize(), ImGui.GetFrameCount());
        }

        ImGui.End();

        PruneSizeCache();
    }

    private static (Vector2 Position, Vector2 Pivot) ResolveAnchor(TooltipStyle style)
    {
        if (style.Placement == TooltipPlacement.Mouse)
            return (ImGui.GetMousePos() + style.MouseOffset, Vector2.Zero);

        // Item-relative placements use the rect of the last drawn ImGui item,
        // so they must be resolved before beginning the tooltip window.
        var itemMin = ImGui.GetItemRectMin();
        var itemMax = ImGui.GetItemRectMax();
        var itemCenter = (itemMin + itemMax) / 2f;

        return style.Placement switch
        {
            TooltipPlacement.AboveItem => (new Vector2(itemCenter.X, itemMin.Y - style.ItemGap), new Vector2(0.5f, 1f)),
            TooltipPlacement.BelowItem => (new Vector2(itemCenter.X, itemMax.Y + style.ItemGap), new Vector2(0.5f, 0f)),
            TooltipPlacement.LeftOfItem => (new Vector2(itemMin.X - style.ItemGap, itemCenter.Y), new Vector2(1f, 0.5f)),
            TooltipPlacement.RightOfItem => (new Vector2(itemMax.X + style.ItemGap, itemCenter.Y), new Vector2(0f, 0.5f)),
            _ => (ImGui.GetMousePos() + style.MouseOffset, Vector2.Zero),
        };
    }

    private static void SetupNextWindowPosition(string windowId, Vector2 anchorPosition, Vector2 pivot, TooltipStyle style)
    {
        // Clamping needs the window size, which is only known from the previous frame (the window auto-resizes).
        if (style.ClampToViewport && SizeCache.TryGetValue(windowId, out var cached) && ImGui.GetFrameCount() - cached.Frame <= 2)
        {
            var viewport = ImGui.GetMainViewport();
            var topLeft = anchorPosition - (pivot * cached.Size);
            var max = viewport.Pos + viewport.Size - cached.Size;

            topLeft = new Vector2(
                MathF.Max(viewport.Pos.X, MathF.Min(topLeft.X, max.X)),
                MathF.Max(viewport.Pos.Y, MathF.Min(topLeft.Y, max.Y)));

            ImGui.SetNextWindowPos(topLeft, ImGuiCond.Always);
        }
        else
        {
            ImGui.SetNextWindowPos(anchorPosition, ImGuiCond.Always, pivot);
        }
    }

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
