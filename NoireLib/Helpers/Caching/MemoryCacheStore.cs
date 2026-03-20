using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// A strongly typed, thread-safe, in-memory cache store with TTL-based expiration and optional group invalidation.
/// For a quick global string-keyed cache, see <see cref="CacheHelper"/> instead.
/// </summary>
/// <typeparam name="TKey">The type of the cache keys. Must be non-null.</typeparam>
/// <typeparam name="TValue">The type of the cached values.</typeparam>
public sealed class MemoryCacheStore<TKey, TValue> : IDisposable where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, CacheEntry> store = new();
    private long hits;
    private long misses;
    private bool disposed;

    /// <summary>
    /// Creates a new <see cref="MemoryCacheStore{TKey, TValue}"/> instance.
    /// </summary>
    public MemoryCacheStore() { }

    /// <summary>
    /// Creates a new <see cref="MemoryCacheStore{TKey, TValue}"/> with a custom key comparer.
    /// </summary>
    /// <param name="comparer">The equality comparer used for cache key lookups.</param>
    public MemoryCacheStore(IEqualityComparer<TKey> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);

        store = new ConcurrentDictionary<TKey, CacheEntry>(comparer);
    }

    #region GetOrCreate

    /// <summary>
    /// Returns a cached value for the specified key, or creates and caches a new value using the factory
    /// if the entry does not exist or has expired.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="ttl">The time-to-live for the cache entry. Must not be negative.</param>
    /// <param name="factory">The factory function invoked to produce the value on a cache miss.</param>
    /// <param name="group">An optional group name for bulk invalidation via <see cref="InvalidateGroup"/>.</param>
    /// <returns>The cached or freshly created value.</returns>
    public TValue GetOrCreate(TKey key, TimeSpan ttl, Func<TValue> factory, string? group = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        ValidateTtl(ttl);

        if (store.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            Interlocked.Increment(ref hits);
            return entry.Value;
        }

        Interlocked.Increment(ref misses);
        var value = factory();

        store[key] = new CacheEntry(
            value: value,
            expiresAtMs: Environment.TickCount64 + (long)ttl.TotalMilliseconds,
            group: group);

        return value;
    }

    /// <summary>
    /// Returns a cached value for the specified key, or creates and caches a new value using the factory
    /// if the entry does not exist or has expired. The key-dependent factory receives the key as a parameter.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="ttl">The time-to-live for the cache entry. Must not be negative.</param>
    /// <param name="factory">The factory function invoked with the key to produce the value on a cache miss.</param>
    /// <param name="group">An optional group name for bulk invalidation via <see cref="InvalidateGroup"/>.</param>
    /// <returns>The cached or freshly created value.</returns>
    public TValue GetOrCreate(TKey key, TimeSpan ttl, Func<TKey, TValue> factory, string? group = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        ValidateTtl(ttl);

        if (store.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            Interlocked.Increment(ref hits);
            return entry.Value;
        }

        Interlocked.Increment(ref misses);
        var value = factory(key);

        store[key] = new CacheEntry(
            value: value,
            expiresAtMs: Environment.TickCount64 + (long)ttl.TotalMilliseconds,
            group: group);

        return value;
    }

    /// <summary>
    /// Returns a cached value for the specified key, or asynchronously creates and caches a new value using
    /// the factory if the entry does not exist or has expired.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="ttl">The time-to-live for the cache entry. Must not be negative.</param>
    /// <param name="factory">The asynchronous factory function invoked to produce the value on a cache miss.</param>
    /// <param name="group">An optional group name for bulk invalidation via <see cref="InvalidateGroup"/>.</param>
    /// <returns>The cached or freshly created value.</returns>
    public async Task<TValue> GetOrCreateAsync(TKey key, TimeSpan ttl, Func<Task<TValue>> factory, string? group = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        ValidateTtl(ttl);

        if (store.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            Interlocked.Increment(ref hits);
            return entry.Value;
        }

        Interlocked.Increment(ref misses);
        var value = await factory().ConfigureAwait(false);

        store[key] = new CacheEntry(
            value: value,
            expiresAtMs: Environment.TickCount64 + (long)ttl.TotalMilliseconds,
            group: group);

        return value;
    }

    /// <summary>
    /// Returns a cached value for the specified key, or asynchronously creates and caches a new value using
    /// the factory if the entry does not exist or has expired. The key-dependent factory receives the key as a parameter.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="ttl">The time-to-live for the cache entry. Must not be negative.</param>
    /// <param name="factory">The asynchronous factory function invoked with the key to produce the value on a cache miss.</param>
    /// <param name="group">An optional group name for bulk invalidation via <see cref="InvalidateGroup"/>.</param>
    /// <returns>The cached or freshly created value.</returns>
    public async Task<TValue> GetOrCreateAsync(TKey key, TimeSpan ttl, Func<TKey, Task<TValue>> factory, string? group = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        ValidateTtl(ttl);

        if (store.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            Interlocked.Increment(ref hits);
            return entry.Value;
        }

        Interlocked.Increment(ref misses);
        var value = await factory(key).ConfigureAwait(false);

        store[key] = new CacheEntry(
            value: value,
            expiresAtMs: Environment.TickCount64 + (long)ttl.TotalMilliseconds,
            group: group);

        return value;
    }

    #endregion

    #region Get / Set / TryGet

    /// <summary>
    /// Attempts to retrieve a cached value for the specified key.<br/>
    /// Returns <see langword="false"/> if the key does not exist or the entry has expired.
    /// </summary>
    /// <param name="key">The cache key to look up.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the cached value; otherwise <see langword="default"/>.</param>
    /// <returns><see langword="true"/> if a valid (non-expired) entry was found; otherwise <see langword="false"/>.</returns>
    public bool TryGet(TKey key, out TValue? value)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);

        if (store.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            Interlocked.Increment(ref hits);
            value = entry.Value;
            return true;
        }

        Interlocked.Increment(ref misses);
        value = default;
        return false;
    }

    /// <summary>
    /// Adds or replaces a cache entry for the specified key with the given value and TTL.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="ttl">The time-to-live for the cache entry. Must not be negative.</param>
    /// <param name="group">An optional group name for bulk invalidation via <see cref="InvalidateGroup"/>.</param>
    public void Set(TKey key, TValue value, TimeSpan ttl, string? group = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);
        ValidateTtl(ttl);

        store[key] = new CacheEntry(
            value: value,
            expiresAtMs: Environment.TickCount64 + (long)ttl.TotalMilliseconds,
            group: group);
    }

    /// <summary>
    /// Gets a cached value or returns the specified default if the key does not exist or has expired.
    /// </summary>
    /// <param name="key">The cache key to look up.</param>
    /// <param name="defaultValue">The value to return on a cache miss.</param>
    /// <returns>The cached value, or <paramref name="defaultValue"/> if not found or expired.</returns>
    public TValue? GetOrDefault(TKey key, TValue? defaultValue = default)
    {
        return TryGet(key, out var value) ? value : defaultValue;
    }

    #endregion

    #region Invalidation

    /// <summary>
    /// Removes a single cache entry by key.
    /// </summary>
    /// <param name="key">The cache key to remove.</param>
    /// <returns><see langword="true"/> if the entry was found and removed; otherwise <see langword="false"/>.</returns>
    public bool Invalidate(TKey key)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);

        return store.TryRemove(key, out _);
    }

    /// <summary>
    /// Removes all cache entries belonging to the specified group.
    /// </summary>
    /// <param name="group">The group name whose entries should be removed.</param>
    /// <returns>The number of entries that were removed.</returns>
    public int InvalidateGroup(string group)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(group);

        int removed = 0;

        foreach (var kvp in store)
        {
            if (string.Equals(kvp.Value.Group, group, StringComparison.Ordinal))
            {
                if (store.TryRemove(kvp.Key, out _))
                    removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Removes all entries matching the provided predicate.
    /// </summary>
    /// <param name="predicate">A function that receives the key and value and returns <see langword="true"/> for entries to remove.</param>
    /// <returns>The number of entries that were removed.</returns>
    public int InvalidateWhere(Func<TKey, TValue, bool> predicate)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(predicate);

        int removed = 0;

        foreach (var kvp in store)
        {
            if (!kvp.Value.IsExpired && predicate(kvp.Key, kvp.Value.Value))
            {
                if (store.TryRemove(kvp.Key, out _))
                    removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Removes all entries from the cache and resets statistics.
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed();

        store.Clear();
        Interlocked.Exchange(ref hits, 0);
        Interlocked.Exchange(ref misses, 0);
    }

    /// <summary>
    /// Removes all expired entries from the cache. Call this periodically if you want to
    /// reclaim memory from entries that have expired but not been accessed.
    /// </summary>
    /// <returns>The number of expired entries that were removed.</returns>
    public int Cleanup()
    {
        ThrowIfDisposed();

        int removed = 0;

        foreach (var kvp in store)
        {
            if (kvp.Value.IsExpired)
            {
                if (store.TryRemove(kvp.Key, out _))
                    removed++;
            }
        }

        return removed;
    }

    #endregion

    #region Refresh

    /// <summary>
    /// Forces a cache entry to be refreshed by invoking the factory, regardless of whether the entry has expired.
    /// If the key does not exist, a new entry is created.
    /// </summary>
    /// <param name="key">The cache key to refresh.</param>
    /// <param name="ttl">The time-to-live for the refreshed entry. Must not be negative.</param>
    /// <param name="factory">The factory function invoked to produce the new value.</param>
    /// <param name="group">An optional group name for bulk invalidation.</param>
    /// <returns>The newly produced value.</returns>
    public TValue Refresh(TKey key, TimeSpan ttl, Func<TValue> factory, string? group = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        ValidateTtl(ttl);

        var value = factory();

        store[key] = new CacheEntry(
            value: value,
            expiresAtMs: Environment.TickCount64 + (long)ttl.TotalMilliseconds,
            group: group);

        return value;
    }

    /// <summary>
    /// Forces a cache entry to be refreshed asynchronously by invoking the factory, regardless of whether
    /// the entry has expired. If the key does not exist, a new entry is created.
    /// </summary>
    /// <param name="key">The cache key to refresh.</param>
    /// <param name="ttl">The time-to-live for the refreshed entry. Must not be negative.</param>
    /// <param name="factory">The asynchronous factory function invoked to produce the new value.</param>
    /// <param name="group">An optional group name for bulk invalidation.</param>
    /// <returns>The newly produced value.</returns>
    public async Task<TValue> RefreshAsync(TKey key, TimeSpan ttl, Func<Task<TValue>> factory, string? group = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        ValidateTtl(ttl);

        var value = await factory().ConfigureAwait(false);

        store[key] = new CacheEntry(
            value: value,
            expiresAtMs: Environment.TickCount64 + (long)ttl.TotalMilliseconds,
            group: group);

        return value;
    }

    #endregion

    #region Inspection

    /// <summary>
    /// Gets the current number of entries in the cache, including expired entries not yet cleaned up.
    /// </summary>
    public int Count => store.Count;

    /// <summary>
    /// Gets the number of active (non-expired) entries in the cache.
    /// </summary>
    public int ActiveCount => store.Values.Count(e => !e.IsExpired);

    /// <summary>
    /// Checks whether an entry exists for the specified key and has not expired.
    /// </summary>
    /// <param name="key">The cache key to check.</param>
    /// <returns><see langword="true"/> if a valid (non-expired) entry exists; otherwise <see langword="false"/>.</returns>
    public bool Contains(TKey key)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);

        return store.TryGetValue(key, out var entry) && !entry.IsExpired;
    }

    /// <summary>
    /// Returns a snapshot of all cache keys currently stored (including expired entries).
    /// </summary>
    /// <returns>A list of all keys in the cache.</returns>
    public List<TKey> GetKeys()
    {
        ThrowIfDisposed();

        return store.Keys.ToList();
    }

    /// <summary>
    /// Returns a snapshot of all valid (non-expired) cache keys.
    /// </summary>
    /// <returns>A list of non-expired keys in the cache.</returns>
    public List<TKey> GetActiveKeys()
    {
        ThrowIfDisposed();

        return store.Where(kvp => !kvp.Value.IsExpired).Select(kvp => kvp.Key).ToList();
    }

    /// <summary>
    /// Returns a snapshot of all cache keys belonging to the specified group.
    /// </summary>
    /// <param name="group">The group name to filter by.</param>
    /// <returns>A list of keys in the specified group.</returns>
    public List<TKey> GetKeysByGroup(string group)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(group);

        return store
            .Where(kvp => string.Equals(kvp.Value.Group, group, StringComparison.Ordinal))
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// Returns a snapshot of all active (non-expired) values in the cache.
    /// </summary>
    /// <returns>A list of non-expired values.</returns>
    public List<TValue> GetActiveValues()
    {
        ThrowIfDisposed();

        return store.Values.Where(e => !e.IsExpired).Select(e => e.Value).ToList();
    }

    /// <summary>
    /// Returns a snapshot of all active (non-expired) key-value pairs in the cache.
    /// </summary>
    /// <returns>A list of non-expired key-value pairs.</returns>
    public List<KeyValuePair<TKey, TValue>> GetActiveEntries()
    {
        ThrowIfDisposed();

        return store
            .Where(kvp => !kvp.Value.IsExpired)
            .Select(kvp => new KeyValuePair<TKey, TValue>(kvp.Key, kvp.Value.Value))
            .ToList();
    }

    /// <summary>
    /// Returns statistics about the cache including hit/miss counts and entry information.
    /// </summary>
    /// <returns>A <see cref="CacheStatistics"/> snapshot of the current cache state.</returns>
    public CacheStatistics GetStatistics()
    {
        ThrowIfDisposed();

        var hits = Interlocked.Read(ref this.hits);
        var misses = Interlocked.Read(ref this.misses);
        var entryCount = store.Count;
        var expiredCount = store.Values.Count(e => e.IsExpired);

        return new CacheStatistics(hits, misses, entryCount, expiredCount);
    }

    /// <summary>
    /// Resets the hit and miss counters without clearing the cache entries.
    /// </summary>
    public void ResetStatistics()
    {
        ThrowIfDisposed();

        Interlocked.Exchange(ref hits, 0);
        Interlocked.Exchange(ref misses, 0);
    }

    #endregion

    #region Dispose

    /// <summary>
    /// Disposes the cache store, clearing all entries and releasing resources.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        store.Clear();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    #endregion

    #region Internal

    private static void ValidateTtl(TimeSpan ttl)
    {
        if (ttl < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must not be negative.");
    }

    private sealed class CacheEntry
    {
        public TValue Value { get; }
        public long ExpiresAtMs { get; }
        public string? Group { get; }
        public bool IsExpired => Environment.TickCount64 >= ExpiresAtMs;

        public CacheEntry(TValue value, long expiresAtMs, string? group)
        {
            Value = value;
            ExpiresAtMs = expiresAtMs;
            Group = group;
        }
    }

    #endregion
}
