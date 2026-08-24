using System;

namespace NoireLib.CommandRouter;

/// <summary>
/// Describes a typed argument for a subcommand, including its name, type, default value, and whether it is required.
/// </summary>
public sealed class CommandArgumentDefinition
{
    /// <summary>
    /// The name of the argument, used for retrieval from <see cref="ParsedCommandArguments"/>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The expected type of the argument value.
    /// </summary>
    public Type Type { get; }

    /// <summary>
    /// Whether this argument must be provided by the user.
    /// </summary>
    public bool IsRequired { get; }

    /// <summary>
    /// The fixed default value used when the argument is not required and is not provided.
    /// </summary>
    public object? DefaultValue { get; }

    /// <summary>
    /// The optional factory used to evaluate a default value when the argument is not required and is not provided.
    /// </summary>
    public Func<object?>? DefaultValueFactory { get; }

    /// <summary>
    /// An optional human-readable description of the argument, shown in help output.
    /// </summary>
    public string? Description { get; }

    internal CommandArgumentDefinition(string name, Type type, bool isRequired, object? defaultValue, string? description)
    {
        Name = name;
        Type = type;
        IsRequired = isRequired;
        DefaultValue = defaultValue;
        Description = description;
    }

    // Creates a new argument definition with a dynamically evaluated default value.
    internal CommandArgumentDefinition(string name, Type type, bool isRequired, Func<object?> defaultValueFactory, string? description)
    {
        Name = name;
        Type = type;
        IsRequired = isRequired;
        DefaultValueFactory = defaultValueFactory;
        Description = description;
    }

    internal object? GetDefaultValue()
        => DefaultValueFactory != null ? DefaultValueFactory() : DefaultValue;
}
