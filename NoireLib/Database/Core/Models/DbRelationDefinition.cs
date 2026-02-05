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
    /// Gets the foreign key column name.
    /// </summary>
    /// <remarks>
    /// For has-one/has-many relations, this is the column on the related table that points back to the current model.
    /// </remarks>
    /// <example>
    /// A <c>User</c> model with many <c>Post</c> rows can set <see cref="ForeignKey"/> to <c>posts.user_id</c>.
    /// </example>
    public string? ForeignKey { get; init; }
    /// <summary>
    /// Gets the local key column name.
    /// </summary>
    /// <remarks>
    /// The local key is the column on the current model that the related table references.
    /// </remarks>
    /// <example>
    /// If a <c>User</c> model uses <c>id</c> as its primary key, the local key is <c>id</c> for a has-many relation.
    /// </example>
    public string? LocalKey { get; init; }
    /// <summary>
    /// Gets the owner key column name.
    /// </summary>
    /// <remarks>
    /// Used for belongs-to relations to specify the column on the current model that stores the related model key.
    /// </remarks>
    /// <example>
    /// A <c>Post</c> model that belongs to a <c>User</c> can use <c>posts.user_id</c> as the owner key.
    /// </example>
    public string? OwnerKey { get; init; }
    /// <summary>
    /// Gets the pivot table name for many-to-many relationships.
    /// </summary>
    /// <remarks>
    /// The pivot table joins the current model with the related model for belongs-to-many relations.
    /// </remarks>
    /// <example>
    /// A many-to-many relation between <c>User</c> and <c>Role</c> might use a pivot table named <c>user_roles</c>.
    /// </example>
    public string? PivotTable { get; init; }
    /// <summary>
    /// Gets the foreign pivot key column name.
    /// </summary>
    /// <remarks>
    /// This is the column on the pivot table that references the current model's key.
    /// </remarks>
    /// <example>
    /// For a <c>User</c> to <c>Role</c> pivot table, the foreign pivot key can be <c>user_roles.user_id</c>.
    /// </example>
    public string? ForeignPivotKey { get; init; }
    /// <summary>
    /// Gets the related pivot key column name.
    /// </summary>
    /// <remarks>
    /// This is the column on the pivot table that references the related model's key.
    /// </remarks>
    /// <example>
    /// For a <c>User</c> to <c>Role</c> pivot table, the related pivot key can be <c>user_roles.role_id</c>.
    /// </example>
    public string? RelatedPivotKey { get; init; }
    /// <summary>
    /// Gets the parent key column name.
    /// </summary>
    /// <remarks>
    /// This is the column on the current model that maps to the foreign pivot key in belongs-to-many relations.
    /// </remarks>
    /// <example>
    /// A <c>User</c> model with primary key <c>id</c> uses <c>users.id</c> as the parent key for a pivot table relation.
    /// </example>
    public string? ParentKey { get; init; }
}
