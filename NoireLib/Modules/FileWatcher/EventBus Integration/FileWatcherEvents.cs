namespace NoireLib.FileWatcher;

/// <summary>
/// Published when a new watch registration is created.
/// </summary>
public sealed record FileWatchRegisteredEvent(FileWatchRegistrationInfo Registration);

/// <summary>
/// Published when a watch registration is removed.
/// </summary>
public sealed record FileWatchRemovedEvent(string WatchId, string Path, string? Key);

/// <summary>
/// Published when a watch registration is enabled or disabled.
/// </summary>
public sealed record FileWatchStateChangedEvent(string WatchId, bool Enabled);

/// <summary>
/// Published for every captured filesystem notification.
/// </summary>
public sealed record FileWatchNotificationEvent(FileWatchNotification Notification);

/// <summary>
/// Published when a filesystem error is raised by an underlying watcher.
/// </summary>
public sealed record FileWatchErrorEvent(FileWatchError Error);

/// <summary>
/// Published when all watches are removed.
/// </summary>
public sealed record FileWatchesClearedEvent(int RemovedCount);
