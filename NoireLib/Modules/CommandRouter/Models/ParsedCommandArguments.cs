using System;
using System.Collections.Generic;

namespace NoireLib.CommandRouter;

/// <summary>
/// Holds the parsed argument values from a dispatched subcommand invocation.<br/>
/// Provides typed retrieval of arguments by name.
/// </summary>
public sealed class ParsedCommandArguments
{
    private readonly Dictionary<string, object?> values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The raw argument string as received from Dalamud, before any parsing.
    /// </summary>
    public string RawArgs { get; }

    /// <summary>
    /// The tokenized argument values (excluding the subcommand name).
    /// </summary>
    public string[] RawTokens { get; }

    /// <summary>
    /// Creates a new parsed arguments container.
    /// </summary>
    /// <param name="rawArgs">The raw argument string.</param>
    /// <param name="rawTokens">The tokenized argument values.</param>
    internal ParsedCommandArguments(string rawArgs, string[] rawTokens)
    {
        RawArgs = rawArgs;
        RawTokens = rawTokens;
    }

    /// <summary>
    /// Sets an argument value.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <param name="value">The argument value.</param>
    internal void Set(string name, object? value) => values[name] = value;

    /// <summary>
    /// Retrieves a typed argument value by name.
    /// </summary>
    /// <typeparam name="T">The expected type of the argument.</typeparam>
    /// <param name="name">The argument name.</param>
    /// <returns>The argument value cast to <typeparamref name="T"/>.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the argument name does not exist.</exception>
    public T Get<T>(string name)
    {
        if (!values.TryGetValue(name, out var value))
            throw new KeyNotFoundException($"Argument '{name}' was not found.");

        return (T)value!;
    }

    /// <summary>
    /// Retrieves a typed argument value by name, or a default value if the argument is not present or is null.
    /// </summary>
    /// <typeparam name="T">The expected type of the argument.</typeparam>
    /// <param name="name">The argument name.</param>
    /// <param name="defaultValue">The fallback value to return.</param>
    /// <returns>The argument value, or <paramref name="defaultValue"/> if not found.</returns>
    public T GetOrDefault<T>(string name, T defaultValue = default!)
    {
        if (!values.TryGetValue(name, out var value) || value is null)
            return defaultValue;

        return (T)value;
    }

    /// <summary>
    /// Checks whether an argument with the given name was parsed.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <returns>True if the argument exists; otherwise, false.</returns>
    public bool Has(string name) => values.ContainsKey(name);
}
