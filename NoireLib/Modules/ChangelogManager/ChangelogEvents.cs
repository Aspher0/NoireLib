using System;

namespace NoireLib.Changelog;

/// <summary>
/// Events published by the NoireChangelogManager module.
/// </summary>

/// <summary>
/// Event fired when the changelog window is opened.
/// </summary>
/// <param name="Version">The version being displayed.</param>
public record ChangelogWindowOpenedEvent(Version Version);

/// <summary>
/// Event fired when the changelog window is closed.
/// </summary>
public record ChangelogWindowClosedEvent();

/// <summary>
/// Event fired when the user changes the selected version in the changelog window.
/// </summary>
/// <param name="OldVersion">The previously selected version.</param>
/// <param name="NewVersion">The newly selected version.</param>
public record ChangelogVersionChangedEvent(Version? OldVersion, Version NewVersion);

/// <summary>
/// Event fired when a new changelog version is added to the manager.
/// </summary>
/// <param name="Version">The version that was added.</param>
public record ChangelogVersionAddedEvent(Version Version);

/// <summary>
/// Event fired when a changelog version is removed from the manager.
/// </summary>
/// <param name="Version">The version that was removed.</param>
public record ChangelogVersionRemovedEvent(Version Version);

/// <summary>
/// Event fired when all changelog versions are cleared from the manager.
/// </summary>
public record ChangelogVersionsClearedEvent();

/// <summary>
/// Event fired when the last seen version is updated.
/// </summary>
/// <param name="Version">The new last seen version.</param>
public record ChangelogLastSeenVersionUpdatedEvent(Version Version);

/// <summary>
/// Event fired when the last seen version is cleared.
/// </summary>
public record ChangelogLastSeenVersionClearedEvent();
