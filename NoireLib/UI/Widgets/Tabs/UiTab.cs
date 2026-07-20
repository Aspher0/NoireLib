using NoireLib.Helpers;
using System;

namespace NoireLib.UI;

/// <summary>
/// One tab of a <see cref="NoireTabBar"/>: what it is called, what it draws, and the conditions under which it is
/// reachable.
/// </summary>
/// <remarks>
/// The tab carries its own body, so nothing is drawn for a tab that is not open and there is no end call to forget.
/// <br/>
/// <see cref="Id"/> is what code refers to the tab by and never changes. <see cref="Label"/> is what the user reads
/// and may change every frame, including its length, without the tab losing its identity or its position.
/// </remarks>
/// <example>
/// <code>
/// new UiTab("filters", "Filters", () =&gt; DrawFilters())
/// {
///     Badge = () =&gt; activeFilters,
///     Enabled = () =&gt; hasData,
///     DisabledReason = "Load a log first.",
/// }
/// </code>
/// </example>
public sealed class UiTab
{
    /// <summary>
    /// Creates a tab.
    /// </summary>
    /// <param name="id">What code refers to this tab by. When <see langword="null"/> or blank, a random one is generated.</param>
    /// <param name="label">What the user reads on it.</param>
    /// <param name="body">What it draws when it is open.</param>
    public UiTab(string? id = null, string label = "", Action? body = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? RandomGenerator.GenerateGuidString() : id;
        Label = label;
        Body = body;
    }

    /// <summary>
    /// What code refers to this tab by, for example in <see cref="NoireTabBar.SwitchTab"/>. Fixed for the tab's life.
    /// </summary>
    public string Id { get; }

    /// <summary>What the user reads on the tab. Free to change at any time.</summary>
    public string Label { get; set; }

    /// <summary>What the tab draws when it is open. Nothing is drawn while it is closed.</summary>
    public Action? Body { get; set; }

    /// <summary>A tooltip shown when the tab is hovered.</summary>
    public string? Tooltip { get; set; }

    /// <summary>
    /// A count drawn as a badge on the tab, re-read every frame. When <see langword="null"/>, or when it returns zero
    /// or less, no badge is drawn.
    /// </summary>
    /// <remarks>A delegate rather than a number because the thing being counted is the caller's and moves on its own.</remarks>
    public Func<int>? Badge { get; set; }

    /// <summary>How the badge looks. When <see langword="null"/>, the shipped defaults. See <see cref="BadgeStyle"/>.</summary>
    public BadgeStyle? BadgeStyle { get; set; }

    /// <summary>
    /// Whether the tab can be reached, re-read every frame. When <see langword="null"/>, it always can.
    /// </summary>
    /// <remarks>
    /// This gates reaching the tab, not what it shows. A tab that becomes disabled while it is the open one stays open
    /// and keeps drawing: closing it under the user would move them somewhere they did not ask to go, and blanking it
    /// would leave them looking at nothing with no way to tell what happened.
    /// </remarks>
    public Func<bool>? Enabled { get; set; }

    /// <summary>
    /// Why the tab is disabled, shown on hover. Worth setting whenever <see cref="Enabled"/> is, because a control that
    /// is dead for no stated reason reads as broken.
    /// </summary>
    public string? DisabledReason { get; set; }

    /// <summary>Whether the tab carries a close button. Off by default. See <see cref="NoireTabBar.OnTabClosed"/>.</summary>
    public bool Closeable { get; set; }

    /// <summary>Whether this tab is currently reachable, resolving <see cref="Enabled"/>.</summary>
    /// <returns>True when the tab can be reached.</returns>
    public bool IsEnabled()
    {
        var predicate = Enabled;

        if (predicate == null)
            return true;

        try
        {
            return predicate();
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(nameof(UiTab), $"The Enabled predicate of tab '{Id}' threw.", ex);
            return true;
        }
    }

    /// <summary>Whatever the badge count is right now, resolving <see cref="Badge"/>.</summary>
    /// <returns>The count, or zero when there is no badge.</returns>
    public int BadgeCount()
    {
        var source = Badge;

        if (source == null)
            return 0;

        try
        {
            return source();
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(nameof(UiTab), $"The Badge delegate of tab '{Id}' threw.", ex);
            return 0;
        }
    }
}
