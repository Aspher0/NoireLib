using NoireLib.Database.Migrations;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace NoireLib.Database;

/// <summary>
/// SQLite access and querying, below the higher-level abstractions. Calls on one instance are serialized, and a
/// transaction begins and ends on one thread.
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

    private readonly SQLiteConnection _connection;
    private readonly object _sync = new();
    private SQLiteTransaction? _transaction;
    private int _transactionOwnerThreadId;
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

        var connectionString = new SQLiteConnectionStringBuilder
        {
            DataSource = filePath,
            Pooling = true,
            DefaultTimeout = Math.Max(1, (int)BusyTimeout.TotalSeconds)
        }.ToString();

        _connection = new SQLiteConnection(connectionString);
        _connection.Open();
        ApplyConcurrencySettings();
        DatabaseMigrationExecutor.ExecuteMigrations(this);
    }

    #endregion



    #region Public Properties and Methods

    /// <summary>Gets the database name.</summary>
    public string DatabaseName { get; }

    /// <summary>Gets or sets the busy timeout used for concurrent access handling.</summary>
    public static TimeSpan BusyTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets a value indicating whether write-ahead logging is enabled.</summary>
    public static bool UseWriteAheadLogging { get; set; } = true;

    /// <summary>Gets a shared instance of a database connection for the provided name.</summary>
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

    /// <summary>Registers a database to be loaded during plugin initialization.</summary>
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

    /// <summary>Disposes all database instances and clears cached instances.</summary>
    public static void DisposeAll()
    {
        lock (InstanceLock)
        {
            foreach (var instance in Instances.Values)
                instance.Dispose();

            Instances.Clear();
        }

        SQLiteConnection.ClearAllPools();
    }

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

    /// <summary>Removes the directory override for a database name.</summary>
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

    /// <summary>Clears all configured database directory overrides.</summary>
    public static void ClearDatabaseDirectoryOverrides()
    {
        lock (InstanceLock)
        {
            DatabaseDirectoryOverrides.Clear();
        }
    }

    /// <summary>Resolves the database file path for the provided name.</summary>
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
    /// Gets the underlying SQLite connection; commands run on it directly are not serialized with this instance's calls.
    /// </summary>
    /// <returns>The active <see cref="SQLiteConnection"/>.</returns>
    public SQLiteConnection GetConnection() => _connection;

    /// <summary>Gets the current database schema version.</summary>
    /// <returns>The schema version number.</returns>
    public int GetSchemaVersion()
    {
        var result = FetchScalar("PRAGMA user_version");
        return result == null ? 0 : Convert.ToInt32(result);
    }

    /// <summary>Sets the database schema version.</summary>
    /// <param name="version">The schema version number.</param>
    public void SetSchemaVersion(int version)
    {
        var normalizedVersion = Math.Max(0, version);
        Execute($"PRAGMA user_version = {normalizedVersion}");
    }

    /// <summary>Gets the number of logged queries.</summary>
    /// <returns>The count of logged queries.</returns>
    public int GetQueryCount()
    {
        lock (_sync)
            return _queries.Count;
    }

    /// <summary>Gets the logged queries.</summary>
    /// <returns>A snapshot of the logged queries.</returns>
    public IReadOnlyList<DatabaseQueryLog> GetQueries()
    {
        lock (_sync)
            return _queries.ToArray();
    }

    /// <summary>Enables or disables query logging.</summary>
    /// <param name="logQueries">Whether to log executed queries.</param>
    /// <returns>The current <see cref="NoireDatabase"/> instance for chaining.</returns>
    public NoireDatabase SetLogQueries(bool logQueries)
    {
        lock (_sync)
            _logQueries = logQueries;

        return this;
    }

    /// <summary>Clears cached query results.</summary>
    /// <param name="key">An optional cache key to clear. If null, will clear all cache keys.</param>
    public void ClearCache(string? key = null)
    {
        lock (_sync)
        {
            if (key == null)
            {
                _cacheResults.Clear();
                return;
            }

            _cacheResults.Remove(key);
        }
    }

    /// <summary>Caches the result of the provided callback for the given key.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="callback">The factory callback.</param>
    /// <param name="ttl">The cache time-to-live.</param>
    /// <returns>The cached or newly generated result.</returns>
    public T Cache<T>(string key, Func<NoireDatabase, T> callback, TimeSpan? ttl = null)
    {
        var effectiveTtl = ttl ?? TimeSpan.FromMinutes(5);

        lock (_sync)
        {
            if (_cacheResults.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
                return (T)entry.Data!;

            var data = callback(this);
            _cacheResults[key] = new CacheEntry(data, DateTime.UtcNow.Add(effectiveTtl));
            return data;
        }
    }

    /// <summary>Executes a SQL statement and returns the affected row count.</summary>
    /// <param name="sql">The SQL statement.</param>
    /// <param name="parameters">The parameter values.</param>
    /// <returns>The number of rows affected by the execution.</returns>
    public int Execute(string sql, IReadOnlyList<object?>? parameters = null)
    {
        lock (_sync)
        {
            using var command = CreateCommand(sql, parameters);
            return ExecuteNonQuery(command, sql, parameters);
        }
    }

    /// <summary>Executes a SQL query and returns the first row, if any.</summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameter values.</param>
    /// <returns>A dictionary representing the first row, or null if no rows were returned.</returns>
    public Dictionary<string, object?>? Fetch(string sql, IReadOnlyList<object?>? parameters = null)
    {
        lock (_sync)
        {
            using var command = CreateCommand(sql, parameters);
            using var reader = ExecuteReader(command, sql, parameters);
            if (!reader.Read())
                return null;

            return ReadRow(reader);
        }
    }

    /// <summary>Executes a SQL query and returns all rows.</summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameter values.</param>
    /// <returns>A list of dictionaries representing the returned rows.</returns>
    public List<Dictionary<string, object?>> FetchAll(string sql, IReadOnlyList<object?>? parameters = null)
    {
        lock (_sync)
        {
            using var command = CreateCommand(sql, parameters);
            using var reader = ExecuteReader(command, sql, parameters);
            var results = new List<Dictionary<string, object?>>();

            while (reader.Read())
                results.Add(ReadRow(reader));

            return results;
        }
    }

    /// <summary>Executes a SQL query and returns the first column of the first row.</summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameter values.</param>
    /// <returns>The value of the first column in the first row, or null if no rows were returned.</returns>
    public object? FetchScalar(string sql, IReadOnlyList<object?>? parameters = null)
    {
        lock (_sync)
        {
            using var command = CreateCommand(sql, parameters);
            var stopwatch = Stopwatch.StartNew();
            var result = command.ExecuteScalar();
            stopwatch.Stop();

            LogQuery(sql, parameters, stopwatch.Elapsed.TotalSeconds);

            return NormalizeValue(result);
        }
    }

    /// <summary>Inserts a new row into the specified table.</summary>
    /// <param name="table">The table name.</param>
    /// <param name="data">The data to insert.</param>
    /// <returns>The ID of the inserted row, or 0 if the insert failed.</returns>
    public long Insert(string table, IReadOnlyDictionary<string, object?> data)
    {
        var columns = data.Keys.Select(EscapeColumn).ToArray();
        var placeholders = data.Keys.Select((key, index) => $"@p{index}").ToArray();
        var sql = $"INSERT INTO {EscapeColumn(table)} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", placeholders)})";

        // Held across both statements: last_insert_rowid is per connection, and another thread's insert would be read.
        lock (_sync)
        {
            using var command = CreateCommand(sql, data.Values.ToList());
            var rows = ExecuteNonQuery(command, sql, data.Values.ToList());
            if (rows <= 0)
                return 0;

            var id = FetchScalar("SELECT last_insert_rowid()");
            return id == null ? 0 : Convert.ToInt64(id);
        }
    }

    /// <summary>Updates rows in the specified table.</summary>
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

        lock (_sync)
        {
            using var command = CreateCommand(sql, parameters);
            return ExecuteNonQuery(command, sql, parameters);
        }
    }

    /// <summary>Deletes rows from the specified table.</summary>
    /// <param name="table">The table name.</param>
    /// <param name="where">The filter criteria.</param>
    /// <returns>The number of rows affected by the delete.</returns>
    public int Delete(string table, IReadOnlyDictionary<string, object?> where)
    {
        var whereClauses = where.Keys.Select((key, index) => $"{EscapeColumn(key)} = @p{index}").ToArray();
        var sql = $"DELETE FROM {EscapeColumn(table)} WHERE {string.Join(" AND ", whereClauses)}";

        lock (_sync)
        {
            using var command = CreateCommand(sql, where.Values.ToList());
            return ExecuteNonQuery(command, sql, where.Values.ToList());
        }
    }

    /// <summary>Counts rows in the specified table.</summary>
    /// <param name="table">The table name.</param>
    /// <param name="where">Optional filter criteria.</param>
    /// <param name="column">The column to count, or <c>*</c> for every row.</param>
    /// <returns>The count of matching rows.</returns>
    public int Count(string table, IReadOnlyDictionary<string, object?>? where = null, string column = "*")
    {
        var sql = $"SELECT COUNT({EscapeAggregateColumn(column)}) FROM {EscapeColumn(table)}";
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

    /// <summary>Determines whether any row matches the criteria.</summary>
    /// <param name="table">The table name.</param>
    /// <param name="where">The filter criteria.</param>
    /// <returns>True if at least one matching row exists; otherwise, false.</returns>
    public bool Exists(string table, IReadOnlyDictionary<string, object?> where)
    {
        return Count(table, where) > 0;
    }

    /// <summary>
    /// Begins a transaction or creates a savepoint; the calling thread owns the transaction until its outermost commit or rollback.
    /// </summary>
    /// <returns>True if a new transaction was started or a savepoint was created; otherwise, false.</returns>
    /// <exception cref="InvalidOperationException">Thrown when another thread owns the open transaction.</exception>
    public bool BeginTransaction()
    {
        lock (_sync)
        {
            ThrowIfTransactionOwnedElsewhere();

            if (_transactionLevel == 0)
            {
                _transaction ??= _connection.BeginTransaction();

                _transactionLevel = 1;
                _transactionOwnerThreadId = Environment.CurrentManagedThreadId;
                return true;
            }

            Execute($"SAVEPOINT {GetSavepointName(_transactionLevel + 1)}");
            _transactionLevel++;
            return true;
        }
    }

    /// <summary>Commits the current transaction or savepoint.</summary>
    /// <returns>True if the transaction or savepoint was successfully committed; otherwise, false.</returns>
    /// <exception cref="InvalidOperationException">Thrown when another thread owns the open transaction.</exception>
    public bool Commit()
    {
        lock (_sync)
        {
            ThrowIfTransactionOwnedElsewhere();

            if (_transactionLevel <= 1)
            {
                _transaction?.Commit();
                EndTransaction();
                return true;
            }

            Execute($"RELEASE SAVEPOINT {GetSavepointName(_transactionLevel)}");
            _transactionLevel--;
            return true;
        }
    }

    /// <summary>Rolls back all nested transactions and the root transaction, if any.</summary>
    /// <returns>True if the rollback completed; otherwise, false.</returns>
    /// <exception cref="InvalidOperationException">Thrown when another thread owns the open transaction.</exception>
    public bool RollbackAll()
    {
        lock (_sync)
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
    }

    /// <summary>Rolls back the current transaction or savepoint.</summary>
    /// <returns>True if the transaction or savepoint was successfully rolled back; otherwise, false.</returns>
    /// <exception cref="InvalidOperationException">Thrown when another thread owns the open transaction.</exception>
    public bool Rollback()
    {
        lock (_sync)
        {
            ThrowIfTransactionOwnedElsewhere();

            if (_transactionLevel <= 1)
            {
                _transaction?.Rollback();
                EndTransaction();
                return true;
            }

            // ROLLBACK TO keeps the savepoint open: it is released too, leaving the enclosing level current.
            var savepointName = GetSavepointName(_transactionLevel);
            Execute($"ROLLBACK TO SAVEPOINT {savepointName}");
            Execute($"RELEASE SAVEPOINT {savepointName}");
            _transactionLevel--;
            return true;
        }
    }

    /// <summary>Returns whether a transaction is currently active.</summary>
    /// <returns>True if a transaction is active; otherwise, false.</returns>
    public bool InTransaction()
    {
        lock (_sync)
            return _transaction != null;
    }

    /// <summary>Gets the current transaction nesting level.</summary>
    /// <returns>The current transaction level, where 0 means no active transaction.</returns>
    public int GetTransactionLevel()
    {
        lock (_sync)
            return _transactionLevel;
    }

    /// <summary>Disposes the database.</summary>
    public void Dispose()
    {
        NoireLogger.LogDebug(this, $"Disposing database instance: {DatabaseName}");

        lock (_sync)
        {
            try
            {
                _transaction?.Dispose();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(this, ex, $"Failed to dispose the open transaction of database: {DatabaseName}");
            }

            _transaction = null;
            _transactionLevel = 0;
            _transactionOwnerThreadId = 0;

            try
            {
                _connection.Close();
                SQLiteConnection.ClearPool(_connection);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(this, ex, $"Failed to close database: {DatabaseName}");
            }

            _connection.Dispose();
        }
    }

    /// <summary>
    /// Escapes a column or table identifier for SQLite, quoting each dot-separated part; the input is never read as an SQL expression.
    /// </summary>
    /// <param name="column">The identifier to escape, optionally qualified as <c>table.column</c>.</param>
    /// <returns>The escaped identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="column"/> is null or empty.</exception>
    public static string EscapeColumn(string column)
    {
        ArgumentException.ThrowIfNullOrEmpty(column);

        if (!column.Contains('.'))
            return QuoteIdentifier(column);

        var parts = column.Split('.');
        for (var i = 0; i < parts.Length; i++)
            parts[i] = QuoteIdentifier(parts[i]);

        return string.Join('.', parts);
    }

    // The argument of COUNT, AVG, SUM, MIN and MAX: an escaped identifier, or the bare wildcard.
    internal static string EscapeAggregateColumn(string column) => column == "*" ? column : EscapeColumn(column);

    #endregion



    #region Private Methods

    private SQLiteCommand CreateCommand(string sql, IReadOnlyList<object?>? parameters)
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

    private static Dictionary<string, object?> ReadRow(SQLiteDataReader reader)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var value = NormalizeValue(reader.GetValue(i));
            row[reader.GetName(i)] = value;
        }

        return row;
    }

    private SQLiteDataReader ExecuteReader(SQLiteCommand command, string sql, IReadOnlyList<object?>? parameters)
    {
        var stopwatch = Stopwatch.StartNew();
        var reader = command.ExecuteReader();
        stopwatch.Stop();

        LogQuery(sql, parameters, stopwatch.Elapsed.TotalSeconds);
        return reader;
    }

    private int ExecuteNonQuery(SQLiteCommand command, string sql, IReadOnlyList<object?>? parameters)
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

    private static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    private static string GetSavepointName(int level) => $"SAVEPOINT_LEVEL_{level}";

    // The transaction belongs to the connection: another thread's call would nest into or end the owner's work.
    private void ThrowIfTransactionOwnedElsewhere()
    {
        if (_transactionLevel > 0 && _transactionOwnerThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException($"A transaction on database '{DatabaseName}' is owned by another thread. A transaction must begin and end on the same thread.");
    }

    private void EndTransaction()
    {
        _transaction?.Dispose();
        _transaction = null;
        _transactionLevel = 0;
        _transactionOwnerThreadId = 0;
    }

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

/// <summary>Captures a logged database query.</summary>
/// <param name="Sql">The executed SQL statement.</param>
/// <param name="Parameters">The parameter values.</param>
/// <param name="ExecutionTime">The execution time in seconds.</param>
public sealed record DatabaseQueryLog(string Sql, IReadOnlyList<object?> Parameters, double ExecutionTime);
