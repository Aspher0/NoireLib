using Dalamud.Bindings.ImGui;
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
/// expensive one.<br/>
/// Right-click a row to leave that scope out of the totals, which marks it in red; right-click it again to put it
/// back. See <see cref="UiProfiler.SetExcluded"/>.
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

    /// <summary>
    /// The allocation per frame a scope is flagged at.
    /// </summary>
    /// <remarks>
    /// Deliberately just above zero. There is no healthy amount of steady per-frame allocation on the draw thread, so
    /// the threshold exists to ignore the rounding on a rolling average rather than to tolerate a budget.
    /// </remarks>
    private const double WarnBytes = 1d;

    /// <summary>
    /// The colour a figure over its threshold is drawn in.
    /// </summary>
    private static readonly Vector4 WarnColour = new(0.93f, 0.72f, 0.35f, 1f);

    /// <summary>
    /// The background a scope excluded from the totals is drawn on.
    /// </summary>
    /// <remarks>
    /// Dark and half transparent rather than a flat red. It has to read as a marked row at a glance while the figures
    /// on it stay as legible as every other row's, since an excluded scope is still being measured and its own numbers
    /// are still the reason it is on screen.
    /// </remarks>
    private static readonly Vector4 ExcludedColour = new(0.42f, 0.09f, 0.11f, 0.55f);

    /// <summary>
    /// The colour the count of excluded scopes is written in: the same red lifted to where it is readable as text.
    /// </summary>
    private static readonly Vector4 ExcludedTextColour = new(0.86f, 0.36f, 0.38f, 1f);

    private readonly List<UiProfileEntry> rows = new();
    private readonly StringBuilder clipboard = new();

    /// <summary>
    /// The profiler's own read, taken into a list this window owns so that looking costs no garbage.
    /// </summary>
    private readonly List<UiProfileEntry> snapshot = new();

    /// <summary>
    /// The row comparison, held rather than passed as a method group.
    /// </summary>
    /// <remarks>
    /// A method group converted to a delegate at the call site allocates one every time, because an instance method
    /// carries its receiver. This window sorts the roots and every branch, so that was a delegate per branch per
    /// frame.
    /// </remarks>
    private readonly Comparison<UiProfileEntry> compareRows;

    /// <summary>
    /// What the cached rows and text were built from, so a frame that changed nothing rebuilds nothing.
    /// </summary>
    private int lastGeneration = -1;

    /// <summary>How often the rows are rebuilt while the figures move every frame, in seconds.</summary>
    private const float RefreshInterval = 0.15f;

    /// <summary>When the rows were last rebuilt. Negative so the first frame always builds.</summary>
    private float lastRefreshTime = float.NegativeInfinity;

    private string lastSearch = string.Empty;
    private bool lastShowInactive = true;
    private int lastSortColumn = -1;
    private bool lastSortAscending;

    /// <summary>
    /// Whether the tree has to be regrouped before it is drawn again.
    /// </summary>
    private bool treeDirty = true;

    /// <summary>
    /// One row as it appears on screen, with the tree already walked.
    /// </summary>
    /// <param name="Row">The scope.</param>
    /// <param name="Depth">How far in it sits, which is drawn as indentation rather than as a tree push.</param>
    /// <param name="HasChildren">Whether it can be opened.</param>
    private readonly record struct VisibleRow(UiProfileEntry Row, int Depth, bool HasChildren);

    /// <summary>
    /// Every row a reader could scroll to, in the order they appear, flattened out of the tree.
    /// </summary>
    /// <remarks>
    /// A clipper needs a count and an index, and a tree drawn by recursion offers neither: the rows a collapsed branch
    /// hides are not known without walking it, and <c>TreePop</c> cannot be paired across a range the clipper skipped.
    /// Walking once into a flat list and drawing the indentation by hand gives up nothing and makes the row count
    /// something ImGui can skip through.
    /// </remarks>
    private readonly List<VisibleRow> visibleRows = new();

    /// <summary>
    /// Whether the flattened rows have to be rebuilt before they are drawn again.
    /// </summary>
    private bool flattenDirty = true;

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

    /// <summary>
    /// How many scopes are currently marked as excluded, counted from the read rather than asked of the profiler.
    /// </summary>
    /// <remarks>
    /// Taken from the snapshot, which holds every node, rather than from the rows, which the idle filter can thin out:
    /// a scope excluded and then hidden is still excluded, and a control that disappeared while its mark stayed in
    /// force would leave no way to lift it.
    /// </remarks>
    private int excludedCount;

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
        SelfBytes,
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
        compareRows = CompareRows;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460f, 260f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        Size = new Vector2(620f, 420f);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary>
    /// Draws the window: the controls, the totals, and the tree of measured scopes.<br/>
    /// Called by Dalamud's window system; a host that registered the window does not call this itself.
    /// </summary>
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

        var tracking = NoireUI.Profiler.TrackAllocations;

        if (ImGui.Checkbox("Bytes", ref tracking))
            NoireUI.Profiler.TrackAllocations = tracking;

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Fills the byte columns, which read zero while this is off.\n"
                + "Separate from Enable because sampling allocation costs more per scope than timing does, and an\n"
                + "interface opening several hundred scopes a frame pays it on every one. Switch it on to judge\n"
                + "whether a change allocates; leave it off while reading milliseconds.");
        }

        ImGui.SameLine(0f, NoireUI.Scaled(14f));

        var fine = NoireUI.Profiler.Detailed;

        if (ImGui.Checkbox("Detail", ref fine))
            NoireUI.Profiler.Detailed = fine;

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Breaks each surface down into per-method rows, one per drawing helper (NoireShapes.Glow and the\n"
                + "like). Those rows are most of what measuring costs: a decorated window opens a scope per shape\n"
                + "it paints, several hundred a frame. While this is off their time folds into the widget or\n"
                + "surface around them, so the totals stay complete either way.");
        }

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

        // Only once there is something to lift. A control that is always there and does nothing on most frames is one
        // more thing to read past on a window whose whole job is to be read quickly.
        if (excludedCount == 0)
            return;

        ImGui.SameLine(0f, NoireUI.Scaled(12f));

        if (ImGui.Button("Include all"))
            NoireUI.Profiler.ClearExclusions();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Counts every excluded scope towards the totals again. Nothing measured is forgotten.");
    }

    /// <summary>
    /// The totals across every scope, which is what says whether a number in the table is worth acting on.
    /// </summary>
    private void DrawTotals()
    {
        var (lastTotal, averageTotal) = SelfTotals();
        var root = NoireUI.Profiler.RootAverageMs;
        var unaccounted = NoireUI.Profiler.UnaccountedAverageMs;

        // Written into the stack rather than interpolated into strings. Every figure on these lines is a rolling
        // average, so all of them move on every measured frame and none of them can be cached.
        Span<char> line = stackalloc char[LineCapacity];

        if (line.TryWrite(
                CultureInfo.CurrentCulture,
                $"Total last: {lastTotal:0.0000} ms     Total average: {averageTotal:0.0000} ms     Scopes: {rows.Count}     Allocated: {NoireUI.Profiler.TotalAverageBytes:N0} bytes/frame",
                out var written))
        {
            ImGui.TextUnformatted(line[..written]);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Summed from the self column, so each piece of work is counted once.\n"
                + "Right-click a scope to leave it out of these totals, and again to put it back.");
        }

        if (excludedCount > 0)
        {
            ImGui.SameLine(0f, NoireUI.Scaled(14f));

            using (UiPush.Color(ImGuiCol.Text, ExcludedTextColour))
            {
                if (line.TryWrite(CultureInfo.CurrentCulture, $"Excluded: {excludedCount}", out written))
                    ImGui.TextUnformatted(line[..written]);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "Scopes marked on the table and left out of the totals above. They are still measured\n"
                    + "and still report their own figures. Use Include all to lift every mark at once.");
            }
        }

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

        if (line.TryWrite(CultureInfo.CurrentCulture, $"Whole draw: {root:0.0000} ms", out written))
            ImGui.TextUnformatted(line[..written]);

        ImGui.SameLine(0f, NoireUI.Scaled(14f));

        // The share nothing claimed. This is the honest measure of how complete the instrumentation is: a large
        // remainder means the profiler is not yet explaining the frame, not that the frame is cheap.
        var share = unaccounted / root * 100d;

        var colour = share >= 50d
            ? WarnColour
            : new Vector4(0.55f, 0.58f, 0.62f, 1f);

        // Coloured by pushing rather than with TextColored, which is a printf-style call: the percent sign below would
        // be read as a conversion there, and escaping it is a trap nobody remembers on the next edit.
        using (UiPush.Color(ImGuiCol.Text, colour))
        {
            if (line.TryWrite(CultureInfo.CurrentCulture, $"Unaccounted: {unaccounted:0.0000} ms ({share:0}%)", out written))
                ImGui.TextUnformatted(line[..written]);
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
    /// for every scope enclosing it, which is how an interface comes out looking several times its real cost.<br/>
    /// Scopes the reader has excluded are skipped, which is the whole point of marking one: it is how the cost of a
    /// window open beside the thing being measured is kept out of the figure being read.
    /// </remarks>
    /// <returns>The last frame's total and the rolling average.</returns>
    private (double Last, double Average) SelfTotals()
    {
        var last = 0d;
        var average = 0d;

        foreach (var row in rows)
        {
            if (row.Excluded)
                continue;

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

        if (!ImGui.BeginTable("###NoireProfilerTable", 7, flags))
            return;

        try
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Scope", ImGuiTableColumnFlags.WidthStretch, 3f);
            ImGui.TableSetupColumn("Calls", ImGuiTableColumnFlags.WidthStretch, 0.9f);
            ImGui.TableSetupColumn("Self avg", ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.WidthStretch, 1.4f);
            // Beside the self time rather than at the end, because the two are read together: bytes are the same on
            // every machine and the milliseconds beside them are not.
            ImGui.TableSetupColumn("Self bytes", ImGuiTableColumnFlags.WidthStretch, 1.4f);
            ImGui.TableSetupColumn("Total avg", ImGuiTableColumnFlags.WidthStretch, 1.4f);
            ImGui.TableSetupColumn("Last", ImGuiTableColumnFlags.WidthStretch, 1.4f);
            // The last column takes whatever is left, so a handle on its right edge would have nothing to give ground
            // to and reads as broken.
            ImGui.TableSetupColumn("Longest", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoResize, 1.4f);
            ImGui.TableHeadersRow();

            ReadSortSpecs();

            // A click on a header reorders the same rows, so it needs the tree regrouped without a fresh snapshot.
            if (sortColumn != lastSortColumn || sortAscending != lastSortAscending)
            {
                lastSortColumn = sortColumn;
                lastSortAscending = sortAscending;

                Sort();
                treeDirty = true;
            }

            if (treeDirty)
            {
                BuildTree();
                treeDirty = false;
                flattenDirty = true;
            }

            // Applied to every node the profiler knows about, not merely the ones on screen, which is the whole point:
            // the branches that need opening are the ones currently hidden inside a collapsed parent.
            if (pendingTreeState is { } state)
            {
                foreach (var row in rows)
                    openState[row.Id] = state;

                pendingTreeState = null;
                flattenDirty = true;
            }

            if (flattenDirty)
            {
                FlattenVisible();
                flattenDirty = false;
            }

            // Only the rows on screen are submitted. The table scrolls, so a plugin with a few hundred scopes was
            // paying for every one of them on every frame to show the dozen that fit.
            var clipper = new ImGuiListClipper();
            clipper.Begin(visibleRows.Count);

            while (clipper.Step())
            {
                for (var position = clipper.DisplayStart; position < clipper.DisplayEnd; position++)
                    DrawRow(visibleRows[position]);
            }

            clipper.End();
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

        roots.Sort(compareRows);

        // Sorted here rather than as each branch is drawn, so a branch is ordered once per read instead of once per
        // frame it is open for.
        foreach (var list in children.Values)
            list.Sort(compareRows);

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
    /// Walks the tree once and writes out the rows a reader can actually reach, in the order they appear.
    /// </summary>
    /// <remarks>
    /// Everything inside a collapsed branch is left out, which is what makes the count the clipper is given the count
    /// of rows on screen rather than of scopes measured.
    /// </remarks>
    private void FlattenVisible()
    {
        visibleRows.Clear();
        drawn.Clear();

        foreach (var root in roots)
            FlattenBranch(root, depth: 0);
    }

    /// <summary>
    /// Adds a scope and, when it is open, everything measured inside it.
    /// </summary>
    /// <param name="row">The scope to add.</param>
    /// <param name="depth">How deep the branch is, drawn as indentation and used to stop a malformed chain from
    /// recursing forever.</param>
    private void FlattenBranch(UiProfileEntry row, int depth)
    {
        // A scope seen under two parents in one frame could otherwise be reached twice and, in the worst case, become
        // its own ancestor.
        if (depth > MaxDepth || !drawn.Add(row.Id))
            return;

        if (search.Length > 0 && !visible.Contains(row.Id))
            return;

        var hasChildren = children.TryGetValue(row.Id, out var list) && list.Count > 0;

        visibleRows.Add(new VisibleRow(row, depth, hasChildren));

        if (!hasChildren)
            return;

        var isRoot = string.Equals(row.Name, UiProfiler.RootScopeName, StringComparison.Ordinal);

        // A match's ancestors are forced open, or the match would sit behind a closed arrow and the search would look
        // like it had found nothing.
        var open = search.Length > 0 || (openState.TryGetValue(row.Id, out var stored) ? stored : isRoot);

        if (!open)
            return;

        foreach (var child in list!)
            FlattenBranch(child, depth + 1);
    }

    /// <summary>
    /// How deep the tree may go before it is assumed to be malformed.
    /// </summary>
    private const int MaxDepth = 32;

    /// <summary>
    /// Draws one scope's row.
    /// </summary>
    /// <param name="visibleRow">The scope to draw, with its place in the tree already resolved.</param>
    private void DrawRow(in VisibleRow visibleRow)
    {
        var row = visibleRow.Row;
        var leaf = !visibleRow.HasChildren;
        var defaultOpen = string.Equals(row.Name, UiProfiler.RootScopeName, StringComparison.Ordinal);

        ImGui.TableNextRow();

        // Set on the row rather than pushed as a text colour, so an excluded scope is marked across the full width
        // including the columns it has nothing written in. RowBg0 is the target the striping uses, so this replaces
        // the stripe rather than sitting under it and coming out two different shades on alternating rows.
        if (row.Excluded)
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ExcludedColour));

        ImGui.TableNextColumn();

        // Nothing is ever pushed onto the tree stack, so nothing has to be popped. That is what lets the clipper skip
        // a range of rows: a push inside a skipped range would never find its pop.
        var flags = ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.OpenOnArrow
            | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.NoTreePushOnOpen;

        if (leaf)
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.Bullet;

        // Depth is drawn rather than pushed, for the same reason.
        var indent = visibleRow.Depth * ImGui.GetTreeNodeToLabelSpacing();

        if (indent > 0f)
            ImGui.Indent(indent);

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

        // Asked here, while the node is still the last item submitted: the numeric cells below are items of their own
        // and would take that place. The node spans the full width, so this is a right click anywhere on the row.
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            NoireUI.Profiler.ToggleExcluded(row.Id);

        // The state is forced every frame, so a difference here is the reader having just clicked the arrow. Opening a
        // branch changes which rows exist, so the flattened list has to be walked again.
        if (!leaf && open != wanted && search.Length == 0)
        {
            openState[row.Id] = open;
            flattenDirty = true;
        }

        if (indent > 0f)
            ImGui.Unindent(indent);

        // Formatted into the stack rather than into strings. These figures are rolling averages, so they genuinely
        // move on every measured frame and no cache of formatted text can stay valid; writing into a span and handing
        // that to ImGui costs nothing per frame either way.
        Span<char> cell = stackalloc char[CellCapacity];

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(WriteCount(cell, row.Calls));

        ImGui.TableNextColumn();

        // Only the self average is coloured. The total flags every scope enclosing an expensive one, which points at
        // the page rather than at the widget actually spending the time.
        WriteCell(cell, row.SelfAverageMs, MsFormat, row.SelfAverageMs >= WarnMs);

        ImGui.TableNextColumn();

        // Coloured on any steady allocation at all rather than against a threshold. A widget that allocates every
        // frame is a finding whatever the amount, because it is garbage on the draw thread sixty times a second.
        WriteCell(cell, row.SelfAverageBytes, BytesFormat, row.SelfAverageBytes >= WarnBytes);

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(WriteValue(cell, row.AverageMs, MsFormat));

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(WriteValue(cell, row.LastMs, MsFormat));

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(WriteValue(cell, row.PeakMs, MsFormat));
    }

    /// <summary>
    /// Takes a fresh read and regroups the tree, but only when something behind them moved.
    /// </summary>
    /// <remarks>
    /// While tracking is on this runs every frame, because the figures are rolling averages and a frame rolls every
    /// frame. What the gate removes is every frame where nothing moved: all of them while tracking is off, and any
    /// where the profiler held still with the window left open.<br/>
    /// The tree cannot be regrouped less often than the rows are read. Its branches hold copies of the entries, so
    /// structure and figures are rebuilt by the same pass, and skipping it would draw last reading's numbers.
    /// </remarks>
    private void Refresh()
    {
        var generation = NoireUI.Profiler.Generation;
        var filtersMoved = showInactive != lastShowInactive || !string.Equals(search, lastSearch, StringComparison.Ordinal);

        if (!filtersMoved && generation == lastGeneration)
            return;

        // A moving generation alone rebuilds a few times a second rather than on every measured frame. While the
        // profiler is on, the figures move every frame, and reformatting a hundred rows sixty times a second made this
        // window the most expensive scope it displayed; the figures are rolling averages, so nothing readable is lost
        // at a slower cadence. A filter change still applies at once, because the reader just asked for it.
        if (!filtersMoved && NoireUI.Time - lastRefreshTime < RefreshInterval)
            return;

        lastRefreshTime = NoireUI.Time;
        lastGeneration = generation;
        lastShowInactive = showInactive;
        lastSearch = search;

        // Into a list this window owns, so a read costs nothing per frame beyond the entries themselves.
        NoireUI.Profiler.Snapshot(snapshot);

        rows.Clear();
        excludedCount = 0;

        // Everything the search excludes is kept, because a filter that dropped rows would also drop the parents a
        // match hangs from and the tree would fall apart into a flat list exactly when it is most wanted. Scopes that
        // did not run are a different matter: they are noise rather than context.
        foreach (var entry in snapshot)
        {
            if (entry.Excluded)
                excludedCount++;

            if (!showInactive && entry.Calls == 0 && entry.LastMs <= 0d)
                continue;

            rows.Add(entry);
        }

        Sort();

        treeDirty = true;
    }

    /// <summary>
    /// Sorts by the current column, largest first unless the header says otherwise.
    /// </summary>
    private void Sort() => rows.Sort(compareRows);

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
            Column.SelfBytes => left.SelfAverageBytes.CompareTo(right.SelfAverageBytes),
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
            .Append(" ms\tScopes: ").Append(rows.Count.ToString(CultureInfo.InvariantCulture));

        // Named on the paste, because the totals above it were summed without them and a reader given the numbers
        // without that fact would be unable to reconcile the columns with the total.
        if (excludedCount > 0)
        {
            clipboard.Append("\tExcluded from the totals: ")
                .Append(excludedCount.ToString(CultureInfo.InvariantCulture));
        }

        clipboard.AppendLine();

        clipboard.Append("Allocated: ")
            .Append(NoireUI.Profiler.TotalAverageBytes.ToString("N0", CultureInfo.InvariantCulture))
            .AppendLine(" bytes per frame");

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
        clipboard.AppendLine(
            "Scope\tPath\tCalls\tSelf (ms)\tLast (ms)\tLongest (ms)\tAverage (ms)\t" +
            "Self (bytes)\tTotal (bytes)\tPeak (bytes)\tExcluded");

        foreach (var row in rows)
        {
            clipboard.Append(row.Name).Append('\t')
                .Append(PathOf(row)).Append('\t')
                .Append(row.Calls.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(row.SelfAverageMs.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\t')
                .Append(row.LastMs.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\t')
                .Append(row.PeakMs.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\t')
                .Append(row.AverageMs.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\t')
                .Append(row.SelfAverageBytes.ToString("0", CultureInfo.InvariantCulture)).Append('\t')
                .Append(row.AverageBytes.ToString("0", CultureInfo.InvariantCulture)).Append('\t')
                .Append(row.PeakBytes.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .AppendLine(row.Excluded ? "yes" : string.Empty);
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
    /// How much stack one formatted cell is given. Comfortably past the longest a millisecond or byte figure reaches.
    /// </summary>
    private const int CellCapacity = 32;

    /// <summary>
    /// How much stack a summary line is given, which is longer than the widest the four figures on it can reach.
    /// </summary>
    private const int LineCapacity = 256;

    private const string MsFormat = "0.0000";
    private const string BytesFormat = "N0";

    /// <summary>
    /// Writes a value into <paramref name="buffer"/> and returns the part written.
    /// </summary>
    /// <remarks>
    /// Falls back to an empty span rather than throwing when the buffer is somehow too small. This is a diagnostic:
    /// a blank cell is a far better outcome than an exception taken while someone is looking at why their interface
    /// is slow.
    /// </remarks>
    private static ReadOnlySpan<char> WriteValue(Span<char> buffer, double value, string format)
        => value.TryFormat(buffer, out var written, format, CultureInfo.CurrentCulture)
            ? buffer[..written]
            : default;

    /// <summary>
    /// Writes a call count into <paramref name="buffer"/> and returns the part written.
    /// </summary>
    private static ReadOnlySpan<char> WriteCount(Span<char> buffer, int value)
        => value.TryFormat(buffer, out var written, default, CultureInfo.CurrentCulture)
            ? buffer[..written]
            : default;

    /// <summary>
    /// Draws one numeric cell, in <see cref="WarnColour"/> when it is over its threshold.
    /// </summary>
    /// <remarks>
    /// Coloured by pushing rather than with <c>TextColored</c>, which is printf-style and would read a percent sign in
    /// the text as a conversion.
    /// </remarks>
    private static void WriteCell(Span<char> buffer, double value, string format, bool warn)
    {
        var text = WriteValue(buffer, value, format);

        if (!warn)
        {
            ImGui.TextUnformatted(text);
            return;
        }

        using (UiPush.Color(ImGuiCol.Text, WarnColour))
            ImGui.TextUnformatted(text);
    }
}
