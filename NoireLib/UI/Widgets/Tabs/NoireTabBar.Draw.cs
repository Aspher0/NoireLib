using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The drawing half of <see cref="NoireTabBar"/>.
/// </summary>
public sealed partial class NoireTabBar
{
    private readonly List<UiTab> drawOrder = [];

    private bool HandleWheelScroll()
    {
        if (!WheelScrolls)
            return false;

        var bar = ImGui.GetCurrentContext().CurrentTabBar;

        if (bar.IsNull)
            return false;

        var barRect = UiRect.FromBounds(bar.BarRect.Min, bar.BarRect.Max);
        var travel = bar.WidthAllTabs - barRect.Size.X;

        // Nothing has scrolled off, so the wheel was never meant for the strip and the windows around it keep it.
        if (travel <= 0f)
            return false;

        if (!ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) || !barRect.Contains(ImGui.GetMousePos()))
            return false;

        ClaimWheelForNextFrame();

        var wheel = ImGui.GetIO().MouseWheel;

        if (wheel == 0f)
            return false;

        // Measured from where the strip is actually resting rather than from the target, so a notch during the glide
        // moves one notch further on from what is on screen instead of from wherever the animation was heading.
        var from = wheel > 0f
            ? MathF.Min(bar.ScrollingAnim, bar.ScrollingTarget)
            : MathF.Max(bar.ScrollingAnim, bar.ScrollingTarget);

        bar.ScrollingTarget = Math.Clamp(from - (wheel * NoireUI.Scaled(WheelScrollStep)), 0f, travel);
        return true;
    }

    // Pulls the window's work rectangle in to the width the bar is allowed, so ImGui builds the strip to that edge
    // rather than to the window's own.
    private void ConstrainWorkRect(ImGuiWindowPtr window)
    {
        if (window.IsNull)
            return;

        var width = Width > 0f ? NoireUI.Scaled(Width) : NoireLayout.ContentWidth();

        if (width <= 0f)
            return;

        var right = ImGui.GetCursorScreenPos().X + width;
        var rect = window.WorkRect;

        if (right >= rect.Max.X)
            return;

        rect.Max = new Vector2(right, rect.Max.Y);
        window.WorkRect = rect;
    }

    private static void ClaimWheelForNextFrame()
    {
        for (var window = ImGuiP.GetCurrentWindow(); !window.IsNull; window = window.ParentWindow)
            window.Flags |= ImGuiWindowFlags.NoScrollWithMouse;
    }

    /// <summary>
    /// Draws the bar and the body of whichever tab is open.
    /// </summary>
    /// <returns>True when the open tab changed this frame.</returns>
    public bool Draw()
    {
        if (!NoireService.IsInitialized())
            return false;

        using var profile = UiProfile.Widget(nameof(NoireTabBar), Id);

        NoireUI.EnsureFrameServices();

        if (Tabs.Count == 0)
        {
            pendingTab = null;
            EmptyState?.Invoke();
            return false;
        }

        var flags = ImGuiTabBarFlags.None;

        if (Reorderable)
            flags |= ImGuiTabBarFlags.Reorderable;

        if (ScrollWhenCrowded)
            flags |= ImGuiTabBarFlags.FittingPolicyScroll;

        // Narrowed around the bar and put back after it, because ImGui builds a tab bar out to the window's work
        // rectangle and nothing in its public surface takes a width.
        var window = ImGuiP.GetCurrentWindow();
        var workRect = window.WorkRect;
        ConstrainWorkRect(window);

        if (!ImGui.BeginTabBar(UiIds.For("###NoireTabBar_", Id), flags))
        {
            window.WorkRect = workRect;

            // The bar was not begun, so no tab item will run and the pending request has not been applied. Kept rather
            // than cleared: a switch asked for while the bar is clipped or its window collapsed is meant to take effect
            // when it draws again, not to be quietly lost.
            return false;
        }

        // Iterated over a snapshot because a body, a badge delegate or a close can add to or remove from Tabs while
        // the loop is running, and a collection modified during a foreach throws rather than doing anything useful.
        drawOrder.Clear();
        drawOrder.AddRange(Tabs);

        UiTab? closed = null;
        string? opened = null;

        foreach (var tab in drawOrder)
            DrawTab(tab, ref opened, ref closed);

        // Handled after the tabs and before the bar ends. It has to be inside the bar, because that is the only time
        // ImGui will hand its scroll state over, and it has to be after the tabs, because ImGui lays a tab bar out
        // lazily on the first tab item rather than in BeginTabBar: read any earlier and the width of all the tabs is
        // last frame's (zero on the first) and the bar rectangle has not yet been narrowed by the scroll arrows.
        HandleWheelScroll();

        ImGui.EndTabBar();
        window.WorkRect = workRect;

        // Cleared after exactly one frame of being applied, whatever happened above: held any longer and the tab
        // is welded open with the user unable to click away.
        pendingTab = null;

        if (closed != null)
            CloseTab(closed);

        return ApplyOpened(opened);
    }

    private void DrawTab(UiTab tab, ref string? opened, ref UiTab? closed)
    {
        var enabled = tab.IsEnabled();
        var itemFlags = ImGuiTabItemFlags.None;

        if (string.Equals(pendingTab, tab.Id, StringComparison.Ordinal))
            itemFlags |= ImGuiTabItemFlags.SetSelected;

        // The label carries the id after a triple hash, so ImGui keys the tab on something stable while the caller is
        // free to change what is written on it, including its length, every frame.
        var label = UiIds.Labelled(tab.Label, "###NoireTab_", Id, tab.Id);
        var open = true;

        if (!enabled)
            ImGui.BeginDisabled();

        var selected = tab.Closeable
            ? ImGui.BeginTabItem(label, ref open, itemFlags)
            : ImGui.BeginTabItem(label, itemFlags);

        // Read while the header is still the last item, and before the disabled scope ends, because everything below
        // either measures it or answers for it.
        var header = UiRect.FromBounds(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);

        if (!enabled)
            ImGui.EndDisabled();

        DrawBadge(tab, header);
        DrawTabTooltip(tab, enabled, hovered);

        if (selected)
        {
            opened = tab.Id;
            DrawBody(tab);
            ImGui.EndTabItem();
        }

        if (!open)
            closed = tab;
    }

    private static void DrawBadge(UiTab tab, UiRect header)
    {
        var count = tab.BadgeCount();

        if (count <= 0 || header.IsEmpty)
            return;

        var bar = ImGui.GetCurrentContext().CurrentTabBar;

        if (bar.IsNull)
        {
            NoireBadge.Count(header, count, tab.BadgeStyle);
            return;
        }

        var overhang = NoireBadge.CountSize(count, tab.BadgeStyle).Y;

        ImGui.PushClipRect(
            new Vector2(bar.BarRect.Min.X, bar.BarRect.Min.Y - overhang),
            new Vector2(bar.BarRect.Max.X, bar.BarRect.Max.Y + overhang),
            true);

        try
        {
            NoireBadge.Count(header, count, tab.BadgeStyle);
        }
        finally
        {
            ImGui.PopClipRect();
        }
    }

    // The disabled reason wins while the tab is disabled.
    private static void DrawTabTooltip(UiTab tab, bool enabled, bool hovered)
    {
        if (!hovered)
            return;

        if (!enabled && !string.IsNullOrEmpty(tab.DisabledReason))
        {
            NoireTooltip.Show(tab.DisabledReason);
            return;
        }

        if (!string.IsNullOrEmpty(tab.Tooltip))
            NoireTooltip.Show(tab.Tooltip);
    }

    private static void DrawBody(UiTab tab)
    {
        if (tab.Body == null)
            return;

        try
        {
            tab.Body();
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(nameof(NoireTabBar), $"The body of tab '{tab.Id}' threw.", ex);
        }
    }

    // Called after the bar has ended so the list is not edited mid-draw.
    private void CloseTab(UiTab tab)
    {
        Tabs.Remove(tab);

        // Dropped along with the tab, so an id that is added again later is not silently refused on the strength of a
        // warning about the tab that used to hold it.
        refusalsLogged.Remove(tab.Id);

        if (string.Equals(Current, tab.Id, StringComparison.Ordinal))
            Current = null;

        try
        {
            OnTabClosed?.Invoke(tab);
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(nameof(NoireTabBar), $"An {nameof(OnTabClosed)} handler threw.", ex);
        }
    }

    private bool ApplyOpened(string? opened)
    {
        if (opened == null || string.Equals(Current, opened, StringComparison.Ordinal))
            return false;

        Current = opened;

        try
        {
            OnTabChanged?.Invoke(opened);
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(nameof(NoireTabBar), $"An {nameof(OnTabChanged)} handler threw.", ex);
        }

        return true;
    }
}
