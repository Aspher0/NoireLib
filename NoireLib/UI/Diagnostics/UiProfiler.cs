using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NoireLib.UI;

/// <summary>
/// What each part of the interface costs to build, per frame, by name.<br/>
/// Off by default and free when off. Turn it on, draw for a few seconds, and read <see cref="Snapshot"/>: the answer to
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
/// Every widget NoireUI ships measures itself, so turning this on attributes the frame without any work at the call
/// sites. <see cref="NoireUI.Profile{TState}(string, TState, System.Action{TState})"/> puts your own code on the same
/// list.
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
    /// The key holds the parent's id rather than a built path string, so nothing is composed or allocated per frame.
    /// </remarks>
    private readonly Dictionary<(int Parent, string Name), Node> nodes = new();

    private int nextNodeId = 1;
    private int currentFrame = -1;

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
        public string Name;
        public long Started;
        public long ChildTicks;
    }

    /// <summary>
    /// What one scope has cost, accumulating over the frame being drawn and rolled up when it ends.
    /// </summary>
    private sealed class Node
    {
        public int Id;
        public int ParentId;
        public string Name = string.Empty;

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
                return nodes.TryGetValue((0, RootScopeName), out var root) ? root.AverageMs : 0d;
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
                return nodes.TryGetValue((0, RootScopeName), out var root) ? root.SelfAverageMs : 0d;
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
                    node.SelfAverageMs));
            }

            entries.Sort(static (left, right) => right.SelfAverageMs.CompareTo(left.SelfAverageMs));

            return entries;
        }
    }

    /// <summary>
    /// The sum of every scope's self time, averaged. This is the figure that means something: because self time
    /// excludes nested scopes, adding it up counts each piece of work exactly once.
    /// </summary>
    /// <remarks>
    /// It accounts for the instrumented work only. The difference between this and what your host reports for the
    /// plugin is everything not measured here, which includes the host's own windowing and the ImGui calls around it.
    /// </remarks>
    public double TotalAverageMs
    {
        get
        {
            var total = 0d;

            lock (syncRoot)
            {
                foreach (var node in nodes.Values)
                    total += node.SelfAverageMs;
            }

            return total;
        }
    }

    /// <summary>
    /// Forgets every measurement taken so far, including the peaks.
    /// </summary>
    /// <remarks>
    /// Worth calling right before the interaction you actually want to measure. The peak is a high-water mark, so
    /// without a reset it keeps reporting the frame that built the window for the first time.
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
        }

        openScopes?.Clear();
    }

    /// <summary>
    /// Starts timing a scope. Pair with <see cref="Close"/>, which <see cref="UiProfileScope"/> does.
    /// </summary>
    /// <param name="name">The scope's name.</param>
    /// <returns>The timestamp the scope started at, or 0 when the profiler is off.</returns>
    internal long Open(string name)
    {
        if (!Enabled || string.IsNullOrEmpty(name))
            return 0L;

        var stack = openScopes ??= new List<OpenScope>();
        var parentId = stack.Count > 0 ? stack[^1].NodeId : 0;

        // A scope opened with nothing above it still belongs to the frame, even though it is not inside the root's
        // span: NoireUI's own frame pass runs from a second draw handler, alongside the host's rather than within it,
        // and the host times both. Adopted by the root so the tree has one trunk instead of a scattering of orphans.
        if (parentId == 0 && !string.Equals(name, RootScopeName, StringComparison.Ordinal))
            parentId = rootNodeId;

        Node resolved;

        // Resolved on the way in rather than on the way out, because a scope opened inside this one needs this node's
        // id to know where it belongs.
        lock (syncRoot)
        {
            if (!nodes.TryGetValue((parentId, name), out var node))
            {
                node = new Node { Id = nextNodeId++, ParentId = parentId, Name = name };
                nodes[(parentId, name)] = node;

                if (parentId == 0 && string.Equals(name, RootScopeName, StringComparison.Ordinal))
                {
                    rootNodeId = node.Id;
                    rootNode = node;

                    AdoptOrphansLocked();
                }
            }

            resolved = node;
        }

        var started = Stopwatch.GetTimestamp();

        stack.Add(new OpenScope
        {
            Node = resolved,
            NodeId = resolved.Id,
            Name = name,
            Started = started,
            ChildTicks = 0L,
        });

        return started;
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
        List<(int Parent, string Name)>? orphans = null;

        foreach (var pair in nodes)
        {
            if (pair.Key.Parent != 0 || pair.Value.Id == rootNodeId)
                continue;

            (orphans ??= new List<(int, string)>()).Add(pair.Key);
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
                existing.Calls += node.Calls;
                existing.PeakMs = Math.Max(existing.PeakMs, node.PeakMs);
                continue;
            }

            nodes[(rootNodeId, key.Name)] = node;
        }
    }

    /// <summary>
    /// Finishes timing a scope opened by <see cref="Open"/>, charging its time to itself and to whatever encloses it.
    /// </summary>
    /// <param name="name">The scope's name.</param>
    /// <param name="started">The timestamp <see cref="Open"/> returned.</param>
    internal void Close(string name, long started)
    {
        if (started == 0L)
            return;

        var stack = openScopes;

        // Popped even when the profiler was switched off mid-scope, or the entry would be left open and every later
        // scope would be charged as nested inside a scope that never closed.
        if (stack == null || stack.Count == 0)
            return;

        var index = stack.Count - 1;
        var frame = stack[index];

        // Scopes are disposed in the reverse of the order they were opened, so anything else means a caller has kept
        // one past its block. Unwinding to it is better than silently attributing the rest of the frame to it.
        if (!string.Equals(frame.Name, name, StringComparison.Ordinal))
        {
            var found = stack.FindLastIndex(scope => string.Equals(scope.Name, name, StringComparison.Ordinal));


            if (found < 0)
                return;

            index = found;
            frame = stack[index];
        }

        stack.RemoveRange(index, stack.Count - index);

        var elapsed = Stopwatch.GetTimestamp() - frame.Started;
        var self = Math.Max(0L, elapsed - frame.ChildTicks);

        // Charged to the enclosing scope so that its own self time excludes this one.
        if (stack.Count > 0)
        {
            var parent = stack[^1];
            parent.ChildTicks += elapsed;
            stack[^1] = parent;
        }

        if (!Enabled)
            return;

        var node = frame.Node;

        lock (syncRoot)
        {
            RollFrameLocked();

            node.Ticks += elapsed;
            node.SelfTicks += self;
            node.Calls++;

            // A scope the root adopted ran alongside the root rather than inside it, so the root's own clock never saw
            // it. Added to the root's total here, and deliberately not to its self time, which keeps the branch adding
            // up: the root's self plus every child, adopted or nested, is the root's total.
            if (stack.Count == 0 && rootNode != null && node.ParentId == rootNodeId && node.Id != rootNodeId)
                rootNode.Ticks += elapsed;
        }
    }

    /// <summary>
    /// Closes off the previous frame's totals when the frame number moves. Callers hold <see cref="syncRoot"/>.
    /// </summary>
    /// <remarks>
    /// Rolled up here, on the way into a measurement, rather than from the hub's per-frame pass. A scope can be measured
    /// by a plugin that never registered a drawable and so never runs that pass, and a profiler that only reported for
    /// hub-driven interfaces would be silently empty for exactly the plugin most likely to be looking at it.
    /// </remarks>
    private void RollFrameLocked()
    {
        var frame = NoireUI.FrameCount;

        if (frame == currentFrame)
            return;

        currentFrame = frame;

        foreach (var node in nodes.Values)
        {
            // A scope that did not run this frame reports zero rather than holding its last value, so a widget that has
            // been closed stops contributing to the average instead of looking permanently expensive.
            var ms = ToMilliseconds(node.Ticks);
            var selfMs = ToMilliseconds(node.SelfTicks);

            node.LastMs = ms;
            node.SelfLastMs = selfMs;
            node.LastCalls = node.Calls;

            if (node.Seeded)
            {
                node.AverageMs = (node.AverageMs * (1d - AverageWeight)) + (ms * AverageWeight);
                node.SelfAverageMs = (node.SelfAverageMs * (1d - AverageWeight)) + (selfMs * AverageWeight);
            }
            else
            {
                node.AverageMs = ms;
                node.SelfAverageMs = selfMs;
            }

            node.Seeded = true;

            if (ms > node.PeakMs)
                node.PeakMs = ms;

            node.Ticks = 0L;
            node.SelfTicks = 0L;
            node.Calls = 0;
        }
    }

    /// <summary>
    /// Converts a tick count to milliseconds.
    /// </summary>
    private static double ToMilliseconds(long ticks)
        => ticks == 0L ? 0d : Stopwatch.GetElapsedTime(0L, ticks).TotalMilliseconds;
}
