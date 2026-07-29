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
    /// Never carried by a notification; kept only because removing it would break consumers that name it.<br/>
    /// A watcher-level error travels as a <see cref="FileWatchError"/> through <see cref="NoireFileWatcher.Error"/>
    /// and <see cref="FileWatchErrorEvent"/> instead, so <see cref="FileWatchNotification.EventType"/> is never set
    /// to this value and a switch case testing for it is unreachable.
    /// </summary>
    Error
}
