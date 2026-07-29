using System;
using System.Collections.Generic;

namespace NoireLib.ObservedStore;

/// <summary>
/// The store's whole operation set, bound to one scope and one character.<br/>
/// Reach one through <see cref="NoireObservedStore.Character"/>, <see cref="NoireObservedStore.Shared"/> or
/// <see cref="NoireObservedStore.Of"/>. The store's own same-named methods are this view bound to
/// <see cref="NoireObservedStore.Default"/>, so <c>store.Record(...)</c> and <c>store.Character.Record(...)</c> are
/// the same call when the default scope is the character one.
/// </summary>
public readonly struct ObservationView
{
    private readonly NoireObservedStore store;
    private readonly ObservationScope scope;
    private readonly ulong? characterId;

    internal ObservationView(NoireObservedStore store, ObservationScope scope, ulong? characterId)
    {
        this.store = store;
        this.scope = scope;
        this.characterId = characterId;
    }

    /// <summary>The scope this view reads and writes in.</summary>
    public ObservationScope Scope => scope;

    /// <summary>
    /// The character this view is bound to, or null when it follows whoever is logged in.
    /// </summary>
    public ulong? CharacterId => characterId;

    /// <summary>
    /// Writes down a sighting, replacing whatever was recorded under the same key.<br/>
    /// <see cref="RecordOptions.Scope"/> and <see cref="RecordOptions.CharacterId"/>, when set, override this view's
    /// own binding.
    /// </summary>
    /// <typeparam name="T">The value's type. It is serialized as JSON, so it has to be serializable.</typeparam>
    /// <param name="key">The key to remember the value under.</param>
    /// <param name="value">The value as it was seen.</param>
    /// <param name="options">Optional sighting details; every one of them has a default.</param>
    /// <returns>True when the observation was stored.</returns>
    public bool Record<T>(string key, T value, RecordOptions? options = null)
        => store.RecordCore(key, value, scope, characterId, options);

    /// <summary>Writes down many sightings at once, in one transaction.</summary>
    /// <typeparam name="T">The values' type.</typeparam>
    /// <param name="values">The keys and the values seen under them.</param>
    /// <param name="options">Optional sighting details, applied to every one of them.</param>
    /// <returns>How many observations were stored.</returns>
    public int RecordMany<T>(IEnumerable<KeyValuePair<string, T>> values, RecordOptions? options = null)
        => store.RecordManyCore(values, scope, characterId, options);

    /// <summary>Reads back what was last seen under a key.</summary>
    /// <typeparam name="T">The type the value was recorded as.</typeparam>
    /// <param name="key">The key to read.</param>
    /// <param name="includeExpired">Whether to return an observation that has outlived its own expiry.</param>
    /// <returns>The observation, or null when the store has never seen this key.</returns>
    public Observation<T>? Read<T>(string key, bool includeExpired = false)
        => store.ReadCore<T>(key, scope, characterId, includeExpired);

    /// <summary>Reads back what was last seen under a key, as a try-pattern.</summary>
    /// <typeparam name="T">The type the value was recorded as.</typeparam>
    /// <param name="key">The key to read.</param>
    /// <param name="observation">The observation, when there is one.</param>
    /// <param name="includeExpired">Whether to return an observation that has outlived its own expiry.</param>
    /// <returns>True when the store has an observation for this key.</returns>
    public bool TryRead<T>(string key, out Observation<T> observation, bool includeExpired = false)
    {
        var found = store.ReadCore<T>(key, scope, characterId, includeExpired);
        observation = found!;
        return found != null;
    }

    /// <summary>Reads back a value, ignoring the sighting's metadata.</summary>
    /// <typeparam name="T">The type the value was recorded as.</typeparam>
    /// <param name="key">The key to read.</param>
    /// <param name="fallback">What to answer when the store has never seen this key.</param>
    /// <returns>The value, or <paramref name="fallback"/>.</returns>
    public T? ReadValue<T>(string key, T? fallback = default)
    {
        var found = store.ReadCore<T>(key, scope, characterId, includeExpired: false);
        return found == null ? fallback : found.Value;
    }

    /// <summary>Reads back a key only when the sighting is recent enough.</summary>
    /// <typeparam name="T">The type the value was recorded as.</typeparam>
    /// <param name="key">The key to read.</param>
    /// <param name="maxAge">The oldest sighting still worth using.</param>
    /// <returns>The observation, or null when there is none or it is older than that.</returns>
    public Observation<T>? ReadFresh<T>(string key, TimeSpan maxAge)
    {
        var found = store.ReadCore<T>(key, scope, characterId, includeExpired: false);
        return found != null && !found.IsOlderThan(maxAge) ? found : null;
    }

    /// <summary>Reads back every observation whose key starts with a prefix.</summary>
    /// <typeparam name="T">The type the values were recorded as.</typeparam>
    /// <param name="keyPrefix">The key prefix, or null for everything in this scope.</param>
    /// <param name="includeExpired">Whether to include observations that have outlived their own expiry.</param>
    /// <returns>The observations, ordered by key.</returns>
    public IReadOnlyList<Observation<T>> ReadAll<T>(string? keyPrefix = null, bool includeExpired = false)
        => store.ReadAllCore<T>(keyPrefix, scope, characterId, includeExpired);

    /// <summary>Reads a sighting's metadata without deserializing its value.</summary>
    /// <param name="key">The key to describe.</param>
    /// <param name="includeExpired">Whether to describe an observation that has outlived its own expiry.</param>
    /// <returns>The metadata, or null when the store has never seen this key.</returns>
    public ObservationInfo? Describe(string key, bool includeExpired = false)
        => store.DescribeCore(key, scope, characterId, includeExpired);

    /// <summary>Reads the metadata of every observation whose key starts with a prefix.</summary>
    /// <param name="keyPrefix">The key prefix, or null for everything in this scope.</param>
    /// <param name="includeExpired">Whether to include observations that have outlived their own expiry.</param>
    /// <returns>The metadata, ordered by key.</returns>
    public IReadOnlyList<ObservationInfo> DescribeAll(string? keyPrefix = null, bool includeExpired = false)
        => store.DescribeAllCore(keyPrefix, scope, characterId, includeExpired);

    /// <summary>Lists the keys this scope holds observations for.</summary>
    /// <param name="keyPrefix">The key prefix, or null for everything in this scope.</param>
    /// <param name="includeExpired">Whether to include observations that have outlived their own expiry.</param>
    /// <returns>The keys, in order.</returns>
    public IReadOnlyList<string> Keys(string? keyPrefix = null, bool includeExpired = false)
        => store.KeysCore(keyPrefix, scope, characterId, includeExpired);

    /// <summary>Whether the store has ever seen a key in this scope.</summary>
    /// <param name="key">The key to test.</param>
    /// <param name="includeExpired">Whether an expired observation still counts as known.</param>
    /// <returns>True when there is an observation.</returns>
    public bool Knows(string key, bool includeExpired = false)
        => store.DescribeCore(key, scope, characterId, includeExpired) != null;

    /// <summary>How many observations this scope holds.</summary>
    /// <param name="keyPrefix">The key prefix, or null for everything in this scope.</param>
    /// <param name="includeExpired">Whether to count observations that have outlived their own expiry.</param>
    /// <returns>The count.</returns>
    public int Count(string? keyPrefix = null, bool includeExpired = false)
        => store.CountCore(keyPrefix, scope, characterId, includeExpired);

    /// <summary>Forgets one observation.</summary>
    /// <param name="key">The key to forget.</param>
    /// <returns>True when something was removed.</returns>
    public bool Forget(string key) => store.ForgetCore(key, scope, characterId);

    /// <summary>Forgets every observation whose key starts with a prefix.</summary>
    /// <param name="keyPrefix">The key prefix.</param>
    /// <returns>How many observations were removed.</returns>
    public int ForgetPrefix(string keyPrefix) => store.ForgetPrefixCore(keyPrefix, scope, characterId);

    /// <summary>Forgets every observation in this scope older than the given span.</summary>
    /// <param name="olderThan">The age past which an observation is dropped.</param>
    /// <returns>How many observations were removed.</returns>
    public int Prune(TimeSpan olderThan) => store.PruneCore(olderThan, scope, characterId);

    /// <summary>Forgets everything in this scope.</summary>
    /// <returns>How many observations were removed.</returns>
    public int Clear() => store.ClearCore(scope, characterId);
}
