namespace NoireLib.FileWatcher;

/// <summary>
/// Defines which kind of filesystem target should be watched.
/// </summary>
public enum FileWatchTargetType
{
    /// <summary>
    /// Automatically detects whether <see cref="FileWatchRegistrationOptions.Path"/> is a directory or file.
    /// </summary>
    Auto,

    /// <summary>
    /// Watches a directory.
    /// </summary>
    Directory,

    /// <summary>
    /// Watches a single file.
    /// </summary>
    File
}
