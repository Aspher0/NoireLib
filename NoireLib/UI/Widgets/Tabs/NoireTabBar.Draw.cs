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

    /// <summary>
    /// Scrolls the tab strip with the wheel while the pointer is over it, and keeps the surrounding windows from
    /// scrolling with the same notch.
    /// </summary>
    /// <remarks>
    /// The tab bar's scroll position lives in ImGui's internals rather than in its public surface, so this reaches it
    /// through <c>ImGui.GetCurrentContext().CurrentTabBar</c>. That is only valid between <c>BeginTabBar</c> and
    /// <c>EndTabBar</c>, which is why this is not a separate call the caller makes.<br/>
    /// Setting the target rather than the animated position leaves ImGui's own easing in charge, so a wheel notch
    /// glides the way clicking the arrows does instead of jumping.
    /// </remarks>
    /// <returns>True when the strip took the wheel.</returns>
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

    /// <summary>
    /// Pulls the window's work rectangle in to the width the bar is allowed, so ImGui builds the strip to that edge
    /// rather than to the window's own.
    /// </summary>
    /// <remarks>
    /// Only ever narrows. A bar asked for more room than the window has cannot be given it, and widening the work
    /// rectangle would push the strip out through the side of the window instead.
    /// </remarks>
    /// <param name="window">The window being drawn into.</param>
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

    /// <summary>
    /// Tells ImGui that no window under the pointer may scroll on the wheel, for as long as the pointer is over the
    /// tab strip.
    /// </summary>
    /// <remarks>
    /// The wheel has to be refused rather than undone. ImGui hands it to the hovered window inside <c>NewFrame</c>,
    /// before a single widget has drawn, so by the time a tab bar could notice, the scrolling has already happened;
    /// and this build of the bindings offers neither <c>SetItemUsingMouseWheel</c> nor a key-owner API to claim it in
    /// advance. Putting the scroll back afterwards is not equivalent either, because the window that moved is often not
    /// the one the bar is drawn in: ImGui walks up from the hovered window to the first ancestor that can actually
    /// scroll, which for a bar inside a non-scrolling column is the page behind it.<br/>
    /// So the flag is set on the whole ancestor chain, which is what makes that same walk find nothing willing to
    /// scroll. It is set a frame ahead, which costs nothing in practice: the pointer rests on the strip for many frames
    /// before a wheel notch arrives.<br/>
    /// Nothing is restored, and nothing needs to be. <c>Begin</c> assigns a window's flags from its own arguments every
    /// frame, so this lasts exactly until the window is next begun and then undoes itself.
    /// </remarks>
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
        // rectangle and nothing in its public surface takes a width. That is the usual right-edge trap: a bar inside a
        // page that centres its content in a narrower column runs straight past the column and out the other side.
        var window = ImGuiP.GetCurrentWindow();
        var workRect = window.WorkRect;
        ConstrainWorkRect(window);

        if (!ImGui.BeginTabBar($"###NoireTabBar_{Id}", flags))
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

        // Cleared after exactly one frame of being applied, whatever happened above. This single line is most of what
        // the widget is for: held any longer and the tab is welded open with the user unable to click away.
        pendingTab = null;

        if (closed != null)
            CloseTab(closed);

        return ApplyOpened(opened);
    }

    /// <summary>
    /// Draws one tab header, its badge and its body.
    /// </summary>
    /// <param name="tab">The tab to draw.</param>
    /// <param name="opened">Receives this tab's id when it is the open one.</param>
    /// <param name="closed">Receives this tab when its close button was used.</param>
    private void DrawTab(UiTab tab, ref string? opened, ref UiTab? closed)
    {
        var enabled = tab.IsEnabled();
        var itemFlags = ImGuiTabItemFlags.None;

        if (string.Equals(pendingTab, tab.Id, StringComparison.Ordinal))
            itemFlags |= ImGuiTabItemFlags.SetSelected;

        // The label carries the id after a triple hash, so ImGui keys the tab on something stable while the caller is
        // free to change what is written on it, including its length, every frame.
        var label = $"{tab.Label}###NoireTab_{Id}_{tab.Id}";
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

    /// <summary>
    /// Draws the tab's badge over its header, if it has one to draw.
    /// </summary>
    /// <remarks>
    /// Clipped to the ends of the bar rather than pushed back inside them. A badge belongs to its tab, so it should
    /// leave with it: a tab scrolled halfway off the end has half a badge, and one scrolled off entirely has none,
    /// which is what the tab itself does. Holding the badge inside instead leaves it stranded at the edge, still
    /// showing a count for a tab that is no longer there.<br/>
    /// Only the ends are clipped. A badge deliberately rides above the top of its tab, so bounding it vertically as
    /// well would shave the top off every badge in the bar rather than only the ones going out of view.
    /// </remarks>
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

    /// <summary>
    /// Shows whichever of the tooltip and the disabled reason applies.
    /// </summary>
    /// <remarks>
    /// The reason wins while the tab is disabled, because a control that is dead for no stated reason reads as broken
    /// and that is the question the user is asking at exactly that moment.
    /// </remarks>
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

    /// <summary>
    /// Runs the open tab's body, keeping a throwing body from taking the bar and every other tab down with it.
    /// </summary>
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

    /// <summary>
    /// Removes a closed tab and reports it, after the bar has ended so the list is not edited mid-draw.
    /// </summary>
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

    /// <summary>
    /// Records which tab ImGui actually drew as open, and reports the change once.
    /// </summary>
    /// <param name="opened">The tab drawn open this frame, if any.</param>
    /// <returns>True when it differs from the tab open before.</returns>
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
