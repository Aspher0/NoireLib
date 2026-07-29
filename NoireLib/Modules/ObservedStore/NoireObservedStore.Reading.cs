using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace NoireLib.ObservedStore;

public partial class NoireObservedStore
{
    private const string SelectColumns =
        "\"key\", \"scope\", \"character_id\", \"value\", \"source\", \"observed_at\", \"expires_after\"";

    // An entry past its own expiry is filtered out in SQL rather than after hydration, so a bulk read never
    // deserializes rows it is about to discard.
    private const string NotExpiredClause =
        " AND (\"expires_after\" IS NULL OR julianday(@p{0}) - julianday(\"observed_at\") <= \"expires_after\" / 86400.0)";

    /// <summary>
    /// Reads back what was last seen under a key in the store's default scope.
    /// </summary>
    /// <typeparam name="T">The type the value was recorded as.</typeparam>
    /// <param name="key">The key to read.</param>
    /// <param name="includeExpired">Whether to return an observation that has outlived its own expiry.</param>
    /// <returns>The observation, or null when the store has never seen this key.</returns>
    public Observation<T>? Read<T>(string key, bool includeExpired = false)
        => Default.Read<T>(key, includeExpired);

    /// <summary>Reads back a value in the store's default scope, ignoring the sighting's metadata.</summary>
    /// <typeparam name="T">The type the value was recorded as.</typeparam>
    /// <param name="key">The key to read.</param>
    /// <param name="fallback">What to answer when the store has never seen this key.</param>
    /// <returns>The value, or <paramref name="fallback"/>.</returns>
    public T? ReadValue<T>(string key, T? fallback = default) => Default.ReadValue(key, fallback);

    /// <summary>
    /// Reads back a key only when the sighting is recent enough.
    /// </summary>
    /// <typeparam name="T">The type the value was recorded as.</typeparam>
    /// <param name="key">The key to read.</param>
    /// <param name="maxAge">The oldest sighting still worth using.</param>
    /// <returns>The observation, or null when there is none or it is older than that.</returns>
    public Observation<T>? ReadFresh<T>(string key, TimeSpan maxAge) => Default.ReadFresh<T>(key, maxAge);

    /// <summary>Reads a sighting's metadata in the store's default scope without deserializing its value.</summary>
    /// <param name="key">The key to describe.</param>
    /// <param name="includeExpired">Whether to describe an observation that has outlived its own expiry.</param>
    /// <returns>The metadata, or null when the store has never seen this key.</returns>
    public ObservationInfo? Describe(string key, bool includeExpired = false) => Default.Describe(key, includeExpired);

    /// <summary>Whether the store has ever seen a key in its default scope.</summary>
    /// <param name="key">The key to test.</param>
    /// <param name="includeExpired">Whether an expired observation still counts as known.</param>
    /// <returns>True when there is an observation.</returns>
    public bool Knows(string key, bool includeExpired = false) => Default.Knows(key, includeExpired);

    #region Internals used by ObservationView

    internal Observation<T>? ReadCore<T>(string key, ObservationScope scope, ulong? requestedCharacterId, bool includeExpired)
    {
        var row = FetchRow(key, scope, requestedCharacterId, includeExpired);

        if (row == null)
            return null;

        var info = ToInfo(row);

        return TryDeserialize<T>(row, info, out var value) ? new Observation<T>(info, value!) : null;
    }

    internal ObservationInfo? DescribeCore(string key, ObservationScope scope, ulong? requestedCharacterId, bool includeExpired = true)
    {
        var row = FetchRow(key, scope, requestedCharacterId, includeExpired);
        return row == null ? null : ToInfo(row);
    }

    internal IReadOnlyList<Observation<T>> ReadAllCore<T>(
        string? keyPrefix,
        ObservationScope scope,
        ulong? requestedCharacterId,
        bool includeExpired)
    {
        var results = new List<Observation<T>>();

        foreach (var row in FetchRows(keyPrefix, scope, requestedCharacterId, includeExpired))
        {
            var info = ToInfo(row);

            if (TryDeserialize<T>(row, info, out var value))
                results.Add(new Observation<T>(info, value!));
        }

        return results;
    }

    internal IReadOnlyList<ObservationInfo> DescribeAllCore(
        string? keyPrefix,
        ObservationScope scope,
        ulong? requestedCharacterId,
        bool includeExpired)
    {
        var results = new List<ObservationInfo>();

        foreach (var row in FetchRows(keyPrefix, scope, requestedCharacterId, includeExpired))
            results.Add(ToInfo(row));

        return results;
    }

    internal IReadOnlyList<string> KeysCore(
        string? keyPrefix,
        ObservationScope scope,
        ulong? requestedCharacterId,
        bool includeExpired)
    {
        var keys = new List<string>();

        foreach (var row in FetchRows(keyPrefix, scope, requestedCharacterId, includeExpired))
            keys.Add(Text(row, "key"));

        return keys;
    }

    internal int CountCore(string? keyPrefix, ObservationScope scope, ulong? requestedCharacterId, bool includeExpired)
    {
        if (!IsUsable(nameof(CountCore)) || !TryResolveCharacter(scope, requestedCharacterId, out var characterId))
            return 0;

        var (where, parameters) = BuildFilter(keyPrefix, scope, characterId, includeExpired);

        var count = Execute(db => db.FetchScalar(
            "SELECT COUNT(*) FROM \"" + ObservationRecord.Table + "\" WHERE " + where, parameters));

        return count == null ? 0 : Convert.ToInt32(count, CultureInfo.InvariantCulture);
    }

    #endregion

    #region Row plumbing

    private Dictionary<string, object?>? FetchRow(
        string key,
        ObservationScope scope,
        ulong? requestedCharacterId,
        bool includeExpired)
    {
        if (string.IsNullOrWhiteSpace(key) || !IsUsable(nameof(Read)))
            return null;

        if (!TryResolveCharacter(scope, requestedCharacterId, out var characterId))
            return null;

        var parameters = new List<object?> { ScopeText(scope), CharacterText(characterId), key };
        var where = "\"scope\" = @p0 AND \"character_id\" = @p1 AND \"key\" = @p2";

        if (!includeExpired)
        {
            where += string.Format(CultureInfo.InvariantCulture, NotExpiredClause, parameters.Count);
            parameters.Add(FormatTimestamp(DateTimeOffset.UtcNow));
        }

        return Execute(db => db.Fetch(
            "SELECT " + SelectColumns + " FROM \"" + ObservationRecord.Table + "\" WHERE " + where + " LIMIT 1",
            parameters));
    }

    private List<Dictionary<string, object?>> FetchRows(
        string? keyPrefix,
        ObservationScope scope,
        ulong? requestedCharacterId,
        bool includeExpired)
    {
        if (!IsUsable(nameof(ReadAllCore)) || !TryResolveCharacter(scope, requestedCharacterId, out var characterId))
            return [];

        var (where, parameters) = BuildFilter(keyPrefix, scope, characterId, includeExpired);

        return Execute(
            db => db.FetchAll(
                "SELECT " + SelectColumns + " FROM \"" + ObservationRecord.Table + "\" WHERE " + where + " ORDER BY \"key\"",
                parameters),
            []) ?? [];
    }

    private static (string Where, List<object?> Parameters) BuildFilter(
        string? keyPrefix,
        ObservationScope scope,
        ulong characterId,
        bool includeExpired)
    {
        var parameters = new List<object?> { ScopeText(scope), CharacterText(characterId) };
        var where = "\"scope\" = @p0 AND \"character_id\" = @p1";

        if (!string.IsNullOrEmpty(keyPrefix))
        {
            where += $" AND \"key\" LIKE @p{parameters.Count} ESCAPE '\\'";
            parameters.Add(EscapeLike(keyPrefix) + "%");
        }

        if (!includeExpired)
        {
            where += string.Format(CultureInfo.InvariantCulture, NotExpiredClause, parameters.Count);
            parameters.Add(FormatTimestamp(DateTimeOffset.UtcNow));
        }

        return (where, parameters);
    }

    private bool TryDeserialize<T>(Dictionary<string, object?> row, ObservationInfo info, out T? value)
    {
        var json = row.TryGetValue("value", out var raw) ? raw as string : null;

        if (json == null)
        {
            value = default;
            return false;
        }

        try
        {
            // Into the type the caller asked for, never into a type the stored payload names. See
            // ObservedStoreOptions.SerializerSettings.
            value = JsonConvert.DeserializeObject<T>(json, SerializerSettings);
            return true;
        }
        catch (Exception ex)
        {
            // Deliberately broad. A read is contracted never to throw, and a payload recorded as one type and read
            // as another surfaces as a JsonException, an InvalidCastException or a FormatException depending on how
            // far the deserializer got, so catching only the first would break the contract for the rest.
            value = default;

            NoireLogger.LogWarning(this,
                $"Observation '{info.Key}' could not be read as {typeof(T).Name}: {ex.Message}. " +
                "It was most likely recorded as a different type.");

            return false;
        }
    }

    private static ObservationInfo ToInfo(Dictionary<string, object?> row)
    {
        var observedAt = DateTimeOffset.TryParse(
            Text(row, "observed_at"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

        TimeSpan? expires = null;

        if (row.TryGetValue("expires_after", out var rawExpiry) && rawExpiry != null)
        {
            var seconds = Convert.ToDouble(rawExpiry, CultureInfo.InvariantCulture);

            if (seconds > 0)
                expires = TimeSpan.FromSeconds(seconds);
        }

        var characterId = ulong.TryParse(Text(row, "character_id"), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : 0UL;

        return new ObservationInfo(
            Text(row, "key"),
            ParseScope(Text(row, "scope")),
            characterId,
            Text(row, "source"),
            observedAt,
            expires);
    }

    private static string Text(Dictionary<string, object?> row, string column)
        => row.TryGetValue(column, out var value) && value != null
            ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;

    #endregion
}
