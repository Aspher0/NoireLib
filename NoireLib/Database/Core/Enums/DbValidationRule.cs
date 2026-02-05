namespace NoireLib.Database;

/// <summary>
/// Represents available validation rules for model attributes.
/// </summary>
public enum DbValidationRule
{
    /// <summary>
    /// Requires the attribute to be set.
    /// </summary>
    Required,
    /// <summary>
    /// Requires the attribute to be numeric.
    /// </summary>
    Numeric,
    /// <summary>
    /// Requires the attribute to be an integer.
    /// </summary>
    Integer,
    /// <summary>
    /// Requires the attribute to meet a minimum threshold.
    /// </summary>
    Min,
    /// <summary>
    /// Requires the attribute to meet a maximum threshold.
    /// </summary>
    Max,
    /// <summary>
    /// Requires the attribute to be unique within the table.
    /// </summary>
    Unique,
    /// <summary>
    /// Requires the attribute to be a valid date.
    /// </summary>
    Date,
    /// <summary>
    /// Requires the attribute to be one of the allowed values.
    /// </summary>
    In,
    /// <summary>
    /// Requires the attribute to match a regular expression.
    /// </summary>
    Regex
}
