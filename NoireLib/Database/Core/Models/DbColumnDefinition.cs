namespace NoireLib.Database;

/// <summary>
/// Defines a column used when creating database tables.
/// </summary>
/// <param name="Name">The column name.</param>
/// <param name="Type">The SQLite column type.</param>
public sealed record DbColumnDefinition(string Name, string Type)
{
    /// <summary>
    /// Gets a value indicating whether the column is a primary key.
    /// </summary>
    public bool IsPrimaryKey { get; init; }
    /// <summary>
    /// Gets a value indicating whether the column allows null values.
    /// </summary>
    public bool IsNullable { get; init; } = true;
    /// <summary>
    /// Gets a value indicating whether the column is auto-incrementing.
    /// </summary>
    public bool IsAutoIncrement { get; init; }
    /// <summary>
    /// Gets the default value for the column.
    /// </summary>
    public object? DefaultValue { get; init; }
}
