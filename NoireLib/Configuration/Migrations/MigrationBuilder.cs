using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace NoireLib.Configuration.Migrations;

/// <summary>
/// Fluent helper for easily building JSON migrations with common operations like rename, delete, and type conversion.
/// Use MigrationBuilder.Create() to start building a migration.
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
    public static MigrationBuilder Create() => new();

    /// <summary>
    /// Renames a property in the JSON.
    /// </summary>
    public MigrationBuilder RenameProperty(string oldName, string newName)
    {
        _propertiesToRename[oldName] = newName;
        return this;
    }

    /// <summary>
    /// Deletes a property from the JSON.
    /// </summary>
    public MigrationBuilder DeleteProperty(string propertyName)
    {
        _propertiesToDelete.Add(propertyName);
        return this;
    }

    /// <summary>
    /// Deletes multiple properties from the JSON.
    /// </summary>
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
    /// Adds a new property with a default value.
    /// </summary>
    public MigrationBuilder AddProperty<T>(string propertyName, T defaultValue)
    {
        _propertiesToAdd[propertyName] = defaultValue;
        return this;
    }

    /// <summary>
    /// Adds a new property with a value computed from existing properties.
    /// </summary>
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
    public MigrationBuilder WithCustomOperation(Action<JsonElement, Utf8JsonWriter> operation)
    {
        _customOperations.Add(operation);
        return this;
    }

    /// <summary>
    /// Applies all operations and returns the migrated JSON string.
    /// </summary>
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
