namespace NoireLib.FileWatcher;

/// <summary>
/// Represents the semantic type of a file watcher notification.
/// </summary>
public enum FileWatchEventType
{
    /// <summary>
    /// A file or directory was created.
    /// </summary>
    Created,

    /// <summary>
    /// A file or directory was modified.
    /// </summary>
    Changed,

    /// <summary>
    /// A file or directory was deleted.
    /// </summary>
    Deleted,

    /// <summary>
    /// A file or directory was renamed.
    /// </summary>
    Renamed,

    /// <summary>
    /// Never carried by a notification, and kept only because removing it would break consumers that name it.<br/>
    /// A watcher-level error is not a filesystem notification: it reports an exception rather than a path, so it
    /// travels as a <see cref="FileWatchError"/> through <see cref="NoireFileWatcher.Error"/> and
    /// <see cref="FileWatchErrorEvent"/> instead of as a <see cref="FileWatchNotification"/>. No
    /// <see cref="FileWatchNotification.EventType"/> is ever set to this value, so a switch case testing for it is
    /// unreachable. Handle the error event to observe watcher errors.
    /// </summary>
    Error
}
