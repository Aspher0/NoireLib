using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// What each part of the interface costs to build, per frame, by name. Off by default and free when off.
/// </summary>
/// <remarks>
/// Measures the time spent building the draw data on the draw thread: not the GPU cost, and not the host's whole
/// draw callback, which also includes windowing and the ImGui work around what is instrumented here.<br/>
/// Every surface NoireUI ships opens its own scope; reaching for a draw list without one is a build error, so
/// coverage holds structurally. What that leaves out is reported as the root scope's self time. See
/// <see cref="RootScopeName"/>. <see cref="NoireUI.Profile{TState}(string, TState, System.Action{TState})"/> adds
/// your own code to the same list.
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
    /// How much of the rolling average one frame is worth.
    /// </summary>
    private const double AverageWeight = 0.05d;

    private readonly object syncRoot = new();

    /// <summary>
    /// One entry per <em>call path</em>, not per name: a node is identified by its name and the node it sits inside.
    /// </summary>
    /// <remarks>
    /// Keyed on two integers: the parent's node id and the name's <see cref="UiScopeName.Id"/>. Nothing is composed
    /// or allocated per frame, and nothing is hashed that was not already an integer.
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
    /// Bumped by <see cref="Reset"/>, so a per-thread memo filled before it stops answering. See <see cref="ThreadState"/>.
    /// </summary>
    private int resetStamp;

    /// <summary>
    /// The node <see cref="RootScopeName"/> was given, so scopes opened outside it can still be hung from it.
    /// </summary>
    private int rootNodeId;

    private Node? rootNode;

    /// <summary>
    /// Everything the profiler keeps per thread, in one object so the hot path pays one thread-local read rather than
    /// one per piece.
    /// </summary>
    /// <remarks>
    /// Held per thread because nesting follows the call stack: a scope opened on a background thread must not be
    /// charged into whatever the draw thread is currently building.
    /// </remarks>
    private sealed class ThreadState
    {
        /// <summary>
        /// The scopes currently open on this thread, innermost last, in the first <see cref="Depth"/> slots.
        /// </summary>
        /// <remarks>
        /// A plain array written through by <see langword="ref"/> rather than a <see cref="List{T}"/>, avoiding the
        /// copies a list's indexer and <c>Add</c> make on this struct. A popped slot is left to be overwritten;
        /// everything it references still lives in <see cref="nodes"/>.
        /// </remarks>
        public OpenScope[] Stack = new OpenScope[64];

        /// <summary>How many scopes are open: the next slot of <see cref="Stack"/> to fill.</summary>
        public int Depth;

        /// <summary>
        /// The profiler the memo below was filled for, so two profilers on one thread cannot read each other's nodes.
        /// </summary>
        public UiProfiler? MemoOwner;

        /// <summary>
        /// The <see cref="resetStamp"/> the memo was filled at. A reset makes every node in it a dead object.
        /// </summary>
        public int MemoStamp;

        /// <summary>
        /// The node each call site resolved to last, indexed by <see cref="UiScopeName.Id"/>.
        /// </summary>
        /// <remarks>
        /// Lets a warm scope resolve without the lock or the hash table.
        /// </remarks>
        public Node?[] MemoNodes = new Node?[64];

        /// <summary>
        /// The parent each memo entry was resolved under. Part of the hit check because one call site can appear under
        /// several parents, and a memo that ignored the parent would file a widget's time under whichever branch
        /// happened to draw it first.
        /// </summary>
        public int[] MemoParents = new int[64];
    }

    [ThreadStatic]
    private static ThreadState? threadState;

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
        /// Carried rather than looked up again on the way out: for a scope the root adopted, rebuilding the key at
        /// close time would find no parent on the empty stack and create a duplicate node.
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
        /// The gap between this and <see cref="StartedBytes"/> is what resolving the scope allocated. Neither the
        /// scope nor its parent is charged for it: the parent is charged from here rather than from
        /// <see cref="StartedBytes"/>.
        /// </remarks>
        public long EntryBytes;

        /// <summary>
        /// How many bytes were allocated inside scopes nested in this one, the byte equivalent of
        /// <see cref="ChildTicks"/>; subtracted so a parent is not charged for its children's allocations.
        /// </summary>
        public long ChildBytes;

        /// <summary>
        /// Whether <see cref="UiProfiler.TrackAllocations"/> was on when this scope opened.
        /// </summary>
        /// <remarks>
        /// Carried rather than read again on close, since the setting can change while a scope is open. Without it, a
        /// scope that opened with tracking off and closed with it on would report the thread's entire allocation
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
        /// Held on the node rather than in a separate set of ids, so the totals pass reads a field instead of probing
        /// a collection per scope.
        /// </remarks>
        public bool Excluded;
    }

    /// <summary>
    /// Backs <see cref="Enabled"/>. A field rather than an auto-property so hot paths read it without a call; read
    /// on every scope of every frame.
    /// </summary>
    private bool enabled;

    /// <summary>
    /// Backs <see cref="TrackAllocations"/>, as a field for the reason <see cref="enabled"/> is one.
    /// </summary>
    private bool trackAllocations;

    /// <summary>
    /// Backs <see cref="Detailed"/>, as a field for the reason <see cref="enabled"/> is one.
    /// </summary>
    private bool detailed;

    /// <summary>
    /// Whether scopes are timed. Off by default; a disabled profiler reads one boolean per scope and does nothing
    /// else.
    /// </summary>
    /// <remarks>
    /// Costs two <see cref="Stopwatch"/> reads and a memoized node lookup per measured scope when on: a diagnostic
    /// to switch on while looking, not a setting to ship enabled.
    /// </remarks>
    public bool Enabled
    {
        get => enabled;
        set => enabled = value;
    }

    /// <summary>
    /// Whether scopes also record how many bytes they allocated. Requires <see cref="Enabled"/>. Off by default,
    /// separately from timing, since it costs more per scope.
    /// </summary>
    /// <remarks>
    /// Reads the runtime's per-thread allocation counter twice per scope, a runtime call rather than a field read,
    /// roughly doubling what a scope costs to measure.<br/>
    /// Bytes read 0 while this is off rather than holding stale values. A scope open across a change to this
    /// setting reports 0 bytes for that scope.
    /// </remarks>
    public bool TrackAllocations
    {
        get => trackAllocations;
        set => trackAllocations = value;
    }

    /// <summary>
    /// Whether the library's per-method scopes are measured as rows of their own, such as <c>NoireShapes.Glow</c>.
    /// Requires <see cref="Enabled"/>. Off by default.
    /// </summary>
    /// <remarks>
    /// Fine rows are most of what measuring costs: a decorated window opens a scope per shape painted, several
    /// hundred a frame, against a few dozen widget and surface scopes.<br/>
    /// A method scope that is not measured folds into the scope around it, so its time still lands in the widget or
    /// surface that spent it; the row only stops being listed separately.
    /// </remarks>
    public bool Detailed
    {
        get => detailed;
        set => detailed = value;
    }

    /// <summary>
    /// Both switches a per-method scope needs, in one read for the gate that asks on every shape drawn.
    /// </summary>
    internal bool MeasuringMethods => enabled && detailed;

    /// <summary>
    /// The name to measure a whole draw callback under, so the profiler can account for every millisecond rather than
    /// only the ones something claimed.
    /// </summary>
    /// <remarks>
    /// Wrapping the whole draw in a scope of this name gives the table a row whose total matches what a host times
    /// the plugin over, and whose self time is whatever nothing else measured: windowing, the ImGui work around
    /// widgets, and anything not yet instrumented.
    /// </remarks>
    /// <example>
    /// <code>
    /// pluginInterface.UiBuilder.Draw += () =&gt;
    ///     NoireUI.Profile(NoireUI.Profiler.RootScopeName, () =&gt; windowSystem.Draw());
    /// </code>
    /// </example>
    public const string RootScopeName = "ImGui Draw";

    /// <summary>
    /// What the root scope cost in total on average, or 0 when nothing is measuring one. Directly comparable to the
    /// figure a host reports for the plugin. See <see cref="RootScopeName"/>.
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
    /// For a caller displaying a snapshot: figures only move when a frame rolls, so comparing this against the value
    /// held from last time and rebuilding only when it differs avoids redoing the same formatting every frame.
    /// Treated as opaque, not a frame number or a count of anything.
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
    /// For a caller reading every frame, where fresh entries every frame would be garbage produced by looking.
    /// Cleared first; order matches <see cref="Snapshot()"/>.
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
    /// The sum of every scope's self time, averaged. Counts each piece of work exactly once, since self time
    /// excludes nested scopes.
    /// </summary>
    /// <remarks>
    /// Accounts for instrumented work only; the gap to what a host reports for the plugin is everything not
    /// measured here, including its own windowing and the ImGui calls around it.<br/>
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
    /// The sum of every scope's self allocation, averaged: how many bytes a frame of interface produces. Reads 0
    /// unless both <see cref="Enabled"/> and <see cref="TrackAllocations"/> are on.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="TotalAverageMs"/>; excludes nested scopes, so each allocation is counted
    /// exactly once.<br/>
    /// Unlike <see cref="TotalAverageMs"/>, reads 0 rather than the last figures once tracking is off, since it is
    /// surfaced on <see cref="UiDiagnostics.Snapshot"/>, which a plugin may call every frame.<br/>
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
    /// For a cost you have decided is not part of what you are measuring, such as a diagnostic window open beside
    /// the thing being profiled. The scope keeps being measured and keeps reporting its own figures; only the sums
    /// stop counting it.<br/>
    /// Marks one node, not a branch: excluding a whole branch means marking each row in it. Marks are held on the
    /// nodes, so <see cref="Reset"/> forgets them along with everything else it forgets.
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

                // Totals moved outside the normal frame roll; bump so a Generation-gated reader picks it up
                // immediately.
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
    /// A scan rather than a second dictionary keyed by id: nodes are keyed by call path, and a second index
    /// maintained per first-seen call path would cost more than the scan saves.
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
    /// The peak is a high-water mark: without a reset it keeps reporting the frame that first built the window.
    /// Nodes go with it, and so do the marks <see cref="SetExcluded"/> put on them; use
    /// <see cref="ClearExclusions"/> to put excluded scopes back without discarding what they measured.
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

            // Bumped here too: clearing the nodes changes the display as much as a frame rolling does.
            generation++;

            // Bumping this makes every thread's memo entries unreachable rather than a route back to a discarded node.
            resetStamp++;
        }

        // Only the calling thread's state is reachable here; other threads notice through the stamp above.
        // Cleared rather than truncated so nothing keeps the discarded nodes alive.
        if (threadState is { } ts)
        {
            ts.Depth = 0;
            ts.MemoOwner = null;
            Array.Clear(ts.Stack);
            Array.Clear(ts.MemoNodes);
        }
    }

    /// <summary>
    /// Starts timing a scope. Pair with <see cref="Close"/>, which <see cref="UiProfileScope"/> does.
    /// </summary>
    /// <remarks>
    /// A scope whose name is already the innermost open one is declined (returns 0), since opening it again would
    /// make a child row of the same name and drain the outer row's self time into it. Names are interned, so the
    /// check is one reference compare.
    /// </remarks>
    /// <param name="name">The scope's name, or <see langword="null"/> for nothing to measure.</param>
    /// <returns>The timestamp the scope started at, or 0 when nothing is measured.</returns>
    internal long Open(UiScopeName? name)
    {
        if (!enabled || name == null)
            return 0L;

        var ts = threadState ??= new ThreadState();
        var depth = ts.Depth;
        var stack = ts.Stack;

        if (depth > 0 && ReferenceEquals(stack[depth - 1].Name, name))
            return 0L;

        // Read before the profiler touches anything it might allocate, so its own bookkeeping is charged to nobody.
        var tracking = trackAllocations;
        var entryBytes = tracking ? GC.GetAllocatedBytesForCurrentThread() : 0L;

        // Read only here, on scopes with nothing above them, since every scope nested in one is in the same frame by
        // construction. NoireUI.FrameCount is an ImGui call across the native boundary rather than a field read, so
        // this keeps it to once per frame instead of once per scope. The lock is taken only when the frame has
        // moved, and RollFrameLocked re-reads the number under the lock, so two racing threads roll the frame once
        // between them.
        if (depth == 0 && NoireUI.FrameCount != currentFrame)
        {
            lock (syncRoot)
                RollFrameLocked();
        }

        var parentId = depth > 0 ? stack[depth - 1].NodeId : 0;

        // A scope with nothing above it still belongs to the frame: NoireUI's own frame pass runs from a second draw
        // handler alongside the host's, not within it. Adopted by the root so the tree has one trunk instead of
        // scattered orphans.
        if (parentId == 0 && !ReferenceEquals(name, RootScope))
            parentId = rootNodeId;

        // Resolved on the way in, since a scope opened inside this one needs this node's id. Checked inline rather
        // than through a helper: an array index and two integer compares instead of a lock and a hash. Parent is
        // part of the check because one call site can appear under several parents; ignoring it would file a
        // widget's time under whichever branch drew it first.
        var nameId = name.Id;
        Node? resolved = null;

        if (ReferenceEquals(ts.MemoOwner, this) && ts.MemoStamp == resetStamp
            && nameId < ts.MemoNodes.Length && ts.MemoParents[nameId] == parentId)
        {
            resolved = ts.MemoNodes[nameId];
        }

        resolved ??= Resolve(ts, name, parentId);

        if (depth == stack.Length)
        {
            Array.Resize(ref ts.Stack, depth * 2);
            stack = ts.Stack;
        }

        // Written through a ref rather than built and assigned, so pushing a scope copies nothing.
        ref var slot = ref stack[depth];

        slot.Node = resolved;
        slot.NodeId = resolved.Id;
        slot.Name = name;
        slot.ChildTicks = 0L;
        slot.ChildBytes = 0L;
        slot.EntryBytes = entryBytes;
        slot.Tracked = tracking;

        ts.Depth = depth + 1;

        // Read last, once the profiler's own bookkeeping is done: resolving a node allocates the first time a call
        // path is seen, and charging that to the scope being opened would report garbage the caller never produced.
        slot.StartedBytes = tracking ? GC.GetAllocatedBytesForCurrentThread() : 0L;
        slot.Started = Stopwatch.GetTimestamp();

        return slot.Started;
    }

    /// <summary>
    /// Finds or creates the node for a call path, and remembers it in the thread's memo for the next frame.
    /// </summary>
    /// <remarks>
    /// The slow half, and the only one that takes the lock. Reached on the first frame a call path is seen and
    /// afterwards only when the same call site draws under a different parent than it did last time.
    /// </remarks>
    /// <param name="ts">The calling thread's state, whose memo the answer is filed in.</param>
    /// <param name="name">The scope name.</param>
    /// <param name="parentId">The node id this scope is opening under.</param>
    /// <returns>The node for this call path.</returns>
    private Node Resolve(ThreadState ts, UiScopeName name, int parentId)
    {
        Node resolved;

        lock (syncRoot)
        {
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

        // A different profiler, or a reset since the memo was filled, makes every entry wrong rather than stale;
        // clearing the node column alone stops them answering.
        if (!ReferenceEquals(ts.MemoOwner, this) || ts.MemoStamp != resetStamp)
        {
            ts.MemoOwner = this;
            ts.MemoStamp = resetStamp;
            Array.Clear(ts.MemoNodes);
        }

        var nameId = name.Id;

        if (nameId >= ts.MemoNodes.Length)
        {
            // Grown to the next power of two past the id rather than by one, since ids only ever increase and a scope
            // name is registered once for the life of the process.
            var size = (int)Math.Max(64, BitOperations.RoundUpToPowerOf2((uint)nameId + 1));

            Array.Resize(ref ts.MemoNodes, size);
            Array.Resize(ref ts.MemoParents, size);
        }

        ts.MemoNodes[nameId] = resolved;
        ts.MemoParents[nameId] = parentId;

        return resolved;
    }

    /// <summary>
    /// Hands every scope measured before the root existed over to it. Callers hold <see cref="syncRoot"/>.
    /// </summary>
    /// <remarks>
    /// NoireUI's own draw handler can run before the host's, so on the first frames its scopes open with no root to
    /// adopt them; without rehoming, the same scope would appear twice, once stranded and once adopted.
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

            // A node may already exist under the root for the same name; older readings are folded into it rather
            // than replacing it.
            if (nodes.TryGetValue((rootNodeId, key.Name), out var existing))
            {
                existing.Ticks += node.Ticks;
                existing.SelfTicks += node.SelfTicks;
                existing.Bytes += node.Bytes;
                existing.SelfBytes += node.SelfBytes;
                existing.Calls += node.Calls;
                existing.PeakMs = Math.Max(existing.PeakMs, node.PeakMs);
                existing.PeakBytes = Math.Max(existing.PeakBytes, node.PeakBytes);

                // A mark on either survives the merge: dropping it would put an excluded scope back into the
                // totals without saying so.
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

        // Read first, before this method's own bookkeeping, for the same reason Open reads them last: between the
        // two reads is only the caller's work.
        var tracking = trackAllocations;
        var endedBytes = tracking ? GC.GetAllocatedBytesForCurrentThread() : 0L;
        var ended = Stopwatch.GetTimestamp();

        var ts = threadState;

        // Guarded even though Open pushed, because the profiler may have been reset mid-scope on this very thread.
        if (ts == null || ts.Depth == 0)
            return;

        var stack = ts.Stack;
        var index = ts.Depth - 1;

        // Scopes are disposed in the reverse of the order they were opened, so anything else means a caller has kept
        // one past its block. Unwinding to it is better than silently attributing the rest of the frame to it.
        // Compared by reference, which interning makes equivalent to comparing the names and far cheaper.
        if (!ReferenceEquals(stack[index].Name, name))
        {
            var found = -1;

            for (var candidate = index - 1; candidate >= 0; candidate--)
            {
                if (!ReferenceEquals(stack[candidate].Name, name))
                    continue;

                found = candidate;
                break;
            }

            if (found < 0)
                return;

            index = found;
        }

        // Popped by moving the depth; the slots above are left to be overwritten, since everything they reference
        // outlives them in the shared table anyway.
        ts.Depth = index;

        ref var frame = ref stack[index];

        var elapsed = ended - frame.Started;
        var self = Math.Max(0L, elapsed - frame.ChildTicks);

        // Both ends of the difference have to have been read. A scope spanning a change to TrackAllocations reports
        // no bytes rather than the counter's absolute value.
        var measured = tracking && frame.Tracked;

        var allocated = measured ? Math.Max(0L, endedBytes - frame.StartedBytes) : 0L;
        var selfAllocated = Math.Max(0L, allocated - frame.ChildBytes);

        // Charged to the enclosing scope so that its own self figures exclude this one.
        if (index > 0)
        {
            ref var parent = ref stack[index - 1];
            parent.ChildTicks += elapsed;

            // Charged from this scope's entry rather than where its own measurement began, so the parent is not
            // billed for the node allocated on first seeing this call path. Differs only on the frame a scope first
            // runs.
            if (measured)
                parent.ChildBytes += Math.Max(0L, endedBytes - frame.EntryBytes);
        }

        if (!enabled)
            return;

        var node = frame.Node;

        // Unlocked, unlike the resolution in Open: the only writer of a node's counters is the thread whose scope is
        // closing, and a reader is either that thread or a snapshot taking the lock. Two threads drawing the same
        // widget at once could lose a count here; locking these five additions would cost more than everything they
        // measure on a surface entered hundreds of times a frame.
        node.Ticks += elapsed;
        node.SelfTicks += self;
        node.Bytes += allocated;
        node.SelfBytes += selfAllocated;
        node.Calls++;

        // A scope the root adopted ran alongside it rather than inside it, so the root's clock never saw it. Added
        // to the root's totals here, deliberately not to its self figures: the root's self plus every child,
        // adopted or nested, is the root's total.
        if (index == 0 && rootNode is { } root && node.ParentId == rootNodeId && node.Id != rootNodeId)
        {
            root.Ticks += elapsed;
            root.Bytes += allocated;
        }
    }

    /// <summary>
    /// Closes off the previous frame's totals when the frame number moves. Callers hold <see cref="syncRoot"/>.
    /// </summary>
    /// <remarks>
    /// Rolled up here rather than from the hub's per-frame pass, since a plugin that never registers a drawable
    /// never runs that pass and would otherwise see an empty profiler.<br/>
    /// Called only when a scope opens with nothing above it: once per frame rather than once per scope, and before
    /// any of this frame's measurements land.
    /// </remarks>
    private void RollFrameLocked()
    {
        var frame = NoireUI.FrameCount;

        if (frame == currentFrame)
            return;

        currentFrame = frame;

        // Moved here rather than on every close: the one place the reported figures change, so a Generation reader
        // rebuilds only when there is something new.
        generation++;

        foreach (var node in nodes.Values)
        {
            // A scope that did not run this frame reports zero rather than holding its last value, so a closed
            // widget stops contributing to the average instead of looking permanently expensive.
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
