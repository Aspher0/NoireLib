using NoireLib.Database;

namespace NoireLib.Database.Migrations;

/// <summary>
/// Base class for database schema migrations.
/// </summary>
public abstract class DatabaseMigrationBase : IDatabaseMigration
{
    /// <inheritdoc/>
    public abstract int FromVersion { get; }

    /// <inheritdoc/>
    public abstract int ToVersion { get; }

    /// <inheritdoc/>
    public abstract void Migrate(NoireDatabase database);
}
