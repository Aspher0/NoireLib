using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace NoireLib.Configuration.Migrations;

/// <summary>
/// Fluent helper for easily building JSON migrations with common operations like rename, delete, and type conversion.<br/>
/// Use MigrationBuilder.Create() to start building a migration.<br/>
/// Meant for use with <see cref="ConfigMigrationBase"/>.
/// </summary>
public class MigrationBuilder
{
    private readonly HashSet<string> _propertiesToDelete = new();
    private readonly Dictionary<string, string> _propertiesToRename = new();
    private readonly Dictionary<string, object?> _propertiesToAdd = new();
    private readonly List<Action<JsonElement, Utf8JsonWriter>> _customOperations = new();
    private readonly HashSet<string> _propertiesWithTypeChange = new();

    private MigrationBuilder() { }

    /// <summary>
    /// Creates a new migration builder.
    /// </summary>
    /// <returns>A new instance of <see cref="MigrationBuilder"/>.</returns>
    public static MigrationBuilder Create() => new();

    /// <summary>
    /// Renames a property in the JSON.
    /// </summary>
    /// <param name="oldName">The name of the property in the previous version.</param>
    /// <param name="newName">The new name for the property.</param>
    /// <returns>The MigrationBuilder instance for chaining.</returns>
    public MigrationBuilder RenameProperty(string oldName, string newName)
    {
        _propertiesToRename[oldName] = newName;
        return this;
    }

    /// <summary>
    /// Deletes a property from the JSON.<br/>
    /// Does not need to be called if you omit the property in your configuration class.<br/>
    /// Added for completeness.
    /// </summary>
    /// <param name="propertyName">The name of the property to delete.</param>
    /// <returns>The MigrationBuilder instance for chaining.</returns>
    public MigrationBuilder DeleteProperty(string propertyName)
    {
        _propertiesToDelete.Add(propertyName);
        return this;
    }

    /// <summary>
    /// Deletes multiple properties from the JSON.<br/>
    /// Does not need to be called if you omit the properties in your configuration class.<br/>
    /// Added for completeness.
    /// </summary>
    /// <param name="propertyNames">The names of the properties to delete.</param>
    /// <returns>The MigrationBuilder instance for chaining.</returns>
    public MigrationBuilder DeleteProperties(params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            _propertiesToDelete.Add(name);
        }
        return this;
    }

    /// <summary>
    /// Changes the type of a property with a custom converter.
    /// </summary>
    /// <typeparam name="TFrom">The original type of the property.</typeparam>
    /// <typeparam name="TTo">The new type of the property.</typeparam>
    /// <param name="propertyName">The name of the property to change.</param>
    /// <param name="converter">A conversion function that takes one argument TFrom and returns TTo.</param>
    /// <returns>The MigrationBuilder instance for chaining.</returns>
    public MigrationBuilder ChangePropertyType<TFrom, TTo>(string propertyName, Func<TFrom, TTo> converter)
    {
        _propertiesWithTypeChange.Add(propertyName);
        _customOperations.Add((root, writer) =>
        {
            if (root.TryGetProperty(propertyName, out var value))
            {
                try
                {
                    var oldValue = JsonSerializer.Deserialize<TFrom>(value.GetRawText());
                    if (oldValue != null)
                    {
                        var newValue = converter(oldValue);
                        writer.WritePropertyName(propertyName);
                        JsonSerializer.Serialize(writer, newValue);
                    }
                }
                catch
                {
                    // If conversion fails, write original value
                    writer.WritePropertyName(propertyName);
                    value.WriteTo(writer);
                }
            }
        });
        return this;
    }

    /// <summary>
    /// Adds a new property with a default value.<br/>
    /// Does not need to be called since you add the property in your configuration class with a default value.<br/>
    /// Added for completeness.
    /// </summary>
    /// <typeparam name="T">The type of the property to add.</typeparam>
    /// <param name="propertyName">The name of the property to add.</param>
    /// <param name="defaultValue">The default value for the new property.</param>
    /// <returns>The MigrationBuilder instance for chaining.</returns>
    public MigrationBuilder AddProperty<T>(string propertyName, T defaultValue)
    {
        _propertiesToAdd[propertyName] = defaultValue;
        return this;
    }

    /// <summary>
    /// Adds a new property with a value computed from existing properties.
    /// </summary>
    /// <typeparam name="T">The type of the property to add.</typeparam>
    /// <param name="propertyName">The name of the property to add.</param>
    /// <param name="computeValue">A function that computes the value based on the existing JSON element.</param>
    /// <returns>The MigrationBuilder instance for chaining.</returns>
    public MigrationBuilder AddComputedProperty<T>(string propertyName, Func<JsonElement, T> computeValue)
    {
        _customOperations.Add((root, writer) =>
        {
            var value = computeValue(root);
            writer.WritePropertyName(propertyName);
            JsonSerializer.Serialize(writer, value);
        });
        return this;
    }

    /// <summary>
    /// Transforms a property value using a custom function.
    /// </summary>
    /// <typeparam name="T">The type of the property to transform.</typeparam>
    /// <param name="propertyName">The name of the property to transform.</param>
    /// <param name="transform">A transformation function that takes one argument of type T and returns a transformed value of type T.</param>
    /// <returns>The MigrationBuilder instance for chaining.</returns>
    public MigrationBuilder TransformProperty<T>(string propertyName, Func<T, T> transform)
    {
        _customOperations.Add((root, writer) =>
        {
            if (root.TryGetProperty(propertyName, out var value))
            {
                try
                {
                    var oldValue = JsonSerializer.Deserialize<T>(value.GetRawText());
                    if (oldValue != null)
                    {
                        var newValue = transform(oldValue);
                        writer.WritePropertyName(propertyName);
                        JsonSerializer.Serialize(writer, newValue);
                    }
                }
                catch
                {
                    writer.WritePropertyName(propertyName);
                    value.WriteTo(writer);
                }
            }
        });
        return this;
    }

    /// <summary>
    /// Adds a custom operation to the migration.
    /// </summary>
    /// <param name="operation">An action that takes the root JsonElement and a Utf8JsonWriter to perform custom migration logic.</param>
    /// <returns>The MigrationBuilder instance for chaining.</returns>
    public MigrationBuilder WithCustomOperation(Action<JsonElement, Utf8JsonWriter> operation)
    {
        _customOperations.Add(operation);
        return this;
    }

    /// <summary>
    /// Applies all operations and returns the migrated JSON string.
    /// </summary>
    /// <param name="document">The original JSON document.</param>
    /// <param name="targetVersion">The target version number to set in the migrated JSON.</param>
    /// <returns>The migrated JSON string.</returns>
    public string Migrate(JsonDocument document, int targetVersion)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();

        var root = document.RootElement;
        var processedProperties = new HashSet<string>();

        foreach (var property in root.EnumerateObject())
        {
            var propertyName = property.Name;

            if (propertyName == "Version")
                continue;

            if (_propertiesToDelete.Contains(propertyName))
                continue;

            if (_propertiesWithTypeChange.Contains(propertyName))
                continue;

            if (_propertiesToRename.TryGetValue(propertyName, out var newName))
            {
                if (_propertiesWithTypeChange.Contains(newName))
                {
                    continue;
                }

                writer.WritePropertyName(newName);
                property.Value.WriteTo(writer);
                processedProperties.Add(newName);
            }
            else
            {
                property.WriteTo(writer);
                processedProperties.Add(propertyName);
            }
        }

        foreach (var operation in _customOperations)
        {
            operation(root, writer);
        }

        foreach (var (name, value) in _propertiesToAdd)
        {
            if (!processedProperties.Contains(name))
            {
                writer.WritePropertyName(name);
                JsonSerializer.Serialize(writer, value);
            }
        }

        writer.WriteNumber("Version", targetVersion);

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
