using System;

namespace NoireLib.Configuration.Migrations;

/// <summary>
/// Attribute to register a migration for a configuration type.
/// Can be applied multiple times to register multiple migrations.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class ConfigMigrationAttribute : Attribute
{
    /// <summary>
    /// The type of migration to execute. Must implement <see cref="IConfigMigration"/>.
    /// </summary>
    public Type MigrationType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigMigrationAttribute"/> class.
    /// </summary>
    /// <param name="migrationType">The type of migration. Must implement <see cref="IConfigMigration"/>.</param>
    public ConfigMigrationAttribute(Type migrationType)
    {
        if (!typeof(IConfigMigration).IsAssignableFrom(migrationType))
            throw new ArgumentException($"Type {migrationType.Name} must implement IConfigMigration", nameof(migrationType));

        MigrationType = migrationType;
    }
}
