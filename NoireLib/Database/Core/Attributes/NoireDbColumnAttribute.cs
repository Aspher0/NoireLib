using System;

namespace NoireLib.Database;

/// <summary>
/// Defines database column metadata for a model property.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NoireDbColumnAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireDbColumnAttribute"/> class.
    /// </summary>
    /// <param name="name">The database column name.</param>
    public NoireDbColumnAttribute(string? name = null)
    {
        Name = name;
    }

    /// <summary>
    /// Gets the column name.
    /// </summary>
    public string? Name { get; }
    /// <summary>
    /// Gets or sets the SQLite column type.
    /// </summary>
    public string? Type { get; set; }
    /// <summary>
    /// Gets or sets a value indicating whether the column is a primary key.
    /// </summary>
    public bool IsPrimaryKey { get; set; }
    /// <summary>
    /// Gets or sets a value indicating whether the column allows null values.
    /// </summary>
    public bool IsNullable { get; set; } = true;
    /// <summary>
    /// Gets or sets a value indicating whether the column is auto-incrementing.
    /// </summary>
    public bool IsAutoIncrement { get; set; }
    /// <summary>
    /// Gets or sets the default value for the column.
    /// </summary>
    public object? DefaultValue { get; set; }
}
