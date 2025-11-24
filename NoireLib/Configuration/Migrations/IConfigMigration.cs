using System.Text.Json;

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
    /// Executes the migration on the JSON document.
    /// </summary>
    /// <param name="jsonDocument">The JSON document to migrate.</param>
    /// <returns>The migrated JSON document as a string.</returns>
    string Migrate(JsonDocument jsonDocument);
}
