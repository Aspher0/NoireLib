using NoireLib.Helpers;
using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// A tab bar you can open from code. <c>tabs.SwitchTab("filters")</c> works from another window, a hotkey, a command
/// or a toast action, and each tab carries its own body so nothing is drawn for the ones that are closed.
/// </summary>
/// <remarks>
/// ImGui has a perfectly good tab bar and a genuinely bad story for opening a tab from code. The only lever is
/// <c>ImGuiTabItemFlags.SetSelected</c>, and it has to be set for exactly one frame: leave it set and the tab is welded
/// open with the user unable to click away, clear it on the wrong frame and the switch silently does not happen. Every
/// plugin that wants "the changelog button opens the What's New tab" hand-rolls that dance, and the edge cases are
/// where it goes wrong.<br/>
/// <see cref="SwitchTab"/> is that dance done once. It is callable from any thread, callable before the bar has ever
/// drawn, does nothing if the tab is already open, keeps only the last request when called twice before a frame runs,
/// and refuses an unknown or unreachable tab with one log rather than silently.
/// </remarks>
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
    /// <remarks>
    /// Answers for what was actually drawn rather than for what has been asked for, so it is null until there is a real
    /// answer instead of guessing at the first tab. A <see cref="SwitchTab"/> in flight is visible through
    /// <see cref="PendingTab"/>.
    /// </remarks>
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
    /// ImGui owns the order it draws them in, and does not report it back, so <see cref="Tabs"/> is left as the caller
    /// wrote it. A reordering made here is for this session and is not something to persist.
    /// </remarks>
    public bool Reorderable { get; set; }

    /// <summary>
    /// Whether tabs that do not fit scroll rather than shrinking. Off by default, which shrinks them.
    /// </summary>
    public bool ScrollWhenCrowded { get; set; }

    /// <summary>
    /// Whether the mouse wheel scrolls the tab strip while the pointer is over it. On by default.
    /// </summary>
    /// <remarks>
    /// ImGui's own tab bar does not do this. Its only way to reach a tab that has scrolled off is the little arrows at
    /// the end of the bar, or selecting the last visible tab so the bar scrolls one along and repeating, which
    /// changes the open tab as the price of looking for another one. Wheeling over the strip moves it without
    /// selecting anything.<br/>
    /// This does nothing while every tab already fits, so it costs nothing to leave on. It also keeps the wheel from
    /// scrolling the surrounding window at the same time, because a wheel that scrolls two things at once is worse
    /// than one that scrolls the wrong one.
    /// </remarks>
    public bool WheelScrolls { get; set; } = true;

    /// <summary>
    /// How far one notch of the wheel moves the tab strip, in pixels at 100%. Defaults to 80, about one tab.
    /// </summary>
    public float WheelScrollStep { get; set; } = 80f;

    /// <summary>
    /// How wide the bar is allowed to be, in pixels at 100%. Zero, the default, fits the column it is drawn in.
    /// </summary>
    /// <remarks>
    /// ImGui builds a tab bar out to the window's right edge and its public surface takes no width at all, so a bar
    /// inside a page that centres its content in a narrower column runs past the column and out the other side. Left at
    /// zero this asks <see cref="NoireLayout.ContentWidth"/> instead, which answers for the column rather than the
    /// window. Set it to hold the bar to a width of your own.<br/>
    /// Only ever narrows: a bar cannot be given more room than the window it is in.
    /// </remarks>
    public float Width { get; set; }

    /// <summary>What is drawn when there are no tabs at all. When <see langword="null"/>, nothing is.</summary>
    public Action? EmptyState { get; set; }

    /// <summary>
    /// Opens a tab from code, from anywhere.
    /// </summary>
    /// <remarks>
    /// Safe from any thread and at any time: the request is marshalled onto the draw thread and applied on the next
    /// frame the bar draws, so it works before the bar has ever drawn and from a background task alike. Calling it
    /// twice before a frame runs keeps the last request rather than queueing both.<br/>
    /// A tab that is already open, does not exist, or is not reachable is refused rather than forced, and each refused
    /// id is logged once so a typo or a removed tab is visible without filling the log.
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
    /// <remarks>
    /// Separated from the drawing because it is the whole of what this widget exists to get right, and the only part
    /// that can be checked without an ImGui context.
    /// </remarks>
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
