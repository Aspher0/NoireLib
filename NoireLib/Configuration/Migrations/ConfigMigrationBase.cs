using System.Text.Json;

namespace NoireLib.Configuration.Migrations;

/// <summary>
/// Base class for simple configuration migrations that provides helper methods.
/// </summary>
public abstract class ConfigMigrationBase : IConfigMigration
{
    /// <inheritdoc/>
    public abstract int FromVersion { get; }

    /// <inheritdoc/>
    public abstract int ToVersion { get; }

    /// <inheritdoc/>
    public abstract string Migrate(JsonDocument jsonDocument);

    /// <summary>
    /// Helper method to create a mutable JSON document for easier manipulation.
    /// </summary>
    /// <param name="jsonDocument">The source JSON document.</param>
    /// <returns>A JsonElement that can be modified.</returns>
    protected JsonElement GetRootElement(JsonDocument jsonDocument)
    {
        return jsonDocument.RootElement.Clone();
    }

    /// <summary>
    /// Helper method to serialize a JsonElement back to a JSON string.
    /// </summary>
    /// <param name="element">The JsonElement to serialize.</param>
    /// <param name="writeIndented">Whether to write indented JSON.</param>
    /// <returns>The JSON string.</returns>
    protected string SerializeElement(JsonElement element, bool writeIndented = true)
    {
        var options = new JsonSerializerOptions { WriteIndented = writeIndented };
        return JsonSerializer.Serialize(element, options);
    }
}
