using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// Mutates a stored value in place. The value is handed over as a copy and written back afterwards, so the delegate never
/// holds a reference into the backing store.
/// </summary>
/// <typeparam name="T">The stored value type.</typeparam>
/// <param name="value">The current value, to be updated in place.</param>
public delegate void UiStateUpdater<T>(ref T value);

/// <summary>
/// Id-keyed transient state for immediate-mode helpers: the small amount of memory a stateless-looking widget needs
/// between frames (a hold progress, a drag origin, an animation phase).<br/>
/// Entries are keyed by a caller id plus an optional sub key, typed per value, and pruned automatically once they have
/// gone untouched for <see cref="PruneAfterFrames"/> frames, so a widget that stops drawing leaves nothing behind.<br/>
/// This is deliberately not a configuration store: nothing here is persisted, and everything is lost on reload. See
/// <see cref="NoireAnim"/> for the animation layer built on top of it.<br/>
/// <b>Draw thread only.</b> The entries are unsynchronised, matching the thread the UI is drawn from.
/// </summary>
/// <remarks>
/// No member returns a reference into the store. A <see langword="ref"/> into a dictionary is invalidated by the next
/// insert (growing abandons the backing array), so a write through a stale one would vanish silently, triggered by an
/// unrelated widget happening to exist on the same frame. <see cref="Update{T}(string, string, UiStateUpdater{T})"/>
/// exists to cover that case safely.
/// </remarks>
public static class UiFrameState
{
    /// <summary>
    /// The composite key of an entry. Both parts are kept separate rather than concatenated so that a lookup with two
    /// existing strings allocates nothing.
    /// </summary>
    private readonly record struct StateKey(string Id, string SubKey);

    /// <summary>
    /// The per-type operations the shared maintenance pass needs, registered by each typed store on first use.
    /// </summary>
    private sealed class StoreHandle
    {
        public required Func<int> Count { get; init; }

        public required Action<int, int> Prune { get; init; }

        public required Action Clear { get; init; }
    }

    private static readonly object StoresLock = new();
    private static readonly List<StoreHandle> Stores = new();

    private static int lastPruneFrame;

    /// <summary>
    /// How many frames an entry survives without being read or written before the maintenance pass drops it.<br/>
    /// The default is roughly ten seconds at 60 FPS. Raise it for state that must survive a widget being scrolled out of
    /// view for a long time.
    /// </summary>
    public static int PruneAfterFrames { get; set; } = 600;

    /// <summary>
    /// How often the maintenance pass runs, in frames. Pruning walks every entry, so it is spread out rather than done
    /// every frame.
    /// </summary>
    public static int PruneIntervalFrames { get; set; } = 300;

    /// <summary>
    /// The frame the state is currently keyed against. See <see cref="NoireUI.FrameCount"/>.
    /// </summary>
    public static int Frame => NoireUI.FrameCount;

    /// <summary>
    /// The number of entries currently held, across every value type.
    /// </summary>
    public static int Count
    {
        get
        {
            var total = 0;

            lock (StoresLock)
            {
                foreach (var store in Stores)
                    total += store.Count();
            }

            return total;
        }
    }

    /// <summary>
    /// Reads an entry, returning <paramref name="fallback"/> when it does not exist yet.<br/>
    /// Reading marks the entry as still in use, so a value read every frame is never pruned.
    /// </summary>
    /// <typeparam name="T">The stored value type.</typeparam>
    /// <param name="id">The caller id, unique per widget.</param>
    /// <param name="subKey">The sub key, naming which piece of state this is (for example "hover").</param>
    /// <param name="fallback">The value returned when the entry does not exist.</param>
    /// <returns>The stored value, or <paramref name="fallback"/>.</returns>
    public static T Get<T>(string id, string subKey, T fallback = default!)
    {
        NoireUI.EnsureFrameServices();

        ref var slot = ref GetSlotOrNull<T>(id, subKey, out var found);
        if (!found)
            return fallback;

        slot.Frame = Frame;
        return slot.Value;
    }

    /// <inheritdoc cref="Get{T}(string, string, T)"/>
    public static T Get<T>(string id, T fallback = default!) => Get(id, string.Empty, fallback);

    /// <summary>
    /// Reads an entry and reports whether it existed.
    /// </summary>
    /// <typeparam name="T">The stored value type.</typeparam>
    /// <param name="id">The caller id, unique per widget.</param>
    /// <param name="subKey">The sub key, naming which piece of state this is.</param>
    /// <param name="value">The stored value, or the default when the entry does not exist.</param>
    /// <returns>True when the entry existed.</returns>
    public static bool TryGet<T>(string id, string subKey, out T value)
    {
        NoireUI.EnsureFrameServices();

        ref var slot = ref GetSlotOrNull<T>(id, subKey, out var found);
        if (!found)
        {
            value = default!;
            return false;
        }

        slot.Frame = Frame;
        value = slot.Value;
        return true;
    }

    /// <summary>
    /// Writes an entry, creating it when needed.
    /// </summary>
    /// <typeparam name="T">The stored value type.</typeparam>
    /// <param name="id">The caller id, unique per widget.</param>
    /// <param name="subKey">The sub key, naming which piece of state this is.</param>
    /// <param name="value">The value to store.</param>
    public static void Set<T>(string id, string subKey, T value)
    {
        NoireUI.EnsureFrameServices();

        Store<T>.Entries[new StateKey(id, subKey)] = new Store<T>.Slot { Value = value, Frame = Frame };
    }

    /// <inheritdoc cref="Set{T}(string, string, T)"/>
    public static void Set<T>(string id, T value) => Set(id, string.Empty, value);

    /// <summary>
    /// Reads an entry, creating it from <paramref name="factory"/> the first time.
    /// </summary>
    /// <typeparam name="T">The stored value type.</typeparam>
    /// <param name="id">The caller id, unique per widget.</param>
    /// <param name="subKey">The sub key, naming which piece of state this is.</param>
    /// <param name="factory">Produces the initial value. Invoked at most once per entry.</param>
    /// <returns>The stored value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    public static T GetOrAdd<T>(string id, string subKey, Func<T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (TryGet<T>(id, subKey, out var existing))
            return existing;

        var created = factory();
        Set(id, subKey, created);
        return created;
    }

    /// <summary>
    /// Reads an entry, mutates it in place and writes it back.<br/>
    /// The value is copied out before <paramref name="updater"/> runs and copied back afterwards, so the delegate is free
    /// to touch other entries without corrupting this one.
    /// </summary>
    /// <typeparam name="T">The stored value type.</typeparam>
    /// <param name="id">The caller id, unique per widget.</param>
    /// <param name="subKey">The sub key, naming which piece of state this is.</param>
    /// <param name="updater">Mutates the value in place.</param>
    /// <returns>The value after the update.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="updater"/> is <see langword="null"/>.</exception>
    public static T Update<T>(string id, string subKey, UiStateUpdater<T> updater)
    {
        ArgumentNullException.ThrowIfNull(updater);

        var value = Get<T>(id, subKey);
        updater(ref value);
        Set(id, subKey, value);
        return value;
    }

    /// <summary>
    /// Drops a single entry.
    /// </summary>
    /// <typeparam name="T">The stored value type.</typeparam>
    /// <param name="id">The caller id.</param>
    /// <param name="subKey">The sub key.</param>
    /// <returns>True when an entry was removed.</returns>
    public static bool Remove<T>(string id, string subKey) => Store<T>.Entries.Remove(new StateKey(id, subKey));

    /// <inheritdoc cref="Remove{T}(string, string)"/>
    public static bool Remove<T>(string id) => Remove<T>(id, string.Empty);

    /// <summary>
    /// Drops every entry of every type. Widgets rebuild their state from scratch on the next frame.
    /// </summary>
    public static void Clear()
    {
        lock (StoresLock)
        {
            foreach (var store in Stores)
                store.Clear();
        }
    }

    /// <summary>
    /// Runs the maintenance pass if enough frames have passed since the last one. Called once per frame by the hub.
    /// </summary>
    /// <param name="frame">The current frame.</param>
    internal static void Tick(int frame)
    {
        if (PruneIntervalFrames > 0 && frame - lastPruneFrame < PruneIntervalFrames)
            return;

        lastPruneFrame = frame;

        lock (StoresLock)
        {
            foreach (var store in Stores)
                store.Prune(frame, PruneAfterFrames);
        }
    }

    private static void RegisterStore(StoreHandle handle)
    {
        lock (StoresLock)
            Stores.Add(handle);
    }

    /// <summary>
    /// Returns a reference to the slot of an entry so that a read can refresh its frame stamp without a second lookup.
    /// The reference never leaves this class and is not used across an operation that could insert.
    /// </summary>
    private static ref Store<T>.Slot GetSlotOrNull<T>(string id, string subKey, out bool found)
    {
        ref var slot = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(Store<T>.Entries, new StateKey(id, subKey));
        found = !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref slot);
        return ref slot;
    }

    /// <summary>
    /// The entries of one value type. Segregating by type keeps values unboxed, which is what makes a per-frame write of
    /// a float or a small struct allocation-free.
    /// </summary>
    private static class Store<T>
    {
        internal static readonly Dictionary<StateKey, Slot> Entries = new();

        private static readonly List<StateKey> StaleKeys = new();

        static Store()
        {
            RegisterStore(new StoreHandle
            {
                Count = static () => Entries.Count,
                Prune = Prune,
                Clear = static () => Entries.Clear(),
            });
        }

        internal struct Slot
        {
            public T Value;

            public int Frame;
        }

        private static void Prune(int frame, int maxAge)
        {
            StaleKeys.Clear();

            foreach (var entry in Entries)
            {
                if (frame - entry.Value.Frame > maxAge)
                    StaleKeys.Add(entry.Key);
            }

            foreach (var key in StaleKeys)
                Entries.Remove(key);

            StaleKeys.Clear();
        }
    }
}
