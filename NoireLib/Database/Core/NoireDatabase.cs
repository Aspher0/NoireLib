using Microsoft.Data.Sqlite;
using NoireLib.Database.Migrations;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace NoireLib.Database;

/// <summary>
/// Internal core class providing access to SQLite database manipulation and querying.<br/>
/// You should typically not need to interact with this class directly, as it is used internally and
/// higher-level abstractions are available for common operations.
/// </summary>
public sealed class NoireDatabase : IDisposable
{

    #region Private Porperties and Constructor

    private sealed record CacheEntry(object? Data, DateTime ExpiresAt);

    private static readonly Dictionary<string, NoireDatabase> Instances = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> DatabaseDirectoryOverrides = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> DatabasesToInitialize = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object InstanceLock = new();
    private static bool IsInitialized;

    private readonly SqliteConnection _connection;
    private SqliteTransaction? _transaction;
    private readonly List<DatabaseQueryLog> _queries = new();
    private readonly Dictionary<string, CacheEntry> _cacheResults = new(StringComparer.Ordinal);
    private bool _logQueries = false;
    private int _transactionLevel;

    static NoireDatabase()
    {
        NoireLibMain.RegisterOnDispose("NoireLib.Database", DisposeAll);
    }

    private NoireDatabase(string databaseName)
    {
        DatabaseName = databaseName;

        var filePath = GetDatabaseFilePath(databaseName);
        if (string.IsNullOrWhiteSpace(filePath))
            throw new InvalidOperationException("Database path could not be resolved.");

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = Math.Max(1, (int)BusyTimeout.TotalSeconds)
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        ApplyConcurrencySettings();
        DatabaseMigrationExecutor.ExecuteMigrations(this);
    }

    #endregion



    #region Public Properties and Methods

    /// <summary>
    /// Gets the database name.
    /// </summary>
    public string DatabaseName { get; }

    /// <summary>
    /// Gets or sets the busy timeout used for concurrent access handling.
    /// </summary>
    public static TimeSpan BusyTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets a value indicating whether write-ahead logging is enabled.
    /// </summary>
    public static bool UseWriteAheadLogging { get; set; } = true;

    /// <summary>
    /// Gets a shared instance of a database connection for the provided name.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <returns>A shared <see cref="NoireDatabase"/> instance.</returns>
    public static NoireDatabase GetInstance(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Database name cannot be null or empty.", nameof(databaseName));

        lock (InstanceLock)
        {
            if (!Instances.TryGetValue(databaseName, out var instance))
            {
                instance = new NoireDatabase(databaseName);
                Instances[databaseName] = instance;
                NoireLogger.LogDebug($"Created new database instance for: {databaseName}", $"[{nameof(NoireDatabase)}] ");
            }

            return instance;
        }
    }

    /// <summary>
    /// Registers a database to be loaded during plugin initialization.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="loadOnInitialize">Whether to load the database at initialization.</param>
    public static void RegisterForInitialization(string databaseName, bool loadOnInitialize = true)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Database name cannot be null or empty.", nameof(databaseName));

        if (!loadOnInitialize)
            return;

        var loadNow = false;
        lock (InstanceLock)
        {
            if (IsInitialized)
                loadNow = true;
            else
                DatabasesToInitialize.Add(databaseName);
        }

        if (loadNow)
            GetInstance(databaseName);
    }

    internal static void InitializeRegisteredDatabases()
    {
        List<string> databasesToLoad;
        lock (InstanceLock)
        {
            IsInitialized = true;
            databasesToLoad = DatabasesToInitialize.ToList();
            DatabasesToInitialize.Clear();
        }

        foreach (var databaseName in databasesToLoad)
        {
            NoireLogger.LogDebug($"Initializing registered database: {databaseName}", $"[{nameof(NoireDatabase)}] ");
            GetInstance(databaseName);
        }
    }

    /// <summary>
    /// Disposes all database instances and clears cached instances.
    /// </summary>
    public static void DisposeAll()
    {
        lock (InstanceLock)
        {
            foreach (var instance in Instances.Values)
                instance.Dispose();

            Instances.Clear();
        }

        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// Overrides the database directory for a database name.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="directoryPath">The database directory path.</param>
    internal static void SetDatabaseDirectoryOverride(string databaseName, string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Database name cannot be null or empty.", nameof(databaseName));

        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("Database directory path cannot be null or empty.", nameof(directoryPath));

        var fullPath = Path.GetFullPath(directoryPath);

        lock (InstanceLock)
        {
            DatabaseDirectoryOverrides[databaseName] = fullPath;
        }
    }

    /// <summary>
    /// Removes the directory override for a database name.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <returns>True if an override was removed; otherwise, false.</returns>
    public static bool RemoveDatabaseDirectoryOverride(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Database name cannot be null or empty.", nameof(databaseName));

        lock (InstanceLock)
        {
            return DatabaseDirectoryOverrides.Remove(databaseName);
        }
    }

    /// <summary>
    /// Clears all configured database directory overrides.
    /// </summary>
    public static void ClearDatabaseDirectoryOverrides()
    {
        lock (InstanceLock)
        {
            DatabaseDirectoryOverrides.Clear();
        }
    }

    /// <summary>
    /// Resolves the database file path for the provided name.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <returns>The resolved file path, or null if it cannot be determined.</returns>
    public static string? GetDatabaseFilePath(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            return null;

        string? overridePath = null;
        lock (InstanceLock)
        {
            if (DatabaseDirectoryOverrides.TryGetValue(databaseName, out var path))
                overridePath = path;
        }

        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var fullPath = Path.GetFullPath(overridePath);
            if (!FileHelper.EnsureDirectoryExists(fullPath))
                return null;

            return Path.Combine(fullPath, $"{databaseName}.db");
        }

        var configDirectory = FileHelper.GetPluginConfigDirectory();
        if (string.IsNullOrWhiteSpace(configDirectory))
            return null;

        var databaseDirectory = Path.Combine(configDirectory, "Databases");
        if (!FileHelper.EnsureDirectoryExists(databaseDirectory))
            return null;

        return Path.Combine(databaseDirectory, $"{databaseName}.db");
    }

    /// <summary>
    /// Gets the underlying SQLite connection.
    /// </summary>
    /// <returns>The active <see cref="SqliteConnection"/>.</returns>
    public SqliteConnection GetConnection() => _connection;

    /// <summary>
    /// Gets the current database schema version.
    /// </summary>
    /// <returns>The schema version number.</returns>
    public int GetSchemaVersion()
    {
        var result = FetchScalar("PRAGMA user_version");
        return result == null ? 0 : Convert.ToInt32(result);
    }

    /// <summary>
    /// Sets the database schema version.
    /// </summary>
    /// <param name="version">The schema version number.</param>
    public void SetSchemaVersion(int version)
    {
        var normalizedVersion = Math.Max(0, version);
        Execute($"PRAGMA user_version = {normalizedVersion}");
    }

    /// <summary>
    /// Gets the number of logged queries.
    /// </summary>
    /// <returns>The count of logged queries.</returns>
    public int GetQueryCount() => _queries.Count;

    /// <summary>
    /// Gets the logged queries.
    /// </summary>
    /// <returns>A read-only list of logged queries.</returns>
    public IReadOnlyList<DatabaseQueryLog> GetQueries() => _queries.AsReadOnly();

    /// <summary>
    /// Enables or disables query logging.
    /// </summary>
    /// <param name="logQueries">Whether to log executed queries.</param>
    /// <returns>The current <see cref="NoireDatabase"/> instance for chaining.</returns>
    public NoireDatabase SetLogQueries(bool logQueries)
    {
        _logQueries = logQueries;
        return this;
    }

    /// <summary>
    /// Clears cached query results.
    /// </summary>
    /// <param name="key">An optional cache key to clear. If null, will clear all cache keys.</param>
    public void ClearCache(string? key = null)
    {
        if (key == null)
        {
            _cacheResults.Clear();
            return;
        }

        _cacheResults.Remove(key);
    }

    /// <summary>
    /// Caches the result of the provided callback for the given key.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="callback">The factory callback.</param>
    /// <param name="ttl">The cache time-to-live.</param>
    /// <returns>The cached or newly generated result.</returns>
    public T Cache<T>(string key, Func<NoireDatabase, T> callback, TimeSpan? ttl = null)
    {
        var effectiveTtl = ttl ?? TimeSpan.FromMinutes(5);

        if (_cacheResults.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
            return (T)entry.Data!;

        var data = callback(this);
        _cacheResults[key] = new CacheEntry(data, DateTime.UtcNow.Add(effectiveTtl));
        return data;
    }

    /// <summary>
    /// Executes a SQL statement and returns the affected row count.
    /// </summary>
    /// <param name="sql">The SQL statement.</param>
    /// <param name="parameters">The parameter values.</param>
    /// <returns>The number of rows affected by the execution.</returns>
    public int Execute(string sql, IReadOnlyList<object?>? parameters = null)
    {
        using var command = CreateCommand(sql, parameters);
        return ExecuteNonQuery(command, sql, parameters);
    }

    /// <summary>
    /// Executes a SQL query and returns the first row, if any.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameter values.</param>
    /// <returns>A dictionary representing the first row, or null if no rows were returned.</returns>
    public Dictionary<string, object?>? Fetch(string sql, IReadOnlyList<object?>? parameters = null)
    {
        using var command = CreateCommand(sql, parameters);
        using var reader = ExecuteReader(command, sql, parameters);
        if (!reader.Read())
            return null;

        return ReadRow(reader);
    }

    /// <summary>
    /// Executes a SQL query and returns all rows.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameter values.</param>
    /// <returns>A list of dictionaries representing the returned rows.</returns>
    public List<Dictionary<string, object?>> FetchAll(string sql, IReadOnlyList<object?>? parameters = null)
    {
        using var command = CreateCommand(sql, parameters);
        using var reader = ExecuteReader(command, sql, parameters);
        var results = new List<Dictionary<string, object?>>();

        while (reader.Read())
            results.Add(ReadRow(reader));

        return results;
    }

    /// <summary>
    /// Executes a SQL query and returns the first column of the first row.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameter values.</param>
    /// <returns>The value of the first column in the first row, or null if no rows were returned.</returns>
    public object? FetchScalar(string sql, IReadOnlyList<object?>? parameters = null)
    {
        using var command = CreateCommand(sql, parameters);
        var stopwatch = Stopwatch.StartNew();
        var result = command.ExecuteScalar();
        stopwatch.Stop();

        LogQuery(sql, parameters, stopwatch.Elapsed.TotalSeconds);

        return NormalizeValue(result);
    }

    /// <summary>
    /// Inserts a new row into the specified table.
    /// </summary>
    /// <param name="table">The table name.</param>
    /// <param name="data">The data to insert.</param>
    /// <returns>The ID of the inserted row, or 0 if the insert failed.</returns>
    public long Insert(string table, IReadOnlyDictionary<string, object?> data)
    {
        var columns = data.Keys.Select(EscapeColumn).ToArray();
        var placeholders = data.Keys.Select((key, index) => $"@p{index}").ToArray();
        var sql = $"INSERT INTO {EscapeColumn(table)} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", placeholders)})";

        using var command = CreateCommand(sql, data.Values.ToList());
        var rows = ExecuteNonQuery(command, sql, data.Values.ToList());
        if (rows <= 0)
            return 0;

        var id = FetchScalar("SELECT last_insert_rowid()");
        return id == null ? 0 : Convert.ToInt64(id);
    }

    /// <summary>
    /// Updates rows in the specified table.
    /// </summary>
    /// <param name="table">The table name.</param>
    /// <param name="data">The data to update.</param>
    /// <param name="where">The filter criteria.</param>
    /// <returns>The number of rows affected by the update.</returns>
    public int Update(string table, IReadOnlyDictionary<string, object?> data, IReadOnlyDictionary<string, object?> where)
    {
        var setClauses = data.Keys.Select((key, index) => $"{EscapeColumn(key)} = @p{index}").ToArray();
        var whereOffset = data.Count;
        var whereClauses = where.Keys.Select((key, index) => $"{EscapeColumn(key)} = @p{index + whereOffset}").ToArray();
        var sql = $"UPDATE {EscapeColumn(table)} SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)}";

        var parameters = new List<object?>();
        parameters.AddRange(data.Values);
        parameters.AddRange(where.Values);

        using var command = CreateCommand(sql, parameters);
        return ExecuteNonQuery(command, sql, parameters);
    }

    /// <summary>
    /// Deletes rows from the specified table.
    /// </summary>
    /// <param name="table">The table name.</param>
    /// <param name="where">The filter criteria.</param>
    /// <returns>The number of rows affected by the delete.</returns>
    public int Delete(string table, IReadOnlyDictionary<string, object?> where)
    {
        var whereClauses = where.Keys.Select((key, index) => $"{EscapeColumn(key)} = @p{index}").ToArray();
        var sql = $"DELETE FROM {EscapeColumn(table)} WHERE {string.Join(" AND ", whereClauses)}";

        using var command = CreateCommand(sql, where.Values.ToList());
        return ExecuteNonQuery(command, sql, where.Values.ToList());
    }

    /// <summary>
    /// Counts rows in the specified table.
    /// </summary>
    /// <param name="table">The table name.</param>
    /// <param name="where">Optional filter criteria.</param>
    /// <param name="column">The column to count.</param>
    /// <returns>The count of matching rows.</returns>
    public int Count(string table, IReadOnlyDictionary<string, object?>? where = null, string column = "*")
    {
        var sql = $"SELECT COUNT({column}) FROM {EscapeColumn(table)}";
        var parameters = new List<object?>();

        if (where != null && where.Count > 0)
        {
            var whereClauses = where.Keys.Select((key, index) => $"{EscapeColumn(key)} = @p{index}").ToArray();
            sql += $" WHERE {string.Join(" AND ", whereClauses)}";
            parameters.AddRange(where.Values);
        }

        var result = FetchScalar(sql, parameters);
        return result is null ? 0 : Convert.ToInt32(result);
    }

    /// <summary>
    /// Determines whether any row matches the criteria.
    /// </summary>
    /// <param name="table">The table name.</param>
    /// <param name="where">The filter criteria.</param>
    /// <returns>True if at least one matching row exists; otherwise, false.</returns>
    public bool Exists(string table, IReadOnlyDictionary<string, object?> where)
    {
        return Count(table, where) > 0;
    }

    /// <summary>
    /// Begins a transaction or creates a savepoint.
    /// </summary>
    /// <returns>True if a new transaction was started or a savepoint was created; otherwise, false.</returns>
    public bool BeginTransaction()
    {
        if (_transactionLevel == 0)
        {
            _transaction ??= _connection.BeginTransaction();

            _transactionLevel = 1;
            return true;
        }

        var savepointName = GetSavepointName();
        Execute($"SAVEPOINT {savepointName}");
        _transactionLevel++;
        return true;
    }

    /// <summary>
    /// Commits the current transaction or savepoint.
    /// </summary>
    /// <returns>True if the transaction or savepoint was successfully committed; otherwise, false.</returns>
    public bool Commit()
    {
        if (_transactionLevel <= 0)
        {
            _transactionLevel = 0;
            _transaction?.Commit();
            _transaction?.Dispose();
            _transaction = null;
            return true;
        }

        if (_transactionLevel == 1)
        {
            _transaction?.Commit();
            _transaction?.Dispose();
            _transaction = null;
            _transactionLevel = 0;
            return true;
        }

        var savepointName = GetSavepointName();
        Execute($"RELEASE SAVEPOINT {savepointName}");
        _transactionLevel--;
        return true;
    }

    /// <summary>
    /// Rolls back all nested transactions and the root transaction, if any.
    /// </summary>
    /// <returns>True if the rollback completed; otherwise, false.</returns>
    public bool RollbackAll()
    {
        var rolledBack = false;

        while (_transactionLevel > 0 || _transaction != null)
        {
            rolledBack = Rollback();

            if (!rolledBack)
                break;
        }

        return rolledBack;
    }

    /// <summary>
    /// Rolls back the current transaction or savepoint.
    /// </summary>
    /// <returns>True if the transaction or savepoint was successfully rolled back; otherwise, false.</returns>
    public bool Rollback()
    {
        if (_transactionLevel <= 0)
        {
            _transactionLevel = 0;
            _transaction?.Rollback();
            _transaction?.Dispose();
            _transaction = null;
            return true;
        }

        if (_transactionLevel == 1)
        {
            _transaction?.Rollback();
            _transaction?.Dispose();
            _transaction = null;
            _transactionLevel = 0;
            return true;
        }

        var savepointName = GetSavepointName();
        Execute($"ROLLBACK TO SAVEPOINT {savepointName}");
        _transactionLevel--;
        return true;
    }

    /// <summary>
    /// Returns whether a transaction is currently active.
    /// </summary>
    /// <returns>True if a transaction is active; otherwise, false.</returns>
    public bool InTransaction() => _transaction != null;

    /// <summary>
    /// Gets the current transaction nesting level.
    /// </summary>
    /// <returns>The current transaction level, where 0 means no active transaction.</returns>
    public int GetTransactionLevel() => _transactionLevel;

    /// <summary>
    /// Disposes the database.
    /// </summary>
    public void Dispose()
    {
        NoireLogger.LogDebug(this, $"Disposing database instance: {DatabaseName}");
        try
        {
            _transaction?.Dispose();
            _transaction = null;
        }
        catch
        {
            _transaction = null;
        }

        try
        {
            _connection.Close();
            SqliteConnection.ClearPool(_connection);
        }
        catch
        {
        }

        _connection.Dispose();
    }

    /// <summary>
    /// Escapes a column or table identifier for SQLite.
    /// </summary>
    /// <param name="column">The identifier to escape.</param>
    /// <returns>The escaped identifier.</returns>
    public static string EscapeColumn(string column)
    {
        if (column.Contains('(') || column.Contains(')'))
            return column;

        if (column.Contains('.'))
        {
            var parts = column.Split('.');
            return string.Join('.', parts.Select(part => $"\"{part}\""));
        }

        return $"\"{column}\"";
    }

    #endregion



    #region Private Methods

    private SqliteCommand CreateCommand(string sql, IReadOnlyList<object?>? parameters)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;

        if (parameters != null)
        {
            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = $"@p{i}";
                parameter.Value = parameters[i] ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
        }

        return command;
    }

    private static Dictionary<string, object?> ReadRow(SqliteDataReader reader)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var value = NormalizeValue(reader.GetValue(i));
            row[reader.GetName(i)] = value;
        }

        return row;
    }

    private SqliteDataReader ExecuteReader(SqliteCommand command, string sql, IReadOnlyList<object?>? parameters)
    {
        var stopwatch = Stopwatch.StartNew();
        var reader = command.ExecuteReader();
        stopwatch.Stop();

        LogQuery(sql, parameters, stopwatch.Elapsed.TotalSeconds);
        return reader;
    }

    private int ExecuteNonQuery(SqliteCommand command, string sql, IReadOnlyList<object?>? parameters)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = command.ExecuteNonQuery();
        stopwatch.Stop();

        LogQuery(sql, parameters, stopwatch.Elapsed.TotalSeconds);
        return result;
    }

    private void LogQuery(string sql, IReadOnlyList<object?>? parameters, double executionTime)
    {
        if (!_logQueries)
            return;

        _queries.Add(new DatabaseQueryLog(sql, parameters ?? Array.Empty<object?>(), executionTime));
    }

    private static object? NormalizeValue(object? value)
    {
        return value is DBNull ? null : value;
    }

    private string GetSavepointName() => $"SAVEPOINT_LEVEL_{_transactionLevel}";

    private void ApplyConcurrencySettings()
    {
        var busyTimeoutMs = Math.Max(0, (int)BusyTimeout.TotalMilliseconds);
        var journalMode = UseWriteAheadLogging ? "WAL" : "DELETE";

        using var command = _connection.CreateCommand();
        command.CommandText = $"PRAGMA journal_mode={journalMode}; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout={busyTimeoutMs};";
        command.ExecuteNonQuery();
    }

    #endregion
}

/// <summary>
/// Captures a logged database query.
/// </summary>
/// <param name="Sql">The executed SQL statement.</param>
/// <param name="Parameters">The parameter values.</param>
/// <param name="ExecutionTime">The execution time in seconds.</param>
public sealed record DatabaseQueryLog(string Sql, IReadOnlyList<object?> Parameters, double ExecutionTime);
