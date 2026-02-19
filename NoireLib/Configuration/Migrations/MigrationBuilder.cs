using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace NoireLib.Configuration.Migrations;

/// <summary>
/// Fluent helper for easily building JSON migrations with common operations like rename, delete, and type conversion.<br/>
/// Use MigrationBuilder.Create() to start building a migration.<br/>
/// Meant for use with <see cref="ConfigMigrationBase"/>.
/// </summary>
public class MigrationBuilder
{
    private readonly List<Action<JObject>> _orderedOperations = new();
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
        _orderedOperations.Add(root =>
        {
            if (root.ContainsKey(oldName))
            {
                root[newName] = root[oldName];
                root.Remove(oldName);
            }
        });
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
        _orderedOperations.Add(root => root.Remove(propertyName));
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
            _orderedOperations.Add(root => root.Remove(name));
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
        _orderedOperations.Add(root =>
        {
            if (root.TryGetValue(propertyName, out JToken? value))
            {
                try
                {
                    var oldValue = value.ToObject<TFrom>();
                    if (oldValue != null)
                    {
                        var newValue = converter(oldValue);
                        root[propertyName] = JToken.FromObject(newValue);
                    }
                }
                catch
                {
                    // If conversion fails, keep original value
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
        _orderedOperations.Add(root =>
        {
            if (!root.ContainsKey(propertyName))
                root[propertyName] = JToken.FromObject(defaultValue);
        });
        return this;
    }

    /// <summary>
    /// Adds a new property with a value computed from existing properties.
    /// </summary>
    /// <typeparam name="T">The type of the property to add.</typeparam>
    /// <param name="propertyName">The name of the property to add.</param>
    /// <param name="computeValue">A function that computes the value based on the existing JSON element.</param>
    /// <returns>The MigrationBuilder instance for chaining.</returns>
    public MigrationBuilder AddComputedProperty<T>(string propertyName, Func<JObject, T> computeValue)
    {
        _orderedOperations.Add(root =>
        {
            var value = computeValue(root);
            root[propertyName] = JToken.FromObject(value);
        });
        return this;
    }

    /// <summary>
    /// Transforms a property value using a custom function.
    /// </summary>
    /// <typeparam name="T">The type of the property to transform.</typeparam>
    /// <param name="propertyName">The name of the property to transform.</param>
    /// <param name="transform">A transformation function that takes one argument of type <typeparamref name="T"/> and returns a transformed value of type <typeparamref name="T"/>.</param>
    /// <returns>The MigrationBuilder instance for chaining.</returns>
    public MigrationBuilder TransformProperty<T>(string propertyName, Func<T, T> transform)
    {
        _orderedOperations.Add(root =>
        {
            if (root.TryGetValue(propertyName, out JToken? value))
            {
                try
                {
                    var oldValue = value.ToObject<T>();
                    if (oldValue != null)
                    {
                        var newValue = transform(oldValue);
                        root[propertyName] = JToken.FromObject(newValue);
                    }
                }
                catch
                {
                    // If conversion fails, keep original value
                }
            }
        });
        return this;
    }

    /// <summary>
    /// Adds a custom operation to the migration chain.<br/>
    /// The operation receives the current <see cref="JObject"/> and can perform any transformation or logic.
    /// </summary>
    /// <param name="operation">An action that takes the root <see cref="JObject"/> to perform custom migration logic.</param>
    /// <returns>The MigrationBuilder instance for chaining.</returns>
    public MigrationBuilder WithCustomOperation(Action<JObject> operation)
    {
        _orderedOperations.Add(operation);
        return this;
    }

    /// <summary>
    /// Applies all operations and returns the migrated JSON string.
    /// </summary>
    /// <param name="document">The original JSON document.</param>
    /// <param name="targetVersion">The target version number to set in the migrated JSON.</param>
    /// <returns>The migrated JSON string.</returns>
    public string Migrate(JObject document, int targetVersion)
    {
        var root = (JObject)document.DeepClone();
        foreach (var op in _orderedOperations)
        {
            op(root);
        }
        root["Version"] = targetVersion;
        return root.ToString(Formatting.Indented);
    }
}
