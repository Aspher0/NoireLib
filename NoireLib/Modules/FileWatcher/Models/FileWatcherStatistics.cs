namespace NoireLib.FileWatcher;

/// <summary>
/// Represents metrics for a <see cref="NoireFileWatcher"/> instance.
/// </summary>
/// <param name="RegisteredWatches">The current number of registered watches.</param>
/// <param name="EnabledWatches">The current number of enabled watches.</param>
/// <param name="TotalRegistrations">The total number of watch registrations created since the watcher's creation.</param>
/// <param name="TotalRemoved">The total number of watch registrations removed since the watcher's creation.</param>
/// <param name="TotalNotificationsObserved">The total number of notifications observed from the underlying filesystem watchers since the watcher's creation.</param>
/// <param name="TotalNotificationsDispatched">The total number of notifications dispatched to user callbacks since the watcher's creation.</param>
/// <param name="TotalErrors">The total number of errors observed from the underlying filesystem watchers since the watcher's creation.</param>
/// <param name="TotalDuplicateNotificationsSuppressed">The total number of duplicate notifications that have been suppressed since the watcher's creation.</param>
/// <param name="TotalCallbackExceptionsCaught">The total number of exceptions caught from user callbacks since the watcher's creation.</param>
public sealed record FileWatcherStatistics(
    int RegisteredWatches,
    int EnabledWatches,
    long TotalRegistrations,
    long TotalRemoved,
    long TotalNotificationsObserved,
    long TotalNotificationsDispatched,
    long TotalErrors,
    long TotalDuplicateNotificationsSuppressed,
    long TotalCallbackExceptionsCaught);
