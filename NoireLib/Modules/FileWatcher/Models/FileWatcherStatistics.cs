namespace NoireLib.FileWatcher;

/// <summary>
/// Represents metrics for a <see cref="NoireFileWatcher"/> instance.
/// </summary>
/// <param name="RegisteredWatches">The current number of registered watches.</param>
/// <param name="EnabledWatches">The current number of enabled watches.</param>
/// <param name="TotalRegistrations">The total number of watch registrations created.</param>
/// <param name="TotalRemoved">The total number of watch registrations removed.</param>
/// <param name="TotalNotificationsObserved">The total number of notifications observed from the underlying filesystem watchers.</param>
/// <param name="TotalNotificationsDispatched">The total number of notifications dispatched to user callbacks.</param>
/// <param name="TotalErrors">The total number of errors observed from the underlying filesystem watchers.</param>
/// <param name="TotalDuplicateNotificationsSuppressed">The total number of duplicate notifications that have been suppressed.</param>
/// <param name="TotalCallbackExceptionsCaught">The total number of exceptions caught from user callbacks.</param>
public sealed record FileWatcherStatistics(
    int RegisteredWatches,
    int EnabledWatches,
    long TotalRegistrations,
    long TotalRemoved,
    long TotalNotificationsObserved,
    long TotalNotificationsDispatched,
    long TotalErrors,
    long TotalDuplicateNotificationsSuppressed,
    long TotalCallbackExceptionsCaught)
{
    /// <summary>
    /// The total number of deliveries discarded because the framework thread delivery queue was at capacity.<br/>
    /// A non-zero value means <see cref="TotalNotificationsDispatched"/> undercounts what the filesystem reported:
    /// those notifications were observed but never reached a callback, because handlers were too slow for the
    /// event volume or filesystem activity outpaced the game's frame rate.
    /// </summary>
    public long TotalDeliveriesDropped { get; init; }
}
