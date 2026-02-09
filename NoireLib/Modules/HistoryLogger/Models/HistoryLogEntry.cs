using System;

namespace NoireLib.HistoryLogger;

/// <summary>
/// Represents a single history log entry.
/// </summary>
public sealed record HistoryLogEntry
{
    /// <summary>
    /// Gets the database identifier for the entry when persisted.
    /// </summary>
    public long? Id { get; init; }

    /// <summary>
    /// Gets the timestamp when the entry was created.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the category assigned to the entry.
    /// </summary>
    public string Category { get; init; } = "General";

    /// <summary>
    /// Gets the message describing the log entry.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional source of the log entry (method, type, or system).
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Gets the severity level of the entry.
    /// </summary>
    public HistoryLogLevel Level { get; init; } = HistoryLogLevel.Info;
}
