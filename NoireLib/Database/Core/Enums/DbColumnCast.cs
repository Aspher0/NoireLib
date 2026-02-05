namespace NoireLib.Database;

/// <summary>
/// Represents a type to cast a column to in a database query.
/// </summary>
public enum DbColumnCast
{
    /// <summary>
    /// Casts the column to an integer type.
    /// </summary>
    Integer,
    /// <summary>
    /// Casts the column to a floating-point type.
    /// </summary>
    Float,
    /// <summary>
    /// Casts the column to a string type.
    /// </summary>
    String,
    /// <summary>
    /// Casts the column to a boolean type.
    /// </summary>
    Boolean,
    /// <summary>
    /// Casts the column to an array type.
    /// </summary>
    Array,
    /// <summary>
    /// Casts the column to a JSON type.
    /// </summary>
    Json,
    /// <summary>
    /// Casts the column to a DateTime type.
    /// </summary>
    DateTime,
    /// <summary>
    /// Casts the column to a timestamp type.
    /// </summary>
    Timestamp
}
