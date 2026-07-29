using Newtonsoft.Json;
using NoireLib.Database;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace NoireLib.ObservedStore;

public partial class NoireObservedStore
{
    private const string UpsertSql =
        "INSERT INTO \"" + ObservationRecord.Table + "\" " +
        "(\"scope\", \"character_id\", \"key\", \"value\", \"value_type\", \"source\", \"observed_at\", \"expires_after\") " +
        "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7) " +
        "ON CONFLICT(\"scope\", \"character_id\", \"key\") DO UPDATE SET " +
        "\"value\" = excluded.\"value\", " +
        "\"value_type\" = excluded.\"value_type\", " +
        "\"source\" = excluded.\"source\", " +
        "\"observed_at\" = excluded.\"observed_at\", " +
        "\"expires_after\" = excluded.\"expires_after\"";

    /// <summary>
    /// Writes down a sighting in the store's default scope, replacing whatever was recorded under the same key.
    /// </summary>
    /// <typeparam name="T">The value's type. It is serialized as JSON, so it has to be serializable.</typeparam>
    /// <param name="key">The key to remember the value under.</param>
    /// <param name="value">The value as it was seen.</param>
    /// <param name="options">Optional sighting details; every one of them has a default.</param>
    /// <returns>True when the observation was stored.</returns>
    public bool Record<T>(string key, T value, RecordOptions? options = null)
        => Default.Record(key, value, options);

    /// <summary>
    /// Writes down many sightings at once in the store's default scope, in one transaction.
    /// </summary>
    /// <typeparam name="T">The values' type.</typeparam>
    /// <param name="values">The keys and the values seen under them.</param>
    /// <param name="options">Optional sighting details, applied to every one of them.</param>
    /// <returns>How many observations were stored.</returns>
    public int RecordMany<T>(IEnumerable<KeyValuePair<string, T>> values, RecordOptions? options = null)
        => Default.RecordMany(values, options);

    /// <summary>Forgets one observation in the store's default scope.</summary>
    /// <param name="key">The key to forget.</param>
    /// <returns>True when something was removed.</returns>
    public bool Forget(string key) => Default.Forget(key);

    /// <summary>
    /// Deletes every observation that has outlived its own expiry, across every scope and character; an expired
    /// observation is never returned by a read whether pruned or not, so this only reclaims space.
    /// </summary>
    /// <returns>How many observations were removed.</returns>
    public int PruneExpired()
    {
        if (!IsUsable(nameof(PruneExpired)))
            return 0;

        var now = FormatTimestamp(DateTimeOffset.UtcNow);

        var removed = Execute(db => db.Execute(
            "DELETE FROM \"" + ObservationRecord.Table + "\" WHERE \"expires_after\" IS NOT NULL " +
            "AND julianday(@p0) - julianday(\"observed_at\") > \"expires_after\" / 86400.0",
            [now]));

        if (removed > 0)
            RaisePruned(removed, ObservationScope.Character, 0, "expired");

        return removed;
    }

    #region Internals used by ObservationView

    internal bool RecordCore<T>(
        string key,
        T value,
        ObservationScope fallbackScope,
        ulong? fallbackCharacterId,
        RecordOptions? options)
    {
        if (string.IsNullOrWhiteSpace(key) || !IsUsable(nameof(Record)))
            return false;

        var scope = options?.Scope ?? fallbackScope;

        if (!TryResolveCharacter(scope, options?.CharacterId ?? fallbackCharacterId, out var characterId))
        {
            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Record('{key}') ignored: no character to key a character-scoped observation on.");

            return false;
        }

        var info = BuildInfo(key, scope, characterId, options);
        var previous = ShouldReportReplacement ? DescribeCore(key, scope, characterId) : null;

        var stored = Execute(db => db.Execute(UpsertSql, BuildParameters(info, value)) > 0);

        if (stored)
            RaiseRecorded(info, previous);

        return stored;
    }

    internal int RecordManyCore<T>(
        IEnumerable<KeyValuePair<string, T>> values,
        ObservationScope fallbackScope,
        ulong? fallbackCharacterId,
        RecordOptions? options)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (!IsUsable(nameof(RecordMany)))
            return 0;

        var scope = options?.Scope ?? fallbackScope;

        if (!TryResolveCharacter(scope, options?.CharacterId ?? fallbackCharacterId, out var characterId))
        {
            if (EnableLogging)
                NoireLogger.LogDebug(this, "RecordMany ignored: no character to key character-scoped observations on.");

            return 0;
        }

        var pending = new List<(ObservationInfo Info, ObservationInfo? Previous, IReadOnlyList<object?> Parameters)>();

        foreach (var pair in values)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            var info = BuildInfo(pair.Key, scope, characterId, options);
            var previous = ShouldReportReplacement ? DescribeCore(pair.Key, scope, characterId) : null;
            pending.Add((info, previous, BuildParameters(info, pair.Value)));
        }

        if (pending.Count == 0)
            return 0;

        var written = Execute(db =>
        {
            // One transaction rather than one commit per row: a retainer's inventory is hundreds of observations and
            // SQLite commits each statement on its own otherwise.
            var owned = db.BeginTransaction();
            var count = 0;

            try
            {
                foreach (var entry in pending)
                {
                    if (db.Execute(UpsertSql, entry.Parameters) > 0)
                        count++;
                }

                if (owned)
                    db.Commit();

                return count;
            }
            catch
            {
                if (owned)
                    db.Rollback();

                throw;
            }
        });

        if (written > 0)
        {
            foreach (var entry in pending)
                RaiseRecorded(entry.Info, entry.Previous);
        }

        return written;
    }

    internal bool ForgetCore(string key, ObservationScope scope, ulong? requestedCharacterId)
    {
        if (string.IsNullOrWhiteSpace(key) || !IsUsable(nameof(Forget)))
            return false;

        if (!TryResolveCharacter(scope, requestedCharacterId, out var characterId))
            return false;

        var info = DescribeCore(key, scope, characterId);

        if (info == null)
            return false;

        var removed = Execute(db => db.Execute(
            "DELETE FROM \"" + ObservationRecord.Table + "\" WHERE \"scope\" = @p0 AND \"character_id\" = @p1 AND \"key\" = @p2",
            [ScopeText(scope), CharacterText(characterId), key])) > 0;

        if (removed)
            RaiseForgotten(info.Value);

        return removed;
    }

    internal int ForgetPrefixCore(string keyPrefix, ObservationScope scope, ulong? requestedCharacterId)
    {
        if (string.IsNullOrWhiteSpace(keyPrefix) || !IsUsable(nameof(ForgetCore)))
            return 0;

        if (!TryResolveCharacter(scope, requestedCharacterId, out var characterId))
            return 0;

        var removed = Execute(db => db.Execute(
            "DELETE FROM \"" + ObservationRecord.Table + "\" WHERE \"scope\" = @p0 AND \"character_id\" = @p1 AND \"key\" LIKE @p2 ESCAPE '\\'",
            [ScopeText(scope), CharacterText(characterId), EscapeLike(keyPrefix) + "%"]));

        if (removed > 0)
            RaisePruned(removed, scope, characterId, $"prefix '{keyPrefix}'");

        return removed;
    }

    internal int PruneCore(TimeSpan olderThan, ObservationScope scope, ulong? requestedCharacterId)
    {
        if (!IsUsable(nameof(PruneCore)))
            return 0;

        if (!TryResolveCharacter(scope, requestedCharacterId, out var characterId))
            return 0;

        var cutoff = FormatTimestamp(DateTimeOffset.UtcNow - olderThan);

        var removed = Execute(db => db.Execute(
            "DELETE FROM \"" + ObservationRecord.Table + "\" WHERE \"scope\" = @p0 AND \"character_id\" = @p1 AND \"observed_at\" < @p2",
            [ScopeText(scope), CharacterText(characterId), cutoff]));

        if (removed > 0)
            RaisePruned(removed, scope, characterId, $"older than {olderThan}");

        return removed;
    }

    internal int ClearCore(ObservationScope scope, ulong? requestedCharacterId)
    {
        if (!IsUsable(nameof(ClearCore)))
            return 0;

        if (!TryResolveCharacter(scope, requestedCharacterId, out var characterId))
            return 0;

        var removed = Execute(db => db.Execute(
            "DELETE FROM \"" + ObservationRecord.Table + "\" WHERE \"scope\" = @p0 AND \"character_id\" = @p1",
            [ScopeText(scope), CharacterText(characterId)]));

        if (removed > 0)
            RaisePruned(removed, scope, characterId, "cleared");

        return removed;
    }

    #endregion

    #region Shared plumbing

    /// <summary>
    /// Whether anything is listening closely enough to justify the extra read that reports what a record replaced.
    /// Nobody listening means the read is pure cost, and a bulk import would pay it once per row.
    /// </summary>
    private bool ShouldReportReplacement
        => registry.HasSubscribers(typeof(ObservationRecordedEvent))
           || (ActiveOptions.PublishModuleEvents && ActiveOptions.EventBus != null);

    private ObservationInfo BuildInfo(string key, ObservationScope scope, ulong characterId, RecordOptions? options)
    {
        var expires = options?.ExpiresAfter ?? ActiveOptions.DefaultExpiresAfter;

        // A caller passing zero or a negative span is saying "this one never goes stale", which is the only way to
        // opt out of a store-wide default.
        if (expires is { } lifetime && lifetime <= TimeSpan.Zero)
            expires = null;

        var source = string.IsNullOrWhiteSpace(options?.Source) ? ActiveOptions.DefaultSource : options!.Source!;

        return new ObservationInfo(
            key,
            scope,
            characterId,
            source,
            (options?.ObservedAt ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            expires);
    }

    private IReadOnlyList<object?> BuildParameters<T>(ObservationInfo info, T value)
    {
        var json = JsonConvert.SerializeObject(value, SerializerSettings);

        return
        [
            ScopeText(info.Scope),
            CharacterText(info.CharacterId),
            info.Key,
            json,
            typeof(T) == typeof(object) ? value?.GetType().FullName : typeof(T).FullName,
            info.Source,
            FormatTimestamp(info.ObservedAt),
            info.ExpiresAfter?.TotalSeconds,
        ];
    }

    /// <summary>Runs a unit of database work behind the store's error boundary, returning a default on failure.</summary>
    private TResult Execute<TResult>(Func<NoireDatabase, TResult> work, TResult fallback = default!)
    {
        var databaseName = ActiveOptions.DatabaseName;

        return SafeExecutor.ExecuteSafely(
            () => ObservationRecord.Scoped(databaseName, work),
            fallback) ?? fallback;
    }

    internal static string ScopeText(ObservationScope scope)
        => scope == ObservationScope.Shared ? "shared" : "character";

    internal static ObservationScope ParseScope(string? text)
        => string.Equals(text, "shared", StringComparison.OrdinalIgnoreCase)
            ? ObservationScope.Shared
            : ObservationScope.Character;

    internal static string CharacterText(ulong characterId)
        => characterId.ToString(CultureInfo.InvariantCulture);

    internal static string FormatTimestamp(DateTimeOffset value)
        => value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    /// <summary>
    /// Escapes the wildcards SQLite's LIKE would otherwise read as pattern syntax, so a key prefix containing an
    /// underscore matches that underscore rather than any character.
    /// </summary>
    internal static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    #endregion
}
