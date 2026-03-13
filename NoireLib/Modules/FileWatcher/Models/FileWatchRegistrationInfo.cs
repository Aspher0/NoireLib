using System.Collections.Generic;
using System.IO;

namespace NoireLib.FileWatcher;

/// <summary>
/// Immutable view of a watch registration.
/// </summary>
/// <param name="WatchId">The unique ID of the watch registration.</param>
/// <param name="Path">The root path of the watch registration.</param>
/// <param name="TargetType">The type of the target being watched.</param>
/// <param name="Patterns">The list of patterns used to filter the watched files.</param>
/// <param name="IncludeSubdirectories">Indicates whether subdirectories are included in the watch.</param>
/// <param name="NotifyFilter">The filter used to determine which changes to watch for.</param>
/// <param name="IsEnabled">Indicates whether the watch registration is currently enabled.</param>
/// <param name="Key">The optional key associated with the watch registration.</param>
/// <param name="CallbackCount">The number of synchronous callbacks registered for this watch registration.</param>
/// <param name="AsyncCallbackCount">The number of asynchronous callbacks registered for this watch registration.</param>
public sealed record FileWatchRegistrationInfo(
    string WatchId,
    string Path,
    FileWatchTargetType TargetType,
    IReadOnlyList<string> Patterns,
    bool IncludeSubdirectories,
    NotifyFilters NotifyFilter,
    bool IsEnabled,
    string? Key,
    int CallbackCount,
    int AsyncCallbackCount);
