using System.Collections.Generic;
using System.IO;

namespace NoireLib.FileWatcher;

/// <summary>
/// Configuration used when registering a filesystem watch.
/// </summary>
public sealed class FileWatchRegistrationOptions
{
    /// <summary>
    /// The target path to watch (directory or file according to <see cref="TargetType"/>).
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Defines whether the target path is automatically detected, forced as directory, or forced as file.
    /// </summary>
    public FileWatchTargetType TargetType { get; set; } = FileWatchTargetType.Auto;

    /// <summary>
    /// Optional glob patterns to filter incoming file events.
    /// </summary>
    public List<string> Patterns { get; set; } = ["*"];

    /// <summary>
    /// Whether subdirectories should be included when watching a directory.
    /// </summary>
    public bool IncludeSubdirectories { get; set; } = true;

    /// <summary>
    /// Native file watcher notify flags.
    /// </summary>
    public NotifyFilters NotifyFilter { get; set; } =
        NotifyFilters.FileName |
        NotifyFilters.DirectoryName |
        NotifyFilters.LastWrite |
        NotifyFilters.CreationTime |
        NotifyFilters.Size;

    /// <summary>
    /// Internal buffer size for the underlying watcher.
    /// </summary>
    public int InternalBufferSize { get; set; } = 8192;

    /// <summary>
    /// Whether this watch should be enabled when the module is active.
    /// </summary>
    public bool StartEnabled { get; set; } = true;

    /// <summary>
    /// Whether change notifications should be observed.
    /// </summary>
    public bool NotifyOnChanged { get; set; } = true;

    /// <summary>
    /// Whether created notifications should be observed.
    /// </summary>
    public bool NotifyOnCreated { get; set; } = true;

    /// <summary>
    /// Whether deleted notifications should be observed.
    /// </summary>
    public bool NotifyOnDeleted { get; set; } = true;

    /// <summary>
    /// Whether renamed notifications should be observed.
    /// </summary>
    public bool NotifyOnRenamed { get; set; } = true;

    /// <summary>
    /// Whether error notifications should be observed.
    /// </summary>
    public bool NotifyOnError { get; set; } = true;

    /// <summary>
    /// If true, allows registering a watch for a missing file or directory path.
    /// </summary>
    public bool AllowNonExistingPath { get; set; } = false;

    /// <summary>
    /// Optional user key to identify a registration semantically.
    /// </summary>
    public string? Key { get; set; } = null;
}
