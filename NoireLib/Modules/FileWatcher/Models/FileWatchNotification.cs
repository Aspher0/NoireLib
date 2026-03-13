using System;
using System.IO;

namespace NoireLib.FileWatcher;

/// <summary>
/// Describes one filesystem event captured by <see cref="NoireFileWatcher"/>.
/// </summary>
/// <param name="WatchId">The ID of the watch registration that captured this event.</param>
/// <param name="RootPath">The root path of the watch registration that captured this event.</param>
/// <param name="TargetType">The type of the target that triggered this event.</param>
/// <param name="FullPath">The full path of the target that triggered this event.</param>
/// <param name="Name">The name of the target that triggered this event, if available.</param>
/// <param name="EventType">The semantic type of this event.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when this event occurred.</param>
/// <param name="NativeChangeType">The native change type as reported by the underlying filesystem watcher for this event.</param>
/// <param name="OldFullPath">The old full path of the target that triggered this event, if this event is a rename event and the underlying filesystem watcher provides this information.</param>
/// <param name="OldName">The old name of the target that triggered this event, if this event is a rename event and the underlying filesystem watcher provides this information.</param>
/// <param name="WatchKey">The watch key associated with the watch registration that captured this event, if available.</param>
public sealed record FileWatchNotification(
    string WatchId,
    string RootPath,
    FileWatchTargetType TargetType,
    string FullPath,
    string? Name,
    FileWatchEventType EventType,
    DateTimeOffset OccurredAtUtc,
    WatcherChangeTypes NativeChangeType,
    string? OldFullPath = null,
    string? OldName = null,
    string? WatchKey = null)
{
    /// <summary>
    /// The path relative to <see cref="RootPath"/> if possible.
    /// </summary>
    public string RelativePath
    {
        get
        {
            try
            {
                return Path.GetRelativePath(RootPath, FullPath);
            }
            catch
            {
                return FullPath;
            }
        }
    }
}

/// <summary>
/// Describes one filesystem watcher error event.
/// </summary>
/// <param name="WatchId">The ID of the watch registration that captured this error event.</param>
/// <param name="RootPath">The root path of the watch registration that captured this error event.</param>
/// <param name="Exception">The exception that was thrown by the underlying filesystem watcher.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when this error event occurred.</param>
/// <param name="WatchKey">The watch key associated with the watch registration that captured this error event, if available.</param>
public sealed record FileWatchError(
    string WatchId,
    string RootPath,
    Exception Exception,
    DateTimeOffset OccurredAtUtc,
    string? WatchKey = null);
