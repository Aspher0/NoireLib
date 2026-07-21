using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace NoireLib.UI;

/// <summary>
/// A window listing what every measured scope costs, sortable, searchable and copyable.<br/>
/// Add it to your <see cref="WindowSystem"/> and open it when you want to know which part of your interface is the
/// expensive one.
/// </summary>
/// <remarks>
/// Deliberately built on raw ImGui rather than on <see cref="NoireTable{T}"/>. A diagnostic that depends on the
/// subsystem it diagnoses is unusable exactly when it is needed, and a profiler drawn with a profiled widget would
/// report itself in its own list.
/// </remarks>
/// <example>
/// <code>
/// var profiler = new NoireProfilerWindow();
/// windowSystem.AddWindow(profiler);
///
/// profiler.IsOpen = true;   // or bind it to a command
/// </code>
/// </example>
public sealed class NoireProfilerWindow : Window
{
    /// <summary>
    /// The window's default name, and the id its position is remembered under.
    /// </summary>
    public const string DefaultName = "NoireUI Profiler";

    /// <summary>
    /// Above this many milliseconds an average is drawn as a warning. One sixtieth of a frame at 60 FPS is 0.27 ms, so
    /// half a millisecond spent building one element is worth looking at rather than a number to be alarmed by.
    /// </summary>
    private const double WarnMs = 0.5d;

    private readonly List<UiProfileEntry> rows = new();
    private readonly StringBuilder clipboard = new();

    /// <summary>
    /// The rows that sit under each scope, rebuilt each frame. Held as a field rather than built fresh so an open
    /// diagnostic window does not allocate a dictionary and a list per frame while measuring how much everything else
    /// allocates.
    /// </summary>
    private readonly Dictionary<int, List<UiProfileEntry>> children = new();

    private readonly List<UiProfileEntry> roots = new();
    private readonly HashSet<int> drawn = new();
    private readonly HashSet<int> present = new();

    /// <summary>
    /// Which node each one sits inside, so a search can walk from a match up to its roots.
    /// </summary>
    private readonly Dictionary<int, int> parentOf = new();

    /// <summary>
    /// While a search is running, the nodes that matched plus every ancestor of one. Empty means no search, which
    /// shows everything.
    /// </summary>
    private readonly HashSet<int> visible = new();

    /// <summary>
    /// Whether scopes that did not run on the last measured frame are listed.
    /// </summary>
    /// <remarks>
    /// On by default, because a scope that has stopped running still carries the reading worth having: its longest
    /// frame, which is usually the one that built it for the first time. Turn it off when a plugin has accumulated
    /// enough rows that the zeroes bury what is actually costing something.
    /// </remarks>
    private bool showInactive = true;

    /// <summary>
    /// Which branches are open, by node id.
    /// </summary>
    /// <remarks>
    /// Held here rather than left to ImGui's own tree storage, which cannot be reached for a node that is not being
    /// submitted. Expanding everything has to reach branches that are currently hidden inside collapsed parents, and
    /// with ImGui owning the state that was impossible: the flag only landed on the rows already on screen, so the
    /// buttons appeared to open one level and stop.
    /// </remarks>
    private readonly Dictionary<int, bool> openState = new();

    /// <summary>
    /// Set for one frame by the expand and collapse buttons, and applied to every known node, drawn or not.
    /// </summary>
    private bool? pendingTreeState;

    /// <summary>
    /// Every scope in the last snapshot, by name, so a search can walk a match's parents back up to a root.
    /// </summary>
    private readonly Dictionary<string, UiProfileEntry> byName = new(StringComparer.Ordinal);

    /// <summary>
    /// Set for one frame by the expand and collapse buttons, and applied to every node as it is drawn.
    /// </summary>
    private bool? pendingOpenAll;

    private double totalLastMs;
    private double totalAverageMs;

    private string search = string.Empty;
    private int sortColumn = (int)Column.Self;
    private bool sortAscending;

    /// <summary>
    /// The table's columns, in the order they are drawn.
    /// </summary>
    private enum Column
    {
        Scope,
        Calls,
        Self,
        Total,
        Last,
        Longest,
    }

    /// <summary>
    /// Creates the window.
    /// </summary>
    /// <param name="name">The window title. Defaults to <see cref="DefaultName"/>.</param>
    public NoireProfilerWindow(string name = DefaultName)
        : base(name)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460f, 260f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        Size = new Vector2(620f, 420f);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <inheritdoc/>
    public override void Draw()
    {
        // The window measures itself, because it is not free: a few hundred rows of tree nodes and formatted numbers
        // cost more than most of what they are reporting on. Left out, that cost lands in the root's unaccounted time
        // and reads as the plugin being slow, when it is the act of looking that is slow.
        using var profile = NoireUI.Profiler.Measure(SelfScopeName);

        DrawContents();
    }

    /// <summary>
    /// The name this window records its own drawing under.
    /// </summary>
    public const string SelfScopeName = "NoireProfilerWindow";

    /// <summary>
    /// Draws the profiler's controls and table into whatever is currently being drawn, for a plugin that would rather
    /// put this on a page of its own settings than in a window of its own.
    /// </summary>
    public void DrawContents()
    {
        // Refreshed before anything is drawn, so the copy button and the totals line read the same frame the table
        // does rather than the one before it.
        Refresh();

        DrawControls();
        ImGui.Separator();
        DrawTotals();
        DrawSearch();
        DrawTable();
    }

    /// <summary>
    /// The tracking switch and the two whole-list actions.
    /// </summary>
    private void DrawControls()
    {
        var enabled = NoireUI.Profiler.Enabled;

        if (ImGui.Checkbox("Enable", ref enabled))
            NoireUI.Profiler.Enabled = enabled;

        ImGui.SameLine(0f, NoireUI.Scaled(14f));

        var inactive = showInactive;

        if (ImGui.Checkbox("Show idle", ref inactive))
            showInactive = inactive;

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Lists scopes that did not run on the last measured frame: pages you have visited and closed,\n"
                + "widgets no longer on screen. Their longest reading is usually the frame that built them for\n"
                + "the first time, which is where an opening cost shows up.");
        }

        if (!NoireUI.Profiler.Enabled)
        {
            ImGui.SameLine(0f, NoireUI.Scaled(14f));
            ImGui.TextDisabled("(not measuring)");
        }

        if (ImGui.Button("Reset all"))
            NoireUI.Profiler.Reset();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Forgets every measurement, including the longest seen.");

        ImGui.SameLine(0f, NoireUI.Scaled(6f));

        if (ImGui.Button("Copy all"))
            ImGui.SetClipboardText(BuildClipboardText());

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Copies the summary lines and the whole table as tab separated text, in the order it is sorted.");

        ImGui.SameLine(0f, NoireUI.Scaled(12f));

        if (ImGui.Button("Expand all"))
            pendingTreeState = true;

        ImGui.SameLine(0f, NoireUI.Scaled(6f));

        if (ImGui.Button("Collapse all"))
            pendingTreeState = false;
    }

    /// <summary>
    /// The totals across every scope, which is what says whether a number in the table is worth acting on.
    /// </summary>
    private void DrawTotals()
    {
        var (lastTotal, averageTotal) = SelfTotals();
        var root = NoireUI.Profiler.RootAverageMs;
        var unaccounted = NoireUI.Profiler.UnaccountedAverageMs;

        ImGui.TextUnformatted(
            $"Total last: {lastTotal.ToString("0.0000", CultureInfo.CurrentCulture)} ms     "
            + $"Total average: {averageTotal.ToString("0.0000", CultureInfo.CurrentCulture)} ms     "
            + $"Scopes: {rows.Count}");

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Summed from the self column, so each piece of work is counted once.");

        if (root <= 0d)
        {
            ImGui.TextDisabled("No whole-draw scope: some of the frame is unaccounted for.");

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    $"Wrap your draw callback in a scope named {UiProfiler.RootScopeName} and this line becomes\n"
                    + "the same span your host times the plugin over, with the remainder it cannot see broken out.");
            }

            return;
        }

        // The share nothing claimed. This is the honest measure of how complete the instrumentation is: a large
        // remainder means the profiler is not yet explaining the frame, not that the frame is cheap.
        var share = root > 0d ? unaccounted / root * 100d : 0d;

        ImGui.TextUnformatted($"Whole draw: {root.ToString("0.0000", CultureInfo.CurrentCulture)} ms");
        ImGui.SameLine(0f, NoireUI.Scaled(14f));

        var colour = share >= 50d
            ? new Vector4(0.93f, 0.72f, 0.35f, 1f)
            : new Vector4(0.55f, 0.58f, 0.62f, 1f);

        // Coloured by pushing rather than with TextColored, which is a printf-style call: a percent sign in the text
        // would be read as a conversion there, and escaping it is a trap nobody remembers on the next edit.
        using (ImRaii.PushColor(ImGuiCol.Text, colour))
        {
            ImGui.TextUnformatted(
                $"Unaccounted: {unaccounted.ToString("0.0000", CultureInfo.CurrentCulture)} ms "
                + $"({share.ToString("0", CultureInfo.CurrentCulture)}%)");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Time inside the draw callback that no measured scope claimed: the windowing, the ImGui work\n"
                + "around the widgets, and anything not instrumented yet. Shrinking this is how the profiler\n"
                + "gets more complete.");
        }
    }

    /// <summary>
    /// The self time across every scope, for the last frame and averaged.
    /// </summary>
    /// <remarks>
    /// Self time, not total time. Scopes nest, so adding up the total column counts a widget once for itself and again
    /// for every scope enclosing it, which is how an interface comes out looking several times its real cost.
    /// </remarks>
    /// <returns>The last frame's total and the rolling average.</returns>
    private (double Last, double Average) SelfTotals()
    {
        var last = 0d;
        var average = 0d;

        foreach (var row in rows)
        {
            last += row.SelfLastMs;
            average += row.SelfAverageMs;
        }

        return (last, average);
    }

    /// <summary>
    /// The name filter.
    /// </summary>
    private void DrawSearch()
    {
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("###NoireProfilerSearch", "Search", ref search, 128);
    }

    /// <summary>
    /// The table itself, sorted by whichever header was last clicked.
    /// </summary>
    private void DrawTable()
    {
        Refresh();

        const ImGuiTableFlags flags =
            ImGuiTableFlags.RowBg
            | ImGuiTableFlags.BordersInnerV
            | ImGuiTableFlags.Sortable
            | ImGuiTableFlags.Resizable
            | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("###NoireProfilerTable", 6, flags))
            return;

        try
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Scope", ImGuiTableColumnFlags.WidthStretch, 3f);
            ImGui.TableSetupColumn("Calls", ImGuiTableColumnFlags.WidthStretch, 0.9f);
            ImGui.TableSetupColumn("Self avg", ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.WidthStretch, 1.4f);
            ImGui.TableSetupColumn("Total avg", ImGuiTableColumnFlags.WidthStretch, 1.4f);
            ImGui.TableSetupColumn("Last", ImGuiTableColumnFlags.WidthStretch, 1.4f);
            // The last column takes whatever is left, so a handle on its right edge would have nothing to give ground
            // to and reads as broken.
            ImGui.TableSetupColumn("Longest", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoResize, 1.4f);
            ImGui.TableHeadersRow();

            ReadSortSpecs();

            BuildTree();

            // Applied to every node the profiler knows about, not merely the ones on screen, which is the whole point:
            // the branches that need opening are the ones currently hidden inside a collapsed parent.
            if (pendingTreeState is { } state)
            {
                foreach (var row in rows)
                    openState[row.Id] = state;

                pendingTreeState = null;
            }

            foreach (var root in roots)
                DrawBranch(root, depth: 0);
        }
        finally
        {
            ImGui.EndTable();
        }

        if (rows.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled(NoireUI.Profiler.Enabled
                ? "Nothing measured yet. Draw something."
                : "Tracking is off.");
        }
    }

    /// <summary>
    /// Groups the rows by the scope they sit inside, and collects the ones with nothing above them.
    /// </summary>
    /// <remarks>
    /// A scope whose parent was filtered out, or which was measured before its parent existed, is treated as a root
    /// rather than dropped. A profiler that silently hides rows it cannot place is worse than one that shows them at
    /// the top level.
    /// </remarks>
    private void BuildTree()
    {
        foreach (var list in children.Values)
            list.Clear();

        roots.Clear();
        drawn.Clear();
        present.Clear();
        parentOf.Clear();

        foreach (var row in rows)
        {
            present.Add(row.Id);
            parentOf[row.Id] = row.ParentId;
        }

        foreach (var row in rows)
        {
            // Anything whose parent is not itself in the table would never be reached from a root, so it becomes one.
            // Showing a row somewhere is always better than a profiler quietly dropping it.
            if (row.ParentId == 0 || !present.Contains(row.ParentId))
            {
                roots.Add(row);
                continue;
            }

            if (!children.TryGetValue(row.ParentId, out var list))
                children[row.ParentId] = list = new List<UiProfileEntry>();

            list.Add(row);
        }

        roots.Sort(CompareRows);
        BuildVisibility();
    }

    /// <summary>
    /// Works out which scopes a search leaves on screen: the ones that matched, and every scope between a match and its
    /// root.
    /// </summary>
    /// <remarks>
    /// Keeping the ancestors is what makes the search usable on a tree. Showing only the matches would strand them with
    /// nothing above them, and hiding a parent that did not match itself would hide every match underneath it.
    /// </remarks>
    private void BuildVisibility()
    {
        visible.Clear();

        if (search.Length == 0)
            return;

        foreach (var row in rows)
        {
            if (row.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var id = row.Id;

            // Stops as soon as it reaches something already marked, whose own ancestors were therefore marked with it.
            for (var depth = 0; id != 0 && depth < MaxDepth && visible.Add(id); depth++)
                id = parentOf.TryGetValue(id, out var parent) ? parent : 0;
        }
    }

    /// <summary>
    /// Draws a scope and, when it is open, everything measured inside it.
    /// </summary>
    /// <param name="row">The scope to draw.</param>
    /// <param name="depth">How deep the branch is, used only to stop a malformed chain from recursing forever.</param>
    private void DrawBranch(UiProfileEntry row, int depth)
    {
        // A scope seen under two parents in one frame could otherwise be reached twice and, in the worst case, become
        // its own ancestor.
        if (depth > MaxDepth || !drawn.Add(row.Id))
            return;

        if (search.Length > 0 && !visible.Contains(row.Id))
            return;

        var hasChildren = children.TryGetValue(row.Id, out var list) && list.Count > 0;
        var isRoot = string.Equals(row.Name, UiProfiler.RootScopeName, StringComparison.Ordinal);

        var open = DrawRow(row, leaf: !hasChildren, defaultOpen: isRoot);

        if (!open || !hasChildren)
            return;

        list!.Sort(CompareRows);

        foreach (var child in list)
            DrawBranch(child, depth + 1);

        ImGui.TreePop();
    }

    /// <summary>
    /// How deep the tree may go before it is assumed to be malformed.
    /// </summary>
    private const int MaxDepth = 32;

    /// <summary>
    /// Draws one scope's row.
    /// </summary>
    /// <param name="row">The scope to draw.</param>
    /// <param name="leaf">Whether it has nothing measured inside it.</param>
    /// <param name="defaultOpen">Whether it starts expanded.</param>
    /// <returns>Whether the branch is open, and so whether its children should be drawn.</returns>
    private bool DrawRow(UiProfileEntry row, bool leaf, bool defaultOpen = false)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        var flags = ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick;

        if (leaf)
        {
            // Leaves push nothing onto the tree stack, so they must not be popped either.
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.Bullet;
        }

        // What this branch should be showing: a match's ancestors are forced open, or the match would sit behind a
        // closed arrow and the search would look like it had found nothing.
        var wanted = search.Length > 0 || (openState.TryGetValue(row.Id, out var stored) ? stored : defaultOpen);

        if (!leaf)
            ImGui.SetNextItemOpen(wanted);

        // Keyed by the node id, not the name: the same name appears under several callers now, and ImGui would give
        // them one shared open state and collapse them together.
        ImGui.PushID(row.Id);

        var open = ImGui.TreeNodeEx(row.Name, flags) && !leaf;

        ImGui.PopID();

        // The state is forced every frame, so a difference here is the reader having just clicked the arrow.
        if (!leaf && open != wanted && search.Length == 0)
            openState[row.Id] = open;

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(row.Calls.ToString(CultureInfo.CurrentCulture));

        ImGui.TableNextColumn();

        // Only the self average is coloured. The total flags every scope enclosing an expensive one, which points at
        // the page rather than at the widget actually spending the time.
        if (row.SelfAverageMs >= WarnMs)
            ImGui.TextColored(new Vector4(0.93f, 0.72f, 0.35f, 1f), Format(row.SelfAverageMs));
        else
            ImGui.TextUnformatted(Format(row.SelfAverageMs));

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(Format(row.AverageMs));

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(Format(row.LastMs));

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(Format(row.PeakMs));

        return open;
    }

    /// <summary>
    /// Takes a fresh snapshot, drops what the search excludes, and sorts what is left.
    /// </summary>
    private void Refresh()
    {
        rows.Clear();

        // Everything the search excludes is kept, because a filter that dropped rows would also drop the parents a
        // match hangs from and the tree would fall apart into a flat list exactly when it is most wanted. Scopes that
        // did not run are a different matter: they are noise rather than context.
        foreach (var entry in NoireUI.Profiler.Snapshot())
        {
            if (!showInactive && entry.Calls == 0 && entry.LastMs <= 0d)
                continue;

            rows.Add(entry);
        }

        Sort();
    }

    /// <summary>
    /// Sorts by the current column, largest first unless the header says otherwise.
    /// </summary>
    private void Sort() => rows.Sort(CompareRows);

    /// <summary>
    /// Orders two scopes by the column the header says, largest first unless it says otherwise.
    /// </summary>
    /// <remarks>
    /// Used for the flat list and for each set of siblings in the tree, so a branch is ordered the same way the table
    /// as a whole is.
    /// </remarks>
    private int CompareRows(UiProfileEntry left, UiProfileEntry right)
    {
        var order = (Column)sortColumn switch
        {
            Column.Scope => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase),
            Column.Calls => left.Calls.CompareTo(right.Calls),
            Column.Self => left.SelfAverageMs.CompareTo(right.SelfAverageMs),
            Column.Total => left.AverageMs.CompareTo(right.AverageMs),
            Column.Last => left.LastMs.CompareTo(right.LastMs),
            _ => left.PeakMs.CompareTo(right.PeakMs),
        };

        // Ties are broken by name, so the list does not reshuffle every frame between scopes reading the same zero.
        if (order == 0)
            order = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);

        return sortAscending ? order : -order;
    }

    /// <summary>
    /// Reads which header the user last clicked.
    /// </summary>
    /// <remarks>
    /// Read after the rows have been sorted rather than before, so a click takes effect on the next frame. Sorting the
    /// list a second time inside the same table would leave the header and the rows disagreeing for a frame.
    /// </remarks>
    private void ReadSortSpecs()
    {
        var specs = ImGui.TableGetSortSpecs();

        if (specs.IsNull || specs.SpecsCount <= 0)
            return;

        var primary = specs.Specs[0];

        sortColumn = primary.ColumnIndex;
        sortAscending = primary.SortDirection == ImGuiSortDirection.Ascending;
    }

    /// <summary>
    /// Builds the whole table as tab separated text, in the order it is displayed.
    /// </summary>
    private string BuildClipboardText()
    {
        var (lastTotal, averageTotal) = SelfTotals();
        var root = NoireUI.Profiler.RootAverageMs;
        var unaccounted = NoireUI.Profiler.UnaccountedAverageMs;

        clipboard.Clear();

        // The summary goes with the table, because the rows are unreadable without it: a pasted list of scopes says
        // nothing about what share of the frame it accounts for.
        clipboard.Append("Total last: ").Append(lastTotal.ToString("0.0000", CultureInfo.InvariantCulture))
            .Append(" ms\tTotal average: ").Append(averageTotal.ToString("0.0000", CultureInfo.InvariantCulture))
            .Append(" ms\tScopes: ").Append(rows.Count.ToString(CultureInfo.InvariantCulture))
            .AppendLine();

        if (root > 0d)
        {
            var share = unaccounted / root * 100d;

            clipboard.Append("Whole draw: ").Append(root.ToString("0.0000", CultureInfo.InvariantCulture))
                .Append(" ms\tUnaccounted: ").Append(unaccounted.ToString("0.0000", CultureInfo.InvariantCulture))
                .Append(" ms (").Append(share.ToString("0", CultureInfo.InvariantCulture)).Append("%)")
                .AppendLine();
        }
        else
        {
            clipboard.AppendLine("Whole draw: not measured, so some of the frame is unaccounted for.");
        }

        clipboard.AppendLine();
        clipboard.AppendLine("Scope\tPath\tCalls\tSelf (ms)\tLast (ms)\tLongest (ms)\tAverage (ms)");

        foreach (var row in rows)
        {
            clipboard.Append(row.Name).Append('\t')
                .Append(PathOf(row)).Append('\t')
                .Append(row.Calls.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(row.SelfAverageMs.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\t')
                .Append(row.LastMs.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\t')
                .Append(row.PeakMs.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\t')
                .AppendLine(row.AverageMs.ToString("0.0000", CultureInfo.InvariantCulture));
        }

        return clipboard.ToString();
    }

    /// <summary>
    /// The full chain of scopes a row sits under, outermost first.
    /// </summary>
    /// <remarks>
    /// The whole path rather than the parent's name, because names repeat: a helper called from thirty places has
    /// thirty nodes whose parents may all be called the same thing, and a pasted table naming only the parent reads as
    /// thirty identical rows. The tree does not have this problem, since each one is drawn where it belongs.
    /// </remarks>
    private string PathOf(UiProfileEntry row)
    {
        path.Clear();

        var id = row.ParentId;

        for (var depth = 0; id != 0 && depth < MaxDepth; depth++)
        {
            var found = false;

            foreach (var candidate in rows)
            {
                if (candidate.Id != id)
                    continue;

                path.Insert(0, candidate.Name);
                id = candidate.ParentId;
                found = true;
                break;
            }

            if (!found)
                break;
        }

        return string.Join(" / ", path);
    }

    private readonly List<string> path = new();

    /// <summary>
    /// Formats a millisecond reading at the precision the numbers actually live at.
    /// </summary>
    private static string Format(double ms) => ms.ToString("0.0000", CultureInfo.CurrentCulture);
}
