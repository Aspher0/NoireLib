using NoireLib.Configuration;
using System;

namespace NoireLib.Changelog;

/// <summary>
/// Configuration storage for Changelog Manager settings.
/// </summary>
[NoireConfig("ChangelogManagerConfig")]
public class ChangelogManagerConfigInstance : NoireConfigBase
{
    /// <inheritdoc/>
    public override int Version { get; set; } = 1;

    /// <inheritdoc/>
    public override string GetConfigFileName() => "ChangelogManagerConfig";

    /// <summary>
    /// The last seen changelog version by the user.
    /// </summary>
    [AutoSave]
    public Version? LastSeenChangelogVersion { get; set; }

    /// <summary>
    /// Updates the last seen changelog version to the specified version.
    /// </summary>
    /// <param name="version">The new version to set as the last seen changelog version. This parameter can be null.</param>
    [AutoSave]
    public void UpdateLastSeenVersion(Version? version) => LastSeenChangelogVersion = version;

    /// <summary>
    /// Clears the last seen changelog version, resetting its value to null.
    /// </summary>
    [AutoSave]
    public void ClearLastSeenVersion() => LastSeenChangelogVersion = null;
}
