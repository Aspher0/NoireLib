using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// What each part of the interface costs to build, per frame, by name. Off by default and free when off. Measures
/// the time spent building the draw data on the draw thread, not the GPU cost and not the host's whole draw callback.
/// </summary>
public sealed class UiProfiler
{
    // How much of the rolling average one frame is worth.
    private const double AverageWeight = 0.05d;

    private readonly object syncRoot = new();

    // One entry per call path, not per name: a node is identified by its name and the node it sits inside.
    private readonly Dictionary<(int Parent, int Name), Node> nodes = new();

    // The handle for RootScopeName, resolved once rather than compared as a string per open.
    private static readonly UiScopeName RootScope = UiScopeName.For(RootScopeName);

    private int nextNodeId = 1;
    private int currentFrame = -1;

    private int generation;

    // Bumped by Reset, so a per-thread memo filled before it stops answering.
    private int resetStamp;

    // The node RootScopeName was given, so scopes opened outside it can still be hung from it.
    private int rootNodeId;

    private Node? rootNode;

    // Everything the profiler keeps per thread, in one object so the hot path pays one thread-local read rather than
    // one per piece.
    private sealed class ThreadState
    {
        // The scopes currently open on this thread, innermost last, in the first Depth slots.
        public OpenScope[] Stack = new OpenScope[64];

        // How many scopes are open: the next slot of Stack to fill.
        public int Depth;

        // The profiler the memo below was filled for, so two profilers on one thread cannot read each other's nodes.
        public UiProfiler? MemoOwner;

        // The resetStamp the memo was filled at. A reset makes every node in it a dead object.
        public int MemoStamp;

        // The node each call site resolved to last, indexed by UiScopeName.Id.
        public Node?[] MemoNodes = new Node?[64];

        // The parent each memo entry was resolved under. Part of the hit check because one call site can appear under
        // several parents.
        public int[] MemoParents = new int[64];
    }

    [ThreadStatic]
    private static ThreadState? threadState;

    // A scope that has been opened and not yet closed, and how much of its time has been spent inside scopes nested
    // in it.
    private struct OpenScope
    {
        public Node Node;

        public int NodeId;
        public UiScopeName Name;
        public long Started;
        public long ChildTicks;

        // The thread's allocation counter when this scope opened, so the scope's own allocation is the difference.
        public long StartedBytes;

        // The counter on entry to Open, before the profiler did any of its own work.
        public long EntryBytes;

        // How many bytes were allocated inside scopes nested in this one, subtracted so a parent is not charged for
        // its children's allocations.
        public long ChildBytes;

        // Whether TrackAllocations was on when this scope opened.
        public bool Tracked;
    }

    // What one scope has cost, accumulating over the frame being drawn and rolled up when it ends.
    private sealed class Node
    {
        public int Id;
        public int ParentId;
        public string Name = string.Empty;

        // The UiScopeName.Id half of the key this node is filed under, so a rehome can rebuild the key without
        // hashing the name back into one.
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

        public bool Excluded;
    }

    // Fields rather than auto-properties so hot paths read them without a call; read on every scope of every frame.
    private bool enabled;

    private bool trackAllocations;

    private bool detailed;

    /// <summary>
    /// Whether scopes are timed.
    /// </summary>
    public bool Enabled
    {
        get => enabled;
        set => enabled = value;
    }

    /// <summary>
    /// Whether scopes also record how many bytes they allocated.
    /// </summary>
    /// <remarks>Requires <see cref="Enabled"/>.</remarks>
    public bool TrackAllocations
    {
        get => trackAllocations;
        set => trackAllocations = value;
    }

    /// <summary>
    /// Whether the library's per-method scopes are measured as rows of their own, such as <c>NoireShapes.Glow</c>.
    /// </summary>
    /// <remarks>Requires <see cref="Enabled"/>.</remarks>
    public bool Detailed
    {
        get => detailed;
        set => detailed = value;
    }

    private double slowFrameMs;

    private string? slowFrameReport;

    public double SlowFrameMs
    {
        get => slowFrameMs;
        set => slowFrameMs = Math.Max(0d, value);
    }

    public event Action<string>? SlowFrame;

    // Both switches a per-method scope needs, in one read for the gate that asks on every shape drawn.
    internal bool MeasuringMethods => enabled && detailed;

    /// <summary>
    /// The name to measure a whole draw callback under.
    /// </summary>
    public const string RootScopeName = "ImGui Draw";

    /// <summary>
    /// What the root scope cost in total on average, or 0 when nothing is measuring one.
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
    /// How much of the root scope nothing has accounted for: its own self time.
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
    /// Moves whenever the reported figures change, once per measured frame.
    /// </summary>
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
    /// <returns>One entry per scope measured since the last <see cref="Reset"/>.</returns>
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
    /// <param name="buffer">The list to fill, cleared before anything is added.</param>
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

    // Adds one entry per node, most expensive first. Callers hold syncRoot.
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
    /// The sum of every scope's self time, averaged.
    /// </summary>
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
    /// The sum of every scope's self allocation, averaged: how many bytes a frame of interface produces.
    /// </summary>
    /// <remarks>Reads 0 unless both <see cref="Enabled"/> and <see cref="TrackAllocations"/> are on.</remarks>
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
    /// Flips whether one scope counts towards the totals.
    /// </summary>
    /// <param name="id">The scope's <see cref="UiProfileEntry.Id"/>.</param>
    /// <returns>Whether the scope is now excluded.</returns>
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
    /// Whether one scope is currently left out of the totals.
    /// </summary>
    /// <param name="id">The scope's <see cref="UiProfileEntry.Id"/>.</param>
    /// <returns><see langword="true"/> when the scope exists and is excluded.</returns>
    public bool IsExcluded(int id)
    {
        lock (syncRoot)
            return FindLocked(id) is { Excluded: true };
    }

    /// <summary>
    /// How many scopes are currently left out of the totals.
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
    /// Counts every scope towards the totals again, without forgetting any measurement.
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

    // Callers hold syncRoot.
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

    // Pair with Close. A scope whose name is already the innermost open one is declined and returns 0; otherwise the
    // return is the timestamp the scope started at.
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
            string? report;

            lock (syncRoot)
            {
                RollFrameLocked();
                report = slowFrameReport;
                slowFrameReport = null;
            }

            if (report != null)
                SlowFrame?.Invoke(report);
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

    // Hands every scope measured before the root existed over to it. Callers hold syncRoot.
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
        // to the root's totals here, not to its self figures: the root's self plus every child, adopted or nested,
        // is the root's total.
        if (index == 0 && rootNode is { } root && node.ParentId == rootNodeId && node.Id != rootNodeId)
        {
            root.Ticks += elapsed;
            root.Bytes += allocated;
        }
    }

    // Closes off the previous frame's totals when the frame number moves. Callers hold syncRoot.
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

        if (slowFrameMs > 0d && rootNode is { } root && root.LastMs >= slowFrameMs && SlowFrame != null)
            slowFrameReport = SlowFrameReportLocked(root.LastMs);
    }

    private string SlowFrameReportLocked(double frameMs)
    {
        var slowest = new List<Node>(nodes.Values);
        slowest.Sort(static (a, b) => b.SelfLastMs.CompareTo(a.SelfLastMs));

        var text = new System.Text.StringBuilder($"Slow frame: {frameMs:0} ms, draw path warmed {NoireUI.DrawPathWarmed}. Slowest scopes, self/total ms:");

        for (var index = 0; index < slowest.Count && index < 12; index++)
        {
            var node = slowest[index];

            if (node.SelfLastMs < 0.5d)
                break;

            text.Append($" {node.Name} {node.SelfLastMs:0.0}/{node.LastMs:0.0};");
        }

        return text.ToString();
    }

    private static double ToMilliseconds(long ticks)
        => ticks == 0L ? 0d : Stopwatch.GetElapsedTime(0L, ticks).TotalMilliseconds;
}
