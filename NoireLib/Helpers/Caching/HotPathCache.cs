using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// A cache for values recomputed on every frame: struct-keyed, typed, and invalidated when something changes rather
/// than when a clock runs out.<br/>
/// For anything read while a frame is being drawn. For anything else, use <see cref="CacheHelper"/> or
/// <see cref="MemoryCacheStore{TKey, TValue}"/>.
/// </summary>
/// <typeparam name="TKey">
/// The key. A <see langword="readonly record struct"/> carrying every input the value depends on is the shape this is
/// built for: the compiler writes its equality, and the constraint is what keeps a lookup from boxing.
/// </typeparam>
/// <typeparam name="TValue">The cached value. Held as itself rather than as <see langword="object"/>, so it never boxes.</typeparam>
public sealed class HotPathCache<TKey, TValue>
    where TKey : struct, IEquatable<TKey>
{
    /// <summary>
    /// How many entries a cache keeps before it starts over, when no other bound is given.
    /// </summary>
    /// <remarks>
    /// Sized for the case this is built for: an interface draws a stable set of labels and layouts, so a cache fills
    /// once and hits from then on.
    /// </remarks>
    public const int DefaultCapacity = 4096;

    private readonly Dictionary<TKey, TValue> entries;

    /// <summary>
    /// The token the entries were stored under. See <see cref="InvalidateIfChanged"/>.
    /// </summary>
    private int token;

    private bool tokenSeen;

    private long hits;
    private long misses;

    /// <summary>
    /// Creates a cache.
    /// </summary>
    /// <param name="capacity">
    /// How many entries to keep before starting over. See <see cref="Capacity"/> for what happens at the bound.
    /// Defaults to <see cref="DefaultCapacity"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is not positive.</exception>
    public HotPathCache(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        Capacity = capacity;
        entries = new Dictionary<TKey, TValue>();
    }

    /// <summary>
    /// How many entries are kept before the cache starts over.
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    /// How many entries are currently held.
    /// </summary>
    public int Count => entries.Count;

    /// <summary>
    /// How many lookups have been answered from the cache.
    /// </summary>
    public long Hits => hits;

    /// <summary>
    /// How many lookups have missed.
    /// </summary>
    /// <inheritdoc cref="Hits"/>
    public long Misses => misses;

    /// <summary>
    /// Looks a value up.
    /// </summary>
    /// <param name="key">The key. Passed by reference so a large key is not copied per lookup.</param>
    /// <param name="value">The cached value, or <see langword="default"/> on a miss.</param>
    /// <returns><see langword="true"/> when the value was already known.</returns>
    public bool TryGet(in TKey key, out TValue value)
    {
        if (entries.TryGetValue(key, out value))
        {
            hits++;
            return true;
        }

        misses++;
        return false;
    }

    /// <summary>
    /// Remembers a value, replacing whatever was held under the same key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value to remember.</param>
    public void Set(in TKey key, TValue value)
    {
        // Cleared rather than evicted at the bound. See the remarks on Capacity.
        if (entries.Count >= Capacity)
            entries.Clear();

        entries[key] = value;
    }

    /// <summary>
    /// Forgets everything if <paramref name="current"/> differs from the token the entries were stored under.
    /// </summary>
    /// <param name="current">
    /// A value combining everything that invalidates the whole cache. <see cref="HashCode.Combine{T1, T2}"/> composes
    /// one without allocating.
    /// </param>
    /// <returns><see langword="true"/> when the cache was cleared.</returns>
    /// <example>
    /// <code>
    /// Cache.InvalidateIfChanged(HashCode.Combine(NoireUI.Scale, NoireTheme.Current.Revision, UiFontCache.Generation));
    /// </code>
    /// </example>
    public bool InvalidateIfChanged(int current)
    {
        if (tokenSeen && token == current)
            return false;

        var stale = tokenSeen && entries.Count > 0;

        token = current;
        tokenSeen = true;

        if (stale)
            entries.Clear();

        return stale;
    }

    /// <summary>
    /// Forgets one entry.
    /// </summary>
    /// <param name="key">The key to forget.</param>
    /// <returns><see langword="true"/> when an entry was held under that key.</returns>
    public bool Remove(in TKey key) => entries.Remove(key);

    /// <summary>
    /// Forgets every entry, and the hit and miss counts with them.
    /// </summary>
    /// <remarks>
    /// The invalidation token is kept, so this does not make the next <see cref="InvalidateIfChanged"/> report a change
    /// that did not happen.
    /// </remarks>
    public void Clear()
    {
        entries.Clear();
        hits = 0;
        misses = 0;
    }
}
