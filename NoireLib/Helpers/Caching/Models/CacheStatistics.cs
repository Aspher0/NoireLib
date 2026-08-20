namespace NoireLib.Helpers;

/// <summary>
/// Represents a snapshot of cache usage statistics.
/// </summary>
/// <param name="Hits">The total number of cache hits (successful lookups of valid entries).</param>
/// <param name="Misses">The total number of cache misses (lookups that required factory invocation).</param>
/// <param name="EntryCount">The current number of entries in the cache, including expired ones not yet cleaned up.</param>
/// <param name="ExpiredCount">The number of expired entries currently in the cache awaiting cleanup.</param>
public sealed record CacheStatistics(long Hits, long Misses, int EntryCount, int ExpiredCount)
{
    /// <summary>
    /// Gets the cache hit rate as a value between 0.0 and 1.0.
    /// Returns 0.0 if no accesses have been recorded.
    /// </summary>
    public double HitRate => TotalAccesses > 0 ? (double)Hits / TotalAccesses : 0.0;

    /// <summary>
    /// Gets the total number of cache accesses (hits + misses).
    /// </summary>
    public long TotalAccesses => Hits + Misses;

    /// <summary>
    /// Gets the number of active (non-expired) entries in the cache.
    /// </summary>
    public int ActiveEntryCount => EntryCount - ExpiredCount;
}
