using Newtonsoft.Json.Linq;

namespace NoireLib.Configuration.Migrations;

/// <summary>
/// Interface for defining configuration migrations between versions.
/// </summary>
public interface IConfigMigration
{
    /// <summary>
    /// The version this migration starts from.
    /// </summary>
    int FromVersion { get; }

    /// <summary>
    /// The version this migration upgrades to.
    /// </summary>
    int ToVersion { get; }

    /// <summary>
    /// Executes a migration on a JSON object.
    /// </summary>
    /// <param name="jsonObject">The JSON object to migrate.</param>
    /// <returns>The migrated JSON as a string.</returns>
    string Migrate(JObject jsonObject);
}
