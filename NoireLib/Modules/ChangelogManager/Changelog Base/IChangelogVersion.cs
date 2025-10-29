using System.Collections.Generic;

namespace NoireLib.Changelog;

/// <summary>
/// Interface for changelog version files.
/// </summary>
public interface IChangelogVersion
{
    /// <summary>
    /// Gets the changelog versions data.
    /// </summary>
    List<ChangelogVersion> GetVersions();
}
