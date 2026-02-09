namespace NoireLib.HistoryLogger;

/// <summary>
/// Defines the severity level for history log entries.
/// </summary>
public enum HistoryLogLevel
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    Trace,
    Debug,
    Info,
    Warning,
    Error,
    Critical
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
