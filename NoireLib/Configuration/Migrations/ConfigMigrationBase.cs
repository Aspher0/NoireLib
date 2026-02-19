using Newtonsoft.Json.Linq;

namespace NoireLib.Configuration.Migrations;

/// <summary>
/// Base class for migrating configurations.<br/>
/// Meant for use with <see cref="MigrationBuilder"/> and <see cref="NoireConfigBase"/>, <see cref="NoireConfigBase{T}"/>.
/// </summary>
public abstract class ConfigMigrationBase : IConfigMigration
{
    /// <inheritdoc/>
    public abstract int FromVersion { get; }

    /// <inheritdoc/>
    public abstract int ToVersion { get; }

    /// <inheritdoc/>
    public abstract string Migrate(JObject jsonObject);
}
