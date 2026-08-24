using NoireLib.Helpers;
using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// A tab bar you can open from code. <c>tabs.SwitchTab("filters")</c> works from another window, a hotkey, a command
/// or a toast action, and each tab carries its own body so nothing is drawn for the ones that are closed.
/// </summary>
/// <example>
/// <code>
/// var tabs = new NoireTabBar("Settings")
/// {
///     Tabs =
///     {
///         new UiTab("general", "General", () =&gt; DrawGeneral()),
///         new UiTab("filters", "Filters", () =&gt; DrawFilters()) { Badge = () =&gt; activeFilters },
///         new UiTab("about",   "About",   () =&gt; DrawAbout())   { Enabled = () =&gt; hasData },
///     },
///     OnTabChanged = id =&gt; NoireLogger.LogInformation($"now on {id}"),
/// };
///
/// tabs.Draw();
/// tabs.SwitchTab("filters");
/// </code>
/// </example>
[NoireFacadeFactory]
public sealed partial class NoireTabBar
{
    private readonly HashSet<string> refusalsLogged = [];

    private string? pendingTab;

    /// <summary>
    /// Creates a tab bar.
    /// </summary>
    /// <param name="id">A stable id for the widget. When <see langword="null"/>, a random one is generated.</param>
    public NoireTabBar(string? id = null)
        => Id = string.IsNullOrWhiteSpace(id) ? RandomGenerator.GenerateGuidString() : id;

    /// <summary>The unique identifier of this widget, used for the ImGui ids.</summary>
    public string Id { get; }

    /// <summary>
    /// The tabs, in the order they are drawn. Add to it, remove from it, or replace its contents at any time.
    /// </summary>
    public List<UiTab> Tabs { get; } = [];

    /// <summary>
    /// The tab that was open as of the last draw, or <see langword="null"/> before the bar has drawn once.
    /// </summary>
    public string? Current { get; private set; }

    /// <summary>The tab a <see cref="SwitchTab"/> is waiting to open, or <see langword="null"/> when none is.</summary>
    public string? PendingTab => pendingTab;

    /// <summary>Raised when the open tab changes, by a click or by <see cref="SwitchTab"/>, with the new tab's id.</summary>
    public Action<string>? OnTabChanged { get; set; }

    /// <summary>
    /// Raised when a <see cref="UiTab.Closeable"/> tab's close button is used. The tab has already been removed from
    /// <see cref="Tabs"/> when this runs.
    /// </summary>
    public Action<UiTab>? OnTabClosed { get; set; }

    /// <summary>Whether the user may drag the tabs into a different order. Off by default.</summary>
    /// <remarks>
    /// <see cref="Tabs"/> is left as the caller wrote it; a reordering made here is for this session only.
    /// </remarks>
    public bool Reorderable { get; set; }

    /// <summary>
    /// Whether tabs that do not fit scroll rather than shrinking. Off by default, which shrinks them.
    /// </summary>
    public bool ScrollWhenCrowded { get; set; }

    /// <summary>
    /// Whether the mouse wheel scrolls the tab strip while the pointer is over it. On by default.
    /// </summary>
    public bool WheelScrolls { get; set; } = true;

    /// <summary>
    /// How far one notch of the wheel moves the tab strip, in pixels at 100%. Defaults to 80, about one tab.
    /// </summary>
    public float WheelScrollStep { get; set; } = 80f;

    /// <summary>
    /// How wide the bar is allowed to be, in pixels at 100%. Zero, the default, fits the column it is drawn in.
    /// </summary>
    /// <remarks>Only ever narrows: a bar cannot be given more room than the window it is in.</remarks>
    public float Width { get; set; }

    /// <summary>What is drawn when there are no tabs at all. When <see langword="null"/>, nothing is.</summary>
    public Action? EmptyState { get; set; }

    /// <summary>
    /// Opens a tab from code, from anywhere.
    /// </summary>
    /// <remarks>
    /// Safe from any thread and before the bar has ever drawn; calling it twice before a frame runs keeps the last
    /// request.
    /// </remarks>
    /// <param name="id">The <see cref="UiTab.Id"/> to open.</param>
    public void SwitchTab(string id)
    {
        if (string.IsNullOrEmpty(id))
            return;

        NoireUI.RunOnDraw(() => RequestTab(id));
    }

    /// <summary>
    /// Cancels a <see cref="SwitchTab"/> that has not been applied yet.
    /// </summary>
    public void CancelSwitch() => pendingTab = null;

    /// <summary>
    /// Records a switch request, on the draw thread, once it has been checked against the tabs as they stand.
    /// </summary>
    /// <param name="id">The tab to open.</param>
    private void RequestTab(string id)
    {
        switch (ResolveSwitch(Tabs, Current, id))
        {
            case TabSwitch.Accepted:
                pendingTab = id;
                break;

            case TabSwitch.AlreadyOpen:
                pendingTab = null;
                break;

            case TabSwitch.Unknown:
                LogRefusalOnce(id, "there is no tab with that id");
                break;

            case TabSwitch.Unreachable:
                LogRefusalOnce(id, "the tab is disabled");
                break;
        }
    }

    /// <summary>
    /// What a switch request should do, given the tabs and the tab currently open.
    /// </summary>
    /// <param name="tabs">The tabs as they stand.</param>
    /// <param name="current">The tab open as of the last draw, if any.</param>
    /// <param name="requested">The tab being asked for.</param>
    /// <returns>What to do about it.</returns>
    internal static TabSwitch ResolveSwitch(IReadOnlyList<UiTab> tabs, string? current, string requested)
    {
        if (tabs == null || tabs.Count == 0 || string.IsNullOrEmpty(requested))
            return TabSwitch.Unknown;

        UiTab? match = null;

        foreach (var tab in tabs)
        {
            if (!string.Equals(tab.Id, requested, StringComparison.Ordinal))
                continue;

            match = tab;
            break;
        }

        if (match == null)
            return TabSwitch.Unknown;

        // Checked before the already-open case, so a request for the open tab is still reported as reachable or not
        // rather than being waved through on the strength of where the user happens to be standing.
        if (!match.IsEnabled())
            return TabSwitch.Unreachable;

        return string.Equals(current, requested, StringComparison.Ordinal)
            ? TabSwitch.AlreadyOpen
            : TabSwitch.Accepted;
    }

    /// <summary>
    /// Reports a refused switch once per id, so a typo is visible without a log entry every frame something retries.
    /// </summary>
    /// <param name="id">The refused id.</param>
    /// <param name="reason">Why it was refused.</param>
    private void LogRefusalOnce(string id, string reason)
    {
        if (!refusalsLogged.Add(id))
            return;

        NoireLogger.LogWarning(
            $"Tab bar '{Id}' was asked to open '{id}' and did not, because {reason}. "
            + $"Reported once per id; check the id against {nameof(Tabs)}.",
            nameof(NoireTabBar));
    }
}
