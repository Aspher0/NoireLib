using NoireLib.Database;
using System;
using System.Collections.Generic;
using System.Threading;

namespace NoireLib.ObservedStore;

/// <summary>
/// The row an observation is stored as. One row per (scope, character, key), replaced in place when the same key is
/// observed again.
/// </summary>
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public sealed class ObservationRecord : NoireDbModelBase<ObservationRecord>
{
    /// <summary>The table every store writes into, whatever database file it was pointed at.</summary>
    public const string Table = "observations";

    // NoireDbModelBase resolves its database from a parameterless instance, so the name has to reach the constructor
    // out of band (the same mechanism HistoryLogEntryModel uses); every entry point here goes through Scoped() for it.
    private static readonly AsyncLocal<string?> DatabaseNameContext = new();

    private readonly string databaseName;

    protected override string DatabaseName => databaseName;
    protected override string? TableName => Table;
    protected override string PrimaryKey => "id";
    protected override bool LoadDatabaseOnInit => false;

    [NoireDbColumn("id", IsPrimaryKey = true, IsAutoIncrement = true, IsNullable = false)]
    public long Id
    {
        get => GetColumn<long>("id");
        set => SetColumn("id", value);
    }

    [NoireDbColumn("scope", Type = "TEXT", IsNullable = false)]
    public string Scope
    {
        get => GetColumn<string>("scope") ?? string.Empty;
        set => SetColumn("scope", value);
    }

    // Stored as text because a content id is a ulong and SQLite integers are signed, so the top half of the range
    // would round-trip as a negative number.
    [NoireDbColumn("character_id", Type = "TEXT", IsNullable = false)]
    public string CharacterId
    {
        get => GetColumn<string>("character_id") ?? "0";
        set => SetColumn("character_id", value);
    }

    [NoireDbColumn("key", Type = "TEXT", IsNullable = false)]
    public string Key
    {
        get => GetColumn<string>("key") ?? string.Empty;
        set => SetColumn("key", value);
    }

    [NoireDbColumn("value", Type = "TEXT")]
    public string? Value
    {
        get => GetColumn<string?>("value");
        set => SetColumn("value", value);
    }

    // Diagnostic only. The store deserializes into the type the caller asked for, never into the type a stored
    // payload claims to be, so this is never fed back into the serializer.
    [NoireDbColumn("value_type", Type = "TEXT")]
    public string? ValueType
    {
        get => GetColumn<string?>("value_type");
        set => SetColumn("value_type", value);
    }

    [NoireDbColumn("source", Type = "TEXT", IsNullable = false)]
    public string Source
    {
        get => GetColumn<string>("source") ?? string.Empty;
        set => SetColumn("source", value);
    }

    // ISO-8601 round-trip text, parsed here rather than cast by the ORM so the offset survives.
    [NoireDbColumn("observed_at", Type = "TEXT", IsNullable = false)]
    public string ObservedAt
    {
        get => GetColumn<string>("observed_at") ?? string.Empty;
        set => SetColumn("observed_at", value);
    }

    [NoireDbColumn("expires_after", Type = "REAL")]
    public double? ExpiresAfterSeconds
    {
        get => GetColumn<double?>("expires_after");
        set => SetColumn("expires_after", value);
    }

    public ObservationRecord()
    {
        databaseName = string.IsNullOrWhiteSpace(DatabaseNameContext.Value)
            ? NoireObservedStore.DefaultDatabaseName
            : DatabaseNameContext.Value!;
    }

    /// <summary>
    /// Runs a unit of work against one named database, with the table and its uniqueness index in place.
    /// </summary>
    /// <typeparam name="TResult">The work's result type.</typeparam>
    /// <param name="databaseName">The database file to work against.</param>
    /// <param name="work">The work, given the live database.</param>
    /// <returns>The work's result.</returns>
    internal static TResult Scoped<TResult>(string databaseName, Func<NoireDatabase, TResult> work)
    {
        var previous = DatabaseNameContext.Value;
        DatabaseNameContext.Value = databaseName;

        try
        {
            var model = new ObservationRecord();
            model.EnsureTableCreated();

            var db = model.GetDb();
            EnsureIndex(db, databaseName);

            return work(db);
        }
        finally
        {
            DatabaseNameContext.Value = previous;
        }
    }

    /// <summary>
    /// Runs a query against one named database through the fluent builder.
    /// </summary>
    /// <typeparam name="TResult">The query's result type.</typeparam>
    /// <param name="databaseName">The database file to query.</param>
    /// <param name="query">The query.</param>
    /// <returns>The query's result.</returns>
    internal static TResult Query<TResult>(string databaseName, Func<QueryBuilder<ObservationRecord>, TResult> query)
    {
        var previous = DatabaseNameContext.Value;
        DatabaseNameContext.Value = databaseName;

        try
        {
            var model = new ObservationRecord();
            model.EnsureTableCreated();

            var db = model.GetDb();
            EnsureIndex(db, databaseName);

            return query(new QueryBuilder<ObservationRecord>(model.ResolvedTableName, db));
        }
        finally
        {
            DatabaseNameContext.Value = previous;
        }
    }

    // The unique index turns recording into an upsert instead of a read-then-write race; the column definitions
    // cannot express it, so it is created alongside the table. Tracked per database so the statement runs once
    // rather than on every call.
    private static readonly HashSet<string> IndexedDatabases = new(StringComparer.OrdinalIgnoreCase);

    private static void EnsureIndex(NoireDatabase db, string databaseName)
    {
        // The lock is held across the creation, not just across the bookkeeping. Marking the database as indexed
        // first would let a second caller start upserting while the index is still being built, and an ON CONFLICT
        // clause with no matching unique index does not degrade: it throws.
        lock (IndexedDatabases)
        {
            if (IndexedDatabases.Contains(databaseName))
                return;

            db.Execute($"CREATE UNIQUE INDEX IF NOT EXISTS idx_{Table}_identity ON {Table} (scope, character_id, key)");
            db.Execute($"CREATE INDEX IF NOT EXISTS idx_{Table}_observed_at ON {Table} (observed_at)");

            IndexedDatabases.Add(databaseName);
        }
    }

    /// <summary>Forgets which databases have had their index created, so a test can start from nothing.</summary>
    internal static void ResetIndexCache()
    {
        lock (IndexedDatabases)
            IndexedDatabases.Clear();
    }
}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
