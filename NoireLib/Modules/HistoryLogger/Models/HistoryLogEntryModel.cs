using NoireLib.Database;
using System;
using System.Collections.Generic;

namespace NoireLib.HistoryLogger;

public sealed class HistoryLogEntryModel : NoireDbModelBase<HistoryLogEntryModel>
{
    private static readonly System.Threading.AsyncLocal<string?> DatabaseNameContext = new();
    private readonly string databaseName;

    protected override string DatabaseName => databaseName;
    protected override string? TableName => "history_logs";
    protected override string PrimaryKey => "id";
    protected override bool LoadDatabaseOnInit => false;

    [NoireDbColumn("id", IsPrimaryKey = true, IsAutoIncrement = true, IsNullable = false)]
    public long Id
    {
        get => GetColumn<long>("id");
        set => SetColumn("id", value);
    }

    [NoireDbColumn("timestamp", Type = "TEXT", IsNullable = false)]
    public DateTime Timestamp
    {
        get => GetColumn<DateTime>("timestamp");
        set => SetColumn("timestamp", value);
    }

    [NoireDbColumn("category", Type = "TEXT", IsNullable = false)]
    public string Category
    {
        get => GetColumn<string>("category") ?? string.Empty;
        set => SetColumn("category", value);
    }

    [NoireDbColumn("level", Type = "TEXT", IsNullable = false)]
    public string Level
    {
        get => GetColumn<string>("level") ?? string.Empty;
        set => SetColumn("level", value);
    }

    [NoireDbColumn("message", Type = "TEXT", IsNullable = false)]
    public string Message
    {
        get => GetColumn<string>("message") ?? string.Empty;
        set => SetColumn("message", value);
    }

    [NoireDbColumn("source", Type = "TEXT")]
    public string? Source
    {
        get => GetColumn<string?>("source");
        set => SetColumn("source", value);
    }

    protected override IReadOnlyDictionary<string, DbColumnCast> Casts =>
        new Dictionary<string, DbColumnCast>
        {
            ["timestamp"] = DbColumnCast.DateTime
        };

    public HistoryLogEntryModel()
    {
        databaseName = string.IsNullOrWhiteSpace(DatabaseNameContext.Value)
            ? NoireHistoryLogger.DefaultDatabaseName
            : DatabaseNameContext.Value!;
    }

    internal HistoryLogEntryModel(string databaseName)
    {
        this.databaseName = string.IsNullOrWhiteSpace(databaseName)
            ? NoireHistoryLogger.DefaultDatabaseName
            : databaseName;
    }

    internal static HistoryLogEntryModel Create(string databaseName) => new(databaseName);

    public static TResult ExecuteQuery<TResult>(string databaseName, Func<QueryBuilder<HistoryLogEntryModel>, TResult> action)
    {
        var previous = DatabaseNameContext.Value;
        DatabaseNameContext.Value = databaseName;

        try
        {
            var model = new HistoryLogEntryModel();
            model.EnsureTableCreated();
            var builder = new QueryBuilder<HistoryLogEntryModel>(model.ResolvedTableName, model.GetDb());
            return action(builder);
        }
        finally
        {
            DatabaseNameContext.Value = previous;
        }
    }

    public static void ExecuteQuery(string databaseName, Action<QueryBuilder<HistoryLogEntryModel>> action)
    {
        ExecuteQuery(databaseName, builder =>
        {
            action(builder);
            return true;
        });
    }
}
