using System;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// An immutable snapshot of a tracked addon state at a point in time.
/// </summary>
/// <param name="AddonName">The tracked addon name.</param>
/// <param name="Exists">Whether the addon pointer currently exists.</param>
/// <param name="IsLoaded">Whether the addon has completed ULD loading.</param>
/// <param name="IsVisible">Whether the addon is currently visible.</param>
/// <param name="IsReady">Whether the addon is ready to be interacted with.</param>
/// <param name="ObservedAt">The UTC timestamp when the snapshot was captured.</param>
public sealed record AddonStateSnapshot(
    string AddonName,
    bool Exists,
    bool IsLoaded,
    bool IsVisible,
    bool IsReady,
    DateTimeOffset ObservedAt)
{
    /// <summary>
    /// Gets a value indicating whether the addon currently exists in memory.
    /// </summary>
    public bool IsAvailable => Exists;

    /// <summary>
    /// Gets a value indicating whether the addon is currently open and interactable.
    /// </summary>
    public bool IsOpen => Exists && IsLoaded && IsVisible && IsReady;

    /// <summary>
    /// Determines whether another snapshot represents the same observed addon state, ignoring capture timestamp.
    /// </summary>
    /// <param name="other">The snapshot to compare against.</param>
    /// <returns><see langword="true"/> if the observed addon state matches; otherwise, <see langword="false"/>.</returns>
    public bool HasSameObservedState(AddonStateSnapshot? other)
        => other != null
        && AddonName.Equals(other.AddonName, StringComparison.Ordinal)
        && Exists == other.Exists
        && IsLoaded == other.IsLoaded
        && IsVisible == other.IsVisible
        && IsReady == other.IsReady;
}
