using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Database.Migrations;

/// <summary>
/// Fluent helper for building database migrations with common schema operations.
/// </summary>
public sealed class DatabaseMigrationBuilder
{
    private readonly NoireDatabase _database;
    private readonly List<string> _statements = new();

    private DatabaseMigrationBuilder(NoireDatabase database)
    {
        _database = database;
    }

    /// <summary>
    /// Creates a new migration builder for the provided database.
    /// </summary>
    /// <param name="database">The database instance.</param>
    /// <returns>A new <see cref="DatabaseMigrationBuilder"/>.</returns>
    public static DatabaseMigrationBuilder Create(NoireDatabase database) => new(database);

    /// <summary>
    /// Adds a column to a table.
    /// </summary>
    /// <param name="table">The table name.</param>
    /// <param name="definition">The column definition.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DatabaseMigrationBuilder AddColumn(string table, DbColumnDefinition definition)
    {
        var columnDefinition = BuildColumnDefinition(definition);
        _statements.Add($"ALTER TABLE {NoireDatabase.EscapeColumn(table)} ADD COLUMN {columnDefinition}");
        return this;
    }

    /// <summary>
    /// Adds a column to a table using a simplified definition.
    /// </summary>
    /// <param name="table">The table name.</param>
    /// <param name="columnName">The column name.</param>
    /// <param name="columnType">The SQLite column type.</param>
    /// <param name="isNullable">Whether the column allows null values.</param>
    /// <param name="defaultValue">The default value for the column.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DatabaseMigrationBuilder AddColumn(string table, string columnName, string columnType, bool isNullable = true, object? defaultValue = null)
    {
        var definition = new DbColumnDefinition(columnName, columnType)
        {
            IsNullable = isNullable,
            DefaultValue = defaultValue
        };

        return AddColumn(table, definition);
    }

    /// <summary>
    /// Creates a table with the provided column definitions.
    /// </summary>
    /// <param name="table">The table name.</param>
    /// <param name="definitions">The column definitions.</param>
    /// <param name="ifNotExists">Whether to add IF NOT EXISTS.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DatabaseMigrationBuilder CreateTable(string table, IReadOnlyCollection<DbColumnDefinition> definitions, bool ifNotExists = true)
    {
        var columns = definitions.Select(BuildColumnDefinition).ToArray();
        var existsClause = ifNotExists ? "IF NOT EXISTS " : string.Empty;
        _statements.Add($"CREATE TABLE {existsClause}{NoireDatabase.EscapeColumn(table)} ({string.Join(", ", columns)})");
        return this;
    }

    /// <summary>
    /// Renames a table.
    /// </summary>
    /// <param name="oldName">The current table name.</param>
    /// <param name="newName">The new table name.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DatabaseMigrationBuilder RenameTable(string oldName, string newName)
    {
        _statements.Add($"ALTER TABLE {NoireDatabase.EscapeColumn(oldName)} RENAME TO {NoireDatabase.EscapeColumn(newName)}");
        return this;
    }

    /// <summary>
    /// Drops a table.
    /// </summary>
    /// <param name="table">The table name.</param>
    /// <param name="ifExists">Whether to add IF EXISTS.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DatabaseMigrationBuilder DropTable(string table, bool ifExists = true)
    {
        var existsClause = ifExists ? "IF EXISTS " : string.Empty;
        _statements.Add($"DROP TABLE {existsClause}{NoireDatabase.EscapeColumn(table)}");
        return this;
    }

    /// <summary>
    /// Renames a column in a table.
    /// </summary>
    /// <param name="table">The table name.</param>
    /// <param name="oldName">The current column name.</param>
    /// <param name="newName">The new column name.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DatabaseMigrationBuilder RenameColumn(string table, string oldName, string newName)
    {
        _statements.Add($"ALTER TABLE {NoireDatabase.EscapeColumn(table)} RENAME COLUMN {NoireDatabase.EscapeColumn(oldName)} TO {NoireDatabase.EscapeColumn(newName)}");
        return this;
    }

    /// <summary>
    /// Drops a column from a table.
    /// </summary>
    /// <param name="table">The table name.</param>
    /// <param name="columnName">The column name.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DatabaseMigrationBuilder DropColumn(string table, string columnName)
    {
        _statements.Add($"ALTER TABLE {NoireDatabase.EscapeColumn(table)} DROP COLUMN {NoireDatabase.EscapeColumn(columnName)}");
        return this;
    }

    /// <summary>
    /// Adds a custom SQL statement to the migration.
    /// </summary>
    /// <param name="sql">The SQL statement to execute.</param>
    /// <returns>The builder instance for chaining.</returns>
    public DatabaseMigrationBuilder ExecuteRaw(string sql)
    {
        if (!string.IsNullOrWhiteSpace(sql))
            _statements.Add(sql);
        return this;
    }

    /// <summary>
    /// Executes all queued statements as a single migration.
    /// </summary>
    public void Apply()
    {
        if (_statements.Count == 0)
            return;

        var transactionStarted = _database.BeginTransaction();
        try
        {
            foreach (var statement in _statements)
                _database.Execute(statement);

            if (transactionStarted)
                _database.Commit();
        }
        catch
        {
            if (transactionStarted)
                _database.RollbackAll();

            throw;
        }
    }

    private static string BuildColumnDefinition(DbColumnDefinition definition)
    {
        var parts = new List<string>
        {
            NoireDatabase.EscapeColumn(definition.Name),
            definition.Type
        };

        if (definition.IsPrimaryKey)
            parts.Add("PRIMARY KEY");

        if (definition.IsAutoIncrement)
            parts.Add("AUTOINCREMENT");

        if (!definition.IsNullable)
            parts.Add("NOT NULL");

        if (definition.DefaultValue != null)
        {
            var defaultValue = definition.DefaultValue is string
                ? $"'{definition.DefaultValue.ToString()?.Replace("'", "''")}'"
                : definition.DefaultValue.ToString();
            parts.Add("DEFAULT " + defaultValue);
        }

        return string.Join(" ", parts);
    }
}
