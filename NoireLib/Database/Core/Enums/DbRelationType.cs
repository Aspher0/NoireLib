namespace NoireLib.Database;

/// <summary>
/// Defines supported relationship types between database models.
/// </summary>
public enum DbRelationType
{
    /// <summary>
    /// Defines a one-to-one relationship.
    /// </summary>
    HasOne,
    /// <summary>
    /// Defines a one-to-many relationship.
    /// </summary>
    HasMany,
    /// <summary>
    /// Defines an inverse one-to-one or many-to-one relationship.
    /// </summary>
    BelongsTo,
    /// <summary>
    /// Defines a many-to-many relationship.
    /// </summary>
    BelongsToMany
}
