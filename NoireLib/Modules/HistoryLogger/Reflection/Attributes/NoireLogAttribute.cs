using System;

namespace NoireLib.HistoryLogger;

/// <summary>
/// Marks a member to log a history entry when invoked through a proxy created by <see cref="NoireHistoryLogger"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class NoireLogAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireLogAttribute"/> class.
    /// </summary>
    /// <param name="message">Optional custom message for the log entry.</param>
    /// <param name="category">Optional category for the log entry.</param>
    /// <param name="level">Optional severity level for the log entry.</param>
    public NoireLogAttribute(string? message = null, string? category = null, HistoryLogLevel level = HistoryLogLevel.Info)
    {
        Message = message;
        Category = category;
        Level = level;
    }

    /// <summary>
    /// Gets the custom message for the log entry.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets the optional category for the log entry.
    /// </summary>
    public string? Category { get; }

    /// <summary>
    /// Gets the severity level for the log entry.
    /// </summary>
    public HistoryLogLevel Level { get; }

    /// <summary>
    /// Gets or sets whether arguments should be included in the log message.
    /// </summary>
    public bool IncludeArguments { get; init; } = false;
}
