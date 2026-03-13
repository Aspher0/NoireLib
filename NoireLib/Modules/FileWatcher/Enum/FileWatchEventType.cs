namespace NoireLib.FileWatcher;

/// <summary>
/// Represents the semantic type of a file watcher notification.
/// </summary>
public enum FileWatchEventType
{
    Created,
    Changed,
    Deleted,
    Renamed,
    Error
}
