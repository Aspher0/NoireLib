using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// A static caching helper that provides a default in-memory cache store for on-demand data reuse.
/// Entries are keyed by string, support time-to-live (TTL) expiration, and can be organized into groups
/// for bulk invalidation.<br/>
/// For strongly typed caching with non-string keys, use <see cref="MemoryCacheStore{TKey, TValue}"/> instead.
/// </summary>
public static class CacheHelper
{
    private static readonly ConcurrentDictionary<string, CacheEntry> Store = new();
    private static long Hits;
    private static long Misses;

    #region GetOrCreate

    /// <summary>
    /// Returns a cached value for the specified key, or creates and caches a new value using the factory
    /// if the entry does not exist or has expired.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The unique cache key.</param>
    /// <param name="ttl">The time-to-live for the cache entry. Must not be negative.</param>
    /// <param name="factory">The factory function invoked to produce the value on a cache miss.</param>
    /// <param name="group">An optional group name for bulk invalidation via <see cref="InvalidateGroup"/>.</param>
    /// <returns>The cached or freshly created value.</returns>
    public static T GetOrCreate<T>(string key, TimeSpan ttl, Func<T> factory, string? group = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        ValidateTtl(ttl);

        if (Store.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            Interlocked.Increment(ref Hits);
            return (T)entry.Value!;
        }

        Interlocked.Increment(ref Misses);
        var value = factory();

        Store[key] = new CacheEntry(
            value: value,
            expiresAtMs: Environment.TickCount64 + (long)ttl.TotalMilliseconds,
            group: group);

        return value;
    }

    /// <summary>
    /// Returns a cached value for the specified key, or asynchronously creates and caches a new value using
    /// the factory if the entry does not exist or has expired.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The unique cache key.</param>
    /// <param name="ttl">The time-to-live for the cache entry. Must not be negative.</param>
    /// <param name="factory">The asynchronous factory function invoked to produce the value on a cache miss.</param>
    /// <param name="group">An optional group name for bulk invalidation via <see cref="InvalidateGroup"/>.</param>
    /// <returns>The cached or freshly created value.</returns>
    public static async Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory, string? group = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        ValidateTtl(ttl);

        if (Store.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            Interlocked.Increment(ref Hits);
            return (T)entry.Value!;
        }

        Interlocked.Increment(ref Misses);
        var value = await factory().ConfigureAwait(false);

        Store[key] = new CacheEntry(
            value: value,
            expiresAtMs: Environment.TickCount64 + (long)ttl.TotalMilliseconds,
            group: group);

        return value;
    }

    #endregion

    #region Get / Set / TryGet

    /// <summary>
    /// Attempts to retrieve a cached value for the specified key.
    /// Returns <see langword="false"/> if the key does not exist or the entry has expired.
    /// </summary>
    /// <typeparam name="T">The expected type of the cached value.</typeparam>
    /// <param name="key">The cache key to look up.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the cached value; otherwise <see langword="default"/>.</param>
    /// <returns><see langword="true"/> if a valid (non-expired) entry was found; otherwise <see langword="false"/>.</returns>
    public static bool TryGet<T>(string key, out T? value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (Store.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            Interlocked.Increment(ref Hits);
            value = (T?)entry.Value;
            return true;
        }

        Interlocked.Increment(ref Misses);
        value = default;
        return false;
    }

    /// <summary>
    /// Adds or replaces a cache entry for the specified key with the given value and TTL.
    /// </summary>
    /// <typeparam name="T">The type of the value to cache.</typeparam>
    /// <param name="key">The unique cache key.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="ttl">The time-to-live for the cache entry. Must not be negative.</param>
    /// <param name="group">An optional group name for bulk invalidation via <see cref="InvalidateGroup"/>.</param>
    public static void Set<T>(string key, T value, TimeSpan ttl, string? group = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ValidateTtl(ttl);

        Store[key] = new CacheEntry(
            value: value,
            expiresAtMs: Environment.TickCount64 + (long)ttl.TotalMilliseconds,
            group: group);
    }

    /// <summary>
    /// Gets a cached value or returns the specified default if the key does not exist or has expired.
    /// </summary>
    /// <typeparam name="T">The expected type of the cached value.</typeparam>
    /// <param name="key">The cache key to look up.</param>
    /// <param name="defaultValue">The value to return on a cache miss.</param>
    /// <returns>The cached value, or <paramref name="defaultValue"/> if not found or expired.</returns>
    public static T? GetOrDefault<T>(string key, T? defaultValue = default)
    {
        return TryGet<T>(key, out var value) ? value : defaultValue;
    }

    #endregion

    #region Invalidation

    /// <summary>
    /// Removes a single cache entry by key.
    /// </summary>
    /// <param name="key">The cache key to remove.</param>
    /// <returns><see langword="true"/> if the entry was found and removed; otherwise <see langword="false"/>.</returns>
    public static bool Invalidate(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return Store.TryRemove(key, out _);
    }

    /// <summary>
    /// Removes all cache entries belonging to the specified group.
    /// </summary>
    /// <param name="group">The group name whose entries should be removed.</param>
    /// <returns>The number of entries that were removed.</returns>
    public static int InvalidateGroup(string group)
    {
        ArgumentNullException.ThrowIfNull(group);

        int removed = 0;

        foreach (var kvp in Store)
        {
            if (string.Equals(kvp.Value.Group, group, StringComparison.Ordinal))
            {
                if (Store.TryRemove(kvp.Key, out _))
                    removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Removes all entries from the cache and resets statistics.
    /// </summary>
    public static void Clear()
    {
        Store.Clear();
        Interlocked.Exchange(ref Hits, 0);
        Interlocked.Exchange(ref Misses, 0);
    }

    /// <summary>
    /// Removes all expired entries from the cache. Call this periodically if you want
    /// to reclaim memory from entries that have expired but not been accessed.
    /// </summary>
    /// <returns>The number of expired entries that were removed.</returns>
    public static int Cleanup()
    {
        int removed = 0;

        foreach (var kvp in Store)
        {
            if (kvp.Value.IsExpired)
            {
                if (Store.TryRemove(kvp.Key, out _))
                    removed++;
            }
        }

        return removed;
    }

    #endregion

    #region Inspection

    /// <summary>
    /// Gets the current number of entries in the cache, including expired entries not yet cleaned up.
    /// </summary>
    public static int Count => Store.Count;

    /// <summary>
    /// Checks whether an entry exists for the specified key and has not expired.
    /// </summary>
    /// <param name="key">The cache key to check.</param>
    /// <returns><see langword="true"/> if a valid (non-expired) entry exists; otherwise <see langword="false"/>.</returns>
    public static bool Contains(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return Store.TryGetValue(key, out var entry) && !entry.IsExpired;
    }

    /// <summary>
    /// Returns a snapshot of all cache keys currently stored (including expired entries).<br/>
    /// See <see cref="GetActiveKeys"/> to get only non-expired keys.
    /// </summary>
    /// <returns>A list of all keys in the cache.</returns>
    public static List<string> GetKeys()
    {
        return Store.Keys.ToList();
    }

    /// <summary>
    /// Returns a snapshot of all valid (non-expired) cache keys.<br/>
    /// See <see cref="GetKeys"/> to get all keys, including expired ones.
    /// </summary>
    /// <returns>A list of non-expired keys in the cache.</returns>
    public static List<string> GetActiveKeys()
    {
        return Store.Where(kvp => !kvp.Value.IsExpired).Select(kvp => kvp.Key).ToList();
    }

    /// <summary>
    /// Returns a snapshot of all cache keys belonging to the specified group.
    /// </summary>
    /// <param name="group">The group name to filter by.</param>
    /// <returns>A list of keys in the specified group.</returns>
    public static List<string> GetKeysByGroup(string group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return Store
            .Where(kvp => string.Equals(kvp.Value.Group, group, StringComparison.Ordinal))
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// Returns statistics about the cache including hit/miss counts and entry information.
    /// </summary>
    /// <returns>A <see cref="CacheStatistics"/> snapshot of the current cache state.</returns>
    public static CacheStatistics GetStatistics()
    {
        var hits = Interlocked.Read(ref Hits);
        var misses = Interlocked.Read(ref Misses);
        var entryCount = Store.Count;
        var expiredCount = Store.Values.Count(e => e.IsExpired);

        return new CacheStatistics(hits, misses, entryCount, expiredCount);
    }

    /// <summary>
    /// Resets the hit and miss counters without clearing the cache entries.
    /// </summary>
    public static void ResetStatistics()
    {
        Interlocked.Exchange(ref Hits, 0);
        Interlocked.Exchange(ref Misses, 0);
    }

    #endregion

    #region Refresh

    /// <summary>
    /// Forces a cache entry to be refreshed by invoking the factory, regardless of whether the entry has expired.
    /// If the key does not exist, a new entry is created.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key to refresh.</param>
    /// <param name="ttl">The time-to-live for the refreshed entry. Must not be negative.</param>
    /// <param name="factory">The factory function invoked to produce the new value.</param>
    /// <param name="group">An optional group name for bulk invalidation.</param>
    /// <returns>The newly produced value.</returns>
    public static T Refresh<T>(string key, TimeSpan ttl, Func<T> factory, string? group = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        ValidateTtl(ttl);

        var value = factory();

        Store[key] = new CacheEntry(
            value: value,
            expiresAtMs: Environment.TickCount64 + (long)ttl.TotalMilliseconds,
            group: group);

        return value;
    }

    /// <summary>
    /// Forces a cache entry to be refreshed asynchronously by invoking the factory, regardless of whether
    /// the entry has expired. If the key does not exist, a new entry is created.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key to refresh.</param>
    /// <param name="ttl">The time-to-live for the refreshed entry. Must not be negative.</param>
    /// <param name="factory">The asynchronous factory function invoked to produce the new value.</param>
    /// <param name="group">An optional group name for bulk invalidation.</param>
    /// <returns>The newly produced value.</returns>
    public static async Task<T> RefreshAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory, string? group = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        ValidateTtl(ttl);

        var value = await factory().ConfigureAwait(false);

        Store[key] = new CacheEntry(
            value: value,
            expiresAtMs: Environment.TickCount64 + (long)ttl.TotalMilliseconds,
            group: group);

        return value;
    }

    #endregion

    #region Internal

    private static void ValidateTtl(TimeSpan ttl)
    {
        if (ttl < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must not be negative.");
    }

    /// <summary>
    /// Internal cache entry storing a boxed value, expiration time, and optional group.
    /// </summary>
    private sealed class CacheEntry
    {
        public object? Value { get; }
        public long ExpiresAtMs { get; }
        public string? Group { get; }
        public bool IsExpired => Environment.TickCount64 >= ExpiresAtMs;

        public CacheEntry(object? value, long expiresAtMs, string? group)
        {
            Value = value;
            ExpiresAtMs = expiresAtMs;
            Group = group;
        }
    }

    #endregion
}
