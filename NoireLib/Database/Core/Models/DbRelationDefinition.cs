using System;

namespace NoireLib.Database;

/// <summary>
/// Describes a relationship between two database models.
/// </summary>
public sealed class DbRelationDefinition
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DbRelationDefinition"/> class.
    /// </summary>
    /// <param name="type">The relationship type.</param>
    /// <param name="relatedModelType">The related model type.</param>
    public DbRelationDefinition(DbRelationType type, Type relatedModelType)
    {
        Type = type;
        RelatedModelType = relatedModelType;
    }

    /// <summary>
    /// Gets the relationship type.
    /// </summary>
    public DbRelationType Type { get; }
    /// <summary>
    /// Gets the related model type.
    /// </summary>
    public Type RelatedModelType { get; }
    /// <summary>
    /// The foreign key column name. For has-one/has-many relations, the column on the related table that points
    /// back to the current model.
    /// </summary>
    public string? ForeignKey { get; init; }
    /// <summary>
    /// The local key column name: the column on the current model that the related table references.
    /// </summary>
    public string? LocalKey { get; init; }
    /// <summary>
    /// The owner key column name, for belongs-to relations: the column on the current model that stores the
    /// related model key.
    /// </summary>
    public string? OwnerKey { get; init; }
    /// <summary>
    /// The pivot table name for many-to-many relationships, joining the current model with the related model.
    /// </summary>
    public string? PivotTable { get; init; }
    /// <summary>
    /// The foreign pivot key column name: the column on the pivot table that references the current model's key.
    /// </summary>
    public string? ForeignPivotKey { get; init; }
    /// <summary>
    /// The related pivot key column name: the column on the pivot table that references the related model's key.
    /// </summary>
    public string? RelatedPivotKey { get; init; }
    /// <summary>
    /// The parent key column name: the column on the current model that maps to the foreign pivot key in
    /// belongs-to-many relations.
    /// </summary>
    public string? ParentKey { get; init; }
}
