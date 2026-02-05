using System;

namespace NoireLib.Database.Migrations;

/// <summary>
/// Associates a migration with a database name.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DatabaseMigrationAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseMigrationAttribute"/> class.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    public DatabaseMigrationAttribute(string databaseName)
    {
        DatabaseName = databaseName;
    }

    /// <summary>
    /// Gets the database name associated with the migration.
    /// </summary>
    public string DatabaseName { get; }
}
