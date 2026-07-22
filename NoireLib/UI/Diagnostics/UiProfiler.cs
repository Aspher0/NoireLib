using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NoireLib.UI;

/// <summary>
/// What each part of the interface costs to build, per frame, by name.<br/>
/// Off by default and free when off. Turn it on, draw for a few seconds, and read <see cref="Snapshot()"/>: the answer to
/// "which widget is the expensive one" is a list rather than a guess.
/// </summary>
/// <remarks>
/// This measures the time spent <em>building</em> the draw data on the draw thread, which is the part a plugin controls
/// and the part an optimization pass moves. It is not the GPU cost of drawing the result, and it is not everything the
/// host times against your plugin: a host's own figure covers the whole draw callback, including the windowing and the
/// ImGui work around whatever is instrumented here.<br/>
/// Scopes nest, so every scope is reported twice over: <em>total</em> time, which includes everything measured inside
/// it, and <em>self</em> time, which does not. Self time is the one that adds up. Totalling the total column counts a
/// widget once for itself and again for every scope enclosing it, which is how a frame comes out looking several times
/// more expensive than it is.<br/>
/// Every drawing surface NoireUI ships opens its own scope, so turning this on attributes the frame without work at
/// the call sites. That holds structurally rather than by convention: a surface cannot obtain a draw list without
/// opening a scope in the same call, and an analyzer reports one that reaches for a list directly as a build error.
/// What is still unaccounted for is the ImGui work around the surfaces, which is what the root scope's self time
/// reports. See <see cref="RootScopeName"/>.
/// <see cref="NoireUI.Profile{TState}(string, TState, System.Action{TState})"/> puts your own code on the same list.
/// </remarks>
/// <example>
/// <code>
/// NoireUI.Profiler.Enabled = true;
///
/// foreach (var entry in NoireUI.Profiler.Snapshot())
///     PluginLog.Information($"{entry.Name}: {entry.SelfAverageMs:0.000} ms of its own");
/// </code>
/// </example>
public sealed class UiProfiler
{
    /// <summary>
    /// How much of the rolling average one frame is worth. Low enough that a frame competing with a texture upload does
    /// not read as a regression, high enough that a real change shows up within a second of drawing.
    /// </summary>
    private const double AverageWeight = 0.05d;

    private readonly object syncRoot = new();

    /// <summary>
    /// One entry per <em>call path</em>, not per name: a node is identified by its name and the node it sits inside.
    /// </summary>
    /// <remarks>
    /// Keying on the name alone was wrong in a way that quietly lost time. A helper called from several places, which
    /// is what a helper is, had one row whose parent was whichever caller ran last; every other caller then showed a
    /// self time reduced by a child that was nowhere under it, and the difference vanished from the tree. Keyed by
    /// path, each caller gets its own node and every branch adds up.<br/>
    /// Both halves of the key are integers: the parent's id rather than a built path string, and the name's
    /// <see cref="UiScopeName.Id"/> rather than the name. Nothing is composed or allocated per frame, and nothing is
    /// hashed that was not already an integer.
    /// </remarks>
    private readonly Dictionary<(int Parent, int Name), Node> nodes = new();

    /// <summary>
    /// The handle for <see cref="RootScopeName"/>, resolved once rather than compared as a string per open.
    /// </summary>
    private static readonly UiScopeName RootScope = UiScopeName.For(RootScopeName);

    private int nextNodeId = 1;
    private int currentFrame = -1;

    /// <summary>
    /// Backs <see cref="Generation"/>.
    /// </summary>
    private int generation;

    /// <summary>
    /// The node <see cref="RootScopeName"/> was given, so scopes opened outside it can still be hung from it.
    /// </summary>
    private int rootNodeId;

    private Node? rootNode;

    /// <summary>
    /// The scopes currently open on this thread, innermost last.
    /// </summary>
    /// <remarks>
    /// Per thread, because nesting is a property of a call stack rather than of the profiler: a scope opened on a
    /// background thread is not inside whatever the draw thread happens to be building at that moment, and charging it
    /// there would take the time away from a scope that never spent it.
    /// </remarks>
    [ThreadStatic]
    private static List<OpenScope>? openScopes;

    /// <summary>
    /// A scope that has been opened and not yet closed, and how much of its time has been spent inside scopes nested in
    /// it.
    /// </summary>
    private struct OpenScope
    {
        /// <summary>
        /// The node this scope accumulates into, resolved when it opened.
        /// </summary>
        /// <remarks>
        /// Carried rather than looked up again on the way out. Rebuilding the key at close time was wrong for any
        /// scope the root had adopted: the stack is empty by then, so the key came out with no parent, missed the node
        /// created when it opened, and quietly made a second one carrying the same id.
        /// </remarks>
        public Node Node;

        public int NodeId;
        public UiScopeName Name;
        public long Started;
        public long ChildTicks;

        /// <summary>
        /// The thread's allocation counter when this scope opened, so the scope's own allocation is the difference.
        /// </summary>
        public long StartedBytes;

        /// <summary>
        /// The counter on entry to <see cref="Open"/>, before the profiler did any of its own work.
        /// </summary>
        /// <remarks>
        /// The difference between this and <see cref="StartedBytes"/> is what the profiler spent resolving the scope,
        /// which is a node allocated the first time a call path is seen. The scope is not charged for it, and neither
        /// is whatever encloses it: the enclosing scope is charged from here rather than from
        /// <see cref="StartedBytes"/>, so the cost of measuring lands on nobody instead of on the parent.
        /// </remarks>
        public long EntryBytes;

        /// <summary>
        /// How many bytes were allocated inside scopes nested in this one. The byte equivalent of
        /// <see cref="ChildTicks"/>, and subtracted for the same reason: without it a parent looks responsible for
        /// garbage its children produced.
        /// </summary>
        public long ChildBytes;

        /// <summary>
        /// Whether <see cref="UiProfiler.TrackAllocations"/> was on when this scope opened.
        /// </summary>
        /// <remarks>
        /// Carried rather than read again on the way out, because the setting can move while a scope is open. Both
        /// ends of a difference have to have been read for the difference to mean anything: without this, a scope
        /// that opened while tracking was off and closed while it was on would report the thread's entire allocation
        /// since startup as its own.
        /// </remarks>
        public bool Tracked;
    }

    /// <summary>
    /// What one scope has cost, accumulating over the frame being drawn and rolled up when it ends.
    /// </summary>
    private sealed class Node
    {
        public int Id;
        public int ParentId;
        public string Name = string.Empty;

        /// <summary>
        /// The <see cref="UiScopeName.Id"/> half of the key this node is filed under, so a rehome can rebuild the key
        /// without hashing the name back into one.
        /// </summary>
        public int NameId;

        public long Ticks;
        public long SelfTicks;
        public int Calls;
        public int LastCalls;
        public double LastMs;
        public double AverageMs;
        public double PeakMs;
        public double SelfLastMs;
        public double SelfAverageMs;
        public bool Seeded;

        public long Bytes;
        public long SelfBytes;
        public long LastBytes;
        public double AverageBytes;
        public long PeakBytes;
        public long SelfLastBytes;
        public double SelfAverageBytes;

        /// <summary>
        /// Whether this scope is left out of the totals. See <see cref="SetExcluded"/>.
        /// </summary>
        /// <remarks>
        /// Held on the node rather than in a set of ids beside it, so the totals pass reads a field it already has in
        /// hand instead of probing a collection once per scope per read.
        /// </remarks>
        public bool Excluded;
    }

    /// <summary>
    /// Whether scopes are timed.<br/>
    /// Off by default. A disabled profiler reads one boolean per scope and does nothing else, so leaving the
    /// instrumentation in place costs nothing.
    /// </summary>
    /// <remarks>
    /// Turning it on costs two <see cref="Stopwatch"/> reads and a dictionary lookup per measured scope. That is small
    /// against what it measures, but it is not nothing: it is a diagnostic to switch on while you are looking, not a
    /// setting to ship enabled.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether scopes also record how many bytes they allocated. Requires <see cref="Enabled"/>.<br/>
    /// Off by default, separately from the timing, because it costs more per scope than the timing does.
    /// </summary>
    /// <remarks>
    /// Each measured scope reads the runtime's per-thread allocation counter twice, and that is a runtime call rather
    /// than a field read. An interface opening several hundred scopes a frame pays it several hundred times, which was
    /// measured at roughly half of what a scope costs at all.<br/>
    /// Switch it on while judging whether a change allocates, which is what the byte columns are for, and leave it off
    /// while reading milliseconds. The byte figures report 0 while it is off rather than holding their last values, so
    /// a reader cannot mistake a stale number for a live one. A scope open across a change to this setting reports 0
    /// bytes for that one scope, since a difference needs both of its ends.
    /// </remarks>
    public bool TrackAllocations { get; set; }

    /// <summary>
    /// The name to measure a whole draw callback under, so the profiler can account for every millisecond rather than
    /// only the ones something claimed.
    /// </summary>
    /// <remarks>
    /// Wrap your entire draw in a scope of this name and the table gains a row whose total is the same span your host
    /// times the plugin over, and whose self time is everything inside it that nothing else measured: the windowing,
    /// the ImGui work around the widgets, and any drawing not instrumented yet. That remainder is the number worth
    /// watching, because it is the part of the frame the profiler cannot yet explain.
    /// </remarks>
    /// <example>
    /// <code>
    /// pluginInterface.UiBuilder.Draw += () =&gt;
    ///     NoireUI.Profile(NoireUI.Profiler.RootScopeName, () =&gt; windowSystem.Draw());
    /// </code>
    /// </example>
    public const string RootScopeName = "ImGui Draw";

    /// <summary>
    /// What the root scope cost in total on average, or 0 when nothing is measuring one.<br/>
    /// Directly comparable to the figure a host reports for the plugin. See <see cref="RootScopeName"/>.
    /// </summary>
    public double RootAverageMs
    {
        get
        {
            lock (syncRoot)
                return nodes.TryGetValue((0, RootScope.Id), out var root) ? root.AverageMs : 0d;
        }
    }

    /// <summary>
    /// How much of the root scope nothing has accounted for: its own self time, which is every millisecond inside the
    /// draw that no measured scope claimed.
    /// </summary>
    public double UnaccountedAverageMs
    {
        get
        {
            lock (syncRoot)
                return nodes.TryGetValue((0, RootScope.Id), out var root) ? root.SelfAverageMs : 0d;
        }
    }

    /// <summary>
    /// Moves whenever the reported figures change, which is once per measured frame.
    /// </summary>
    /// <remarks>
    /// For anything displaying a snapshot. The figures only move when a frame rolls, so a reader that rebuilds its
    /// formatting every frame is rebuilding the same strings from the same numbers at whatever the frame rate is.
    /// Compare this against the value held from last time and rebuild only when it differs.<br/>
    /// Treated as opaque: it is not a frame number and not a count of anything, only a value that changes.
    /// </remarks>
    public int Generation
    {
        get
        {
            lock (syncRoot)
                return generation;
        }
    }

    /// <summary>
    /// Takes a read of what every measured scope has cost, ordered by self time, most expensive first.
    /// </summary>
    /// <returns>One entry per scope measured since the last <see cref="Reset"/>. Empty while disabled.</returns>
    public IReadOnlyList<UiProfileEntry> Snapshot()
    {
        lock (syncRoot)
        {
            if (nodes.Count == 0)
                return Array.Empty<UiProfileEntry>();

            var entries = new List<UiProfileEntry>(nodes.Count);

            Fill(entries);

            return entries;
        }
    }

    /// <summary>
    /// Takes the same read as <see cref="Snapshot()"/> into a list you own, allocating nothing when it is already big
    /// enough.
    /// </summary>
    /// <remarks>
    /// For a caller reading every frame, where a fresh list and a fresh set of entries per frame is garbage produced by
    /// the act of looking. The list is cleared first, and the order matches <see cref="Snapshot()"/>.
    /// </remarks>
    /// <param name="buffer">The list to fill. Cleared before anything is added.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="buffer"/> is <see langword="null"/>.</exception>
    public void Snapshot(List<UiProfileEntry> buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        buffer.Clear();

        lock (syncRoot)
        {
            if (nodes.Count == 0)
                return;

            Fill(buffer);
        }
    }

    /// <summary>
    /// Adds one entry per node, most expensive first. Callers hold <see cref="syncRoot"/>.
    /// </summary>
    private void Fill(List<UiProfileEntry> entries)
    {
        foreach (var node in nodes.Values)
        {
            entries.Add(new UiProfileEntry(
                node.Id,
                node.ParentId,
                node.Name,
                node.LastCalls,
                node.LastMs,
                node.AverageMs,
                node.PeakMs,
                node.SelfLastMs,
                node.SelfAverageMs,
                node.LastBytes,
                node.AverageBytes,
                node.PeakBytes,
                node.SelfLastBytes,
                node.SelfAverageBytes,
                node.Excluded));
        }

        entries.Sort(static (left, right) => right.SelfAverageMs.CompareTo(left.SelfAverageMs));
    }

    /// <summary>
    /// The sum of every scope's self time, averaged. This is the figure that means something: because self time
    /// excludes nested scopes, adding it up counts each piece of work exactly once.
    /// </summary>
    /// <remarks>
    /// It accounts for the instrumented work only. The difference between this and what your host reports for the
    /// plugin is everything not measured here, which includes the host's own windowing and the ImGui calls around it.
    /// <br/>
    /// Scopes marked through <see cref="SetExcluded"/> are left out.
    /// </remarks>
    public double TotalAverageMs
    {
        get
        {
            var total = 0d;

            lock (syncRoot)
            {
                foreach (var node in nodes.Values)
                {
                    if (!node.Excluded)
                        total += node.SelfAverageMs;
                }
            }

            return total;
        }
    }

    /// <summary>
    /// The sum of every scope's self allocation, averaged: how many bytes a frame of interface produces.<br/>
    /// Reads 0 unless both <see cref="Enabled"/> and <see cref="TrackAllocations"/> are on.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="TotalAverageMs"/>, and the number to watch when judging whether a change helped.
    /// Self allocation excludes nested scopes, so adding it up counts each allocation exactly once.<br/>
    /// A steady figure above zero is garbage produced every frame on the draw thread, which is the one thread a plugin
    /// cannot afford a collection on.<br/>
    /// Unlike <see cref="TotalAverageMs"/>, this reads 0 rather than the last figures once tracking is switched off.
    /// It is surfaced on <see cref="UiDiagnostics.Snapshot"/>, which a plugin may call every frame, so it has to cost
    /// nothing when nothing is being measured; reporting a stale number as though it were live would also be worse
    /// than reporting none.<br/>
    /// Scopes marked through <see cref="SetExcluded"/> are left out.
    /// </remarks>
    public double TotalAverageBytes
    {
        get
        {
            if (!Enabled || !TrackAllocations)
                return 0d;

            var total = 0d;

            lock (syncRoot)
            {
                foreach (var node in nodes.Values)
                {
                    if (!node.Excluded)
                        total += node.SelfAverageBytes;
                }
            }

            return total;
        }
    }

    /// <summary>
    /// Leaves one scope out of <see cref="TotalAverageMs"/> and <see cref="TotalAverageBytes"/>, or puts it back.
    /// </summary>
    /// <remarks>
    /// For the cost you have decided is not part of what you are measuring: a diagnostic window open beside the thing
    /// being profiled, a debug overlay, a page you already know about and are not working on. The scope keeps being
    /// measured and keeps reporting its own figures; only the sums stop counting it.<br/>
    /// One node, not a branch. The totals are sums of self time, so a scope's mark removes exactly the figure its own
    /// row shows and nothing else; excluding a whole branch means marking the rows in it. Marks are held on the nodes,
    /// so <see cref="Reset"/> forgets them along with everything else it forgets.
    /// </remarks>
    /// <param name="id">The scope's <see cref="UiProfileEntry.Id"/>.</param>
    /// <param name="excluded">Whether to leave it out of the totals.</param>
    /// <returns><see langword="true"/> when a scope with that id was found.</returns>
    public bool SetExcluded(int id, bool excluded)
    {
        lock (syncRoot)
        {
            var node = FindLocked(id);

            if (node == null)
                return false;

            if (node.Excluded != excluded)
            {
                node.Excluded = excluded;

                // The totals have just moved without a frame rolling, so a reader gated on this would otherwise keep
                // showing the sum that included the scope until the next measurement.
                generation++;
            }

            return true;
        }
    }

    /// <summary>
    /// Flips whether one scope counts towards the totals. See <see cref="SetExcluded"/>.
    /// </summary>
    /// <param name="id">The scope's <see cref="UiProfileEntry.Id"/>.</param>
    /// <returns>Whether the scope is now excluded. <see langword="false"/> when no scope has that id.</returns>
    public bool ToggleExcluded(int id)
    {
        lock (syncRoot)
        {
            var node = FindLocked(id);

            if (node == null)
                return false;

            node.Excluded = !node.Excluded;
            generation++;

            return node.Excluded;
        }
    }

    /// <summary>
    /// Whether one scope is currently left out of the totals. See <see cref="SetExcluded"/>.
    /// </summary>
    /// <param name="id">The scope's <see cref="UiProfileEntry.Id"/>.</param>
    /// <returns><see langword="true"/> when the scope exists and is excluded.</returns>
    public bool IsExcluded(int id)
    {
        lock (syncRoot)
            return FindLocked(id) is { Excluded: true };
    }

    /// <summary>
    /// How many scopes are currently left out of the totals. See <see cref="SetExcluded"/>.
    /// </summary>
    public int ExcludedCount
    {
        get
        {
            var count = 0;

            lock (syncRoot)
            {
                foreach (var node in nodes.Values)
                {
                    if (node.Excluded)
                        count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// Counts every scope towards the totals again, without forgetting any measurement. See <see cref="SetExcluded"/>.
    /// </summary>
    public void ClearExclusions()
    {
        lock (syncRoot)
        {
            var changed = false;

            foreach (var node in nodes.Values)
            {
                if (!node.Excluded)
                    continue;

                node.Excluded = false;
                changed = true;
            }

            if (changed)
                generation++;
        }
    }

    /// <summary>
    /// Finds a node by the id its entries are reported under. Callers hold <see cref="syncRoot"/>.
    /// </summary>
    /// <remarks>
    /// A scan rather than a second dictionary keyed by id. The nodes are keyed by call path because that is what a
    /// measurement resolves against, and marking a scope is something a reader does by hand every so often, so a second
    /// index maintained on every first-seen call path would cost more per frame than it saves.
    /// </remarks>
    private Node? FindLocked(int id)
    {
        foreach (var node in nodes.Values)
        {
            if (node.Id == id)
                return node;
        }

        return null;
    }

    /// <summary>
    /// Forgets every measurement taken so far, including the peaks.
    /// </summary>
    /// <remarks>
    /// Worth calling right before the interaction you actually want to measure. The peak is a high-water mark, so
    /// without a reset it keeps reporting the frame that built the window for the first time.<br/>
    /// The nodes go with it, and so do the marks <see cref="SetExcluded"/> put on them. Use
    /// <see cref="ClearExclusions"/> to put excluded scopes back into the totals without discarding what they measured.
    /// </remarks>
    public void Reset()
    {
        lock (syncRoot)
        {
            nodes.Clear();
            nextNodeId = 1;
            rootNodeId = 0;
            rootNode = null;
            currentFrame = -1;

            // Bumped here too: clearing the nodes changes what a reader would display just as surely as a frame
            // rolling does, and one that only watched frames would keep showing rows that no longer exist.
            generation++;
        }

        openScopes?.Clear();
    }

    /// <summary>
    /// The name of the innermost scope currently open on this thread, or <see langword="null"/> when none is.
    /// </summary>
    /// <remarks>
    /// For a caller deciding whether to open a scope at all. A node is keyed on its name and its parent, so a surface
    /// that opens its own name again inside itself does not get one row: it gets a second node under the first, and
    /// the outer row loses its time to a same-named child. Reading this is how <see cref="UiDraw"/> declines to open
    /// the duplicate.<br/>
    /// Handed back as the handle rather than the name, so the caller compares references instead of strings.
    /// </remarks>
    internal static UiScopeName? InnermostScope
    {
        get
        {
            var stack = openScopes;
            return stack is { Count: > 0 } ? stack[^1].Name : null;
        }
    }

    /// <summary>
    /// Starts timing a scope. Pair with <see cref="Close"/>, which <see cref="UiProfileScope"/> does.
    /// </summary>
    /// <param name="name">The scope's name, or <see langword="null"/> for nothing to measure.</param>
    /// <returns>The timestamp the scope started at, or 0 when the profiler is off.</returns>
    internal long Open(UiScopeName? name)
    {
        if (!Enabled || name == null)
            return 0L;

        var tracking = TrackAllocations;

        // Read before the profiler touches anything, including the list it is about to grow.
        var entryBytes = tracking ? GC.GetAllocatedBytesForCurrentThread() : 0L;

        var stack = openScopes ??= new List<OpenScope>();
        var parentId = stack.Count > 0 ? stack[^1].NodeId : 0;

        // The frame number is read here, on the outermost scope, and nowhere else. Reading it is not a field read:
        // outside a test it is an ImGui call across the native boundary, and doing that once per scope close meant
        // several hundred of them a frame in an interface of any size. Every scope nested in this one is in the same
        // frame by construction, so one read answers for all of them.
        var rolling = stack.Count == 0;

        // A scope opened with nothing above it still belongs to the frame, even though it is not inside the root's
        // span: NoireUI's own frame pass runs from a second draw handler, alongside the host's rather than within it,
        // and the host times both. Adopted by the root so the tree has one trunk instead of a scattering of orphans.
        if (parentId == 0 && !ReferenceEquals(name, RootScope))
            parentId = rootNodeId;

        Node resolved;

        // Resolved on the way in rather than on the way out, because a scope opened inside this one needs this node's
        // id to know where it belongs.
        lock (syncRoot)
        {
            if (rolling)
                RollFrameLocked();

            if (!nodes.TryGetValue((parentId, name.Id), out var node))
            {
                node = new Node { Id = nextNodeId++, ParentId = parentId, Name = name.Name, NameId = name.Id };
                nodes[(parentId, name.Id)] = node;

                if (parentId == 0 && ReferenceEquals(name, RootScope))
                {
                    rootNodeId = node.Id;
                    rootNode = node;

                    AdoptOrphansLocked();
                }
            }

            resolved = node;
        }

        stack.Add(new OpenScope
        {
            Node = resolved,
            NodeId = resolved.Id,
            Name = name,
            ChildTicks = 0L,
            ChildBytes = 0L,
            EntryBytes = entryBytes,
            Tracked = tracking,
        });

        // Both counters are read last, once the profiler's own bookkeeping is done. Resolving a node allocates one the
        // first time a call path is seen, and charging that to the scope being opened would report garbage the caller
        // never produced.
        var index = stack.Count - 1;
        var frame = stack[index];

        frame.StartedBytes = tracking ? GC.GetAllocatedBytesForCurrentThread() : 0L;
        frame.Started = Stopwatch.GetTimestamp();

        stack[index] = frame;

        return frame.Started;
    }

    /// <summary>
    /// Hands every scope measured before the root existed over to it. Callers hold <see cref="syncRoot"/>.
    /// </summary>
    /// <remarks>
    /// Adoption at open time only works forward: NoireUI's own draw handler can run before the host's, so on the first
    /// frames its scopes open with no root to be adopted by and become real orphans. Once the root turns up they would
    /// sit outside the tree forever, and the same scope would appear twice, once stranded and once adopted. Rehoming
    /// them here is what makes the tree have a single trunk from the first frame rather than from the second.
    /// </remarks>
    private void AdoptOrphansLocked()
    {
        List<(int Parent, int Name)>? orphans = null;

        foreach (var pair in nodes)
        {
            if (pair.Key.Parent != 0 || pair.Value.Id == rootNodeId)
                continue;

            (orphans ??= new List<(int, int)>()).Add(pair.Key);
        }

        if (orphans == null)
            return;

        foreach (var key in orphans)
        {
            var node = nodes[key];
            nodes.Remove(key);

            node.ParentId = rootNodeId;

            // A node may already exist under the root for the same name, from a frame after the root appeared. The
            // older readings are folded into it rather than replacing it, so nothing measured is thrown away.
            if (nodes.TryGetValue((rootNodeId, key.Name), out var existing))
            {
                existing.Ticks += node.Ticks;
                existing.SelfTicks += node.SelfTicks;
                existing.Bytes += node.Bytes;
                existing.SelfBytes += node.SelfBytes;
                existing.Calls += node.Calls;
                existing.PeakMs = Math.Max(existing.PeakMs, node.PeakMs);
                existing.PeakBytes = Math.Max(existing.PeakBytes, node.PeakBytes);

                // A mark on either survives the merge, because the two were always the same scope and dropping it
                // would put a scope the reader had excluded back into the totals without saying so.
                existing.Excluded |= node.Excluded;
                continue;
            }

            nodes[(rootNodeId, key.Name)] = node;
        }
    }

    /// <summary>
    /// Finishes timing a scope opened by <see cref="Open"/>, charging its time to itself and to whatever encloses it.
    /// </summary>
    /// <param name="name">The scope's name, as it was passed to <see cref="Open"/>.</param>
    /// <param name="started">The timestamp <see cref="Open"/> returned.</param>
    internal void Close(UiScopeName? name, long started)
    {
        if (started == 0L)
            return;

        // Read first, before this method's own bookkeeping, for the same reason Open reads last. Between them the two
        // reads bracket the caller's work and nothing else.
        var tracking = TrackAllocations;
        var endedBytes = tracking ? GC.GetAllocatedBytesForCurrentThread() : 0L;

        var stack = openScopes;

        // Popped even when the profiler was switched off mid-scope, or the entry would be left open and every later
        // scope would be charged as nested inside a scope that never closed.
        if (stack == null || stack.Count == 0)
            return;

        var index = stack.Count - 1;
        var frame = stack[index];

        // Scopes are disposed in the reverse of the order they were opened, so anything else means a caller has kept
        // one past its block. Unwinding to it is better than silently attributing the rest of the frame to it.
        // Compared by reference, which interning makes equivalent to comparing the names and far cheaper.
        if (!ReferenceEquals(frame.Name, name))
        {
            // Walked by hand rather than with FindLastIndex and a predicate. A predicate capturing name captures a
            // parameter, and the closure for that is built on entry to the method rather than where it is used, so
            // every close would allocate one whether or not this branch ran.
            var found = -1;

            for (var candidate = stack.Count - 1; candidate >= 0; candidate--)
            {
                if (!ReferenceEquals(stack[candidate].Name, name))
                    continue;

                found = candidate;
                break;
            }

            if (found < 0)
                return;

            index = found;
            frame = stack[index];
        }

        stack.RemoveRange(index, stack.Count - index);

        var elapsed = Stopwatch.GetTimestamp() - frame.Started;
        var self = Math.Max(0L, elapsed - frame.ChildTicks);

        // Both ends of the difference have to have been read. A scope spanning a change to TrackAllocations reports
        // no bytes rather than the counter's absolute value.
        var measured = tracking && frame.Tracked;

        var allocated = measured ? Math.Max(0L, endedBytes - frame.StartedBytes) : 0L;
        var selfAllocated = Math.Max(0L, allocated - frame.ChildBytes);

        // Charged to the enclosing scope so that its own self figures exclude this one.
        if (stack.Count > 0)
        {
            var parent = stack[^1];
            parent.ChildTicks += elapsed;

            // Charged from this scope's entry rather than from where its own measurement began, so the parent is not
            // billed for the node the profiler allocated on first seeing this call path. Identical in the steady
            // state, and different only on the frame a scope first runs.
            if (measured)
                parent.ChildBytes += Math.Max(0L, endedBytes - frame.EntryBytes);

            stack[^1] = parent;
        }

        if (!Enabled)
            return;

        var node = frame.Node;

        lock (syncRoot)
        {
            node.Ticks += elapsed;
            node.SelfTicks += self;
            node.Bytes += allocated;
            node.SelfBytes += selfAllocated;
            node.Calls++;

            // A scope the root adopted ran alongside the root rather than inside it, so the root's own clock and
            // counter never saw it. Added to the root's totals here, and deliberately not to its self figures, which
            // keeps the branch adding up: the root's self plus every child, adopted or nested, is the root's total.
            if (stack.Count == 0 && rootNode != null && node.ParentId == rootNodeId && node.Id != rootNodeId)
            {
                rootNode.Ticks += elapsed;
                rootNode.Bytes += allocated;
            }
        }
    }

    /// <summary>
    /// Closes off the previous frame's totals when the frame number moves. Callers hold <see cref="syncRoot"/>.
    /// </summary>
    /// <remarks>
    /// Rolled up here, on the way into a measurement, rather than from the hub's per-frame pass. A scope can be measured
    /// by a plugin that never registered a drawable and so never runs that pass, and a profiler that only reported for
    /// hub-driven interfaces would be silently empty for exactly the plugin most likely to be looking at it.<br/>
    /// Called only when a scope opens with nothing above it, which is once per frame in a nested interface rather than
    /// once per scope. The previous frame's figures are therefore closed off before any of this frame's measurements
    /// land, which is what the roll has to guarantee and is the same point in the sequence as closing the first scope
    /// of the frame.
    /// </remarks>
    private void RollFrameLocked()
    {
        var frame = NoireUI.FrameCount;

        if (frame == currentFrame)
            return;

        currentFrame = frame;

        // Moved here rather than on every close: this is the one place the reported figures change, so a reader that
        // watches it rebuilds exactly as often as there is something new to show.
        generation++;

        foreach (var node in nodes.Values)
        {
            // A scope that did not run this frame reports zero rather than holding its last value, so a widget that has
            // been closed stops contributing to the average instead of looking permanently expensive.
            var ms = ToMilliseconds(node.Ticks);
            var selfMs = ToMilliseconds(node.SelfTicks);

            node.LastMs = ms;
            node.SelfLastMs = selfMs;
            node.LastCalls = node.Calls;
            node.LastBytes = node.Bytes;
            node.SelfLastBytes = node.SelfBytes;

            if (node.Seeded)
            {
                node.AverageMs = (node.AverageMs * (1d - AverageWeight)) + (ms * AverageWeight);
                node.SelfAverageMs = (node.SelfAverageMs * (1d - AverageWeight)) + (selfMs * AverageWeight);
                node.AverageBytes = (node.AverageBytes * (1d - AverageWeight)) + (node.Bytes * AverageWeight);
                node.SelfAverageBytes = (node.SelfAverageBytes * (1d - AverageWeight)) + (node.SelfBytes * AverageWeight);
            }
            else
            {
                node.AverageMs = ms;
                node.SelfAverageMs = selfMs;
                node.AverageBytes = node.Bytes;
                node.SelfAverageBytes = node.SelfBytes;
            }

            node.Seeded = true;

            if (ms > node.PeakMs)
                node.PeakMs = ms;

            if (node.Bytes > node.PeakBytes)
                node.PeakBytes = node.Bytes;

            node.Ticks = 0L;
            node.SelfTicks = 0L;
            node.Bytes = 0L;
            node.SelfBytes = 0L;
            node.Calls = 0;
        }
    }

    /// <summary>
    /// Converts a tick count to milliseconds.
    /// </summary>
    private static double ToMilliseconds(long ticks)
        => ticks == 0L ? 0d : Stopwatch.GetElapsedTime(0L, ticks).TotalMilliseconds;
}
