using NoireLib.Database;

namespace NoireLib.Database.Migrations;

/// <summary>
/// Represents a database schema migration.
/// </summary>
public interface IDatabaseMigration
{
    /// <summary>
    /// Gets the source schema version for the migration.
    /// </summary>
    int FromVersion { get; }

    /// <summary>
    /// Gets the target schema version for the migration.
    /// </summary>
    int ToVersion { get; }

    /// <summary>
    /// Executes the migration against the provided database.
    /// </summary>
    /// <param name="database">The database instance.</param>
    void Migrate(NoireDatabase database);
}
