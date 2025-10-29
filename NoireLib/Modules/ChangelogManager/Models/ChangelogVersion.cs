using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Changelog;

/// <summary>
/// Contains information about a specific version in the changelog.
/// </summary>
public record ChangelogVersion
{
    private Version version = null!;

    public required Version Version
    {
        get => version;
        init => version = NormalizeVersion(value);
    }

    public required string Date { get; init; }
    public required List<ChangelogEntry> Entries { get; init; }
    public string? Title { get; init; }
    public Vector4? TitleColor { get; init; }
    public string? Description { get; init; }

    /// <summary>
    /// Normalizes a Version object to always have 4 components (Major.Minor.Build.Revision).
    /// </summary>
    private static Version NormalizeVersion(Version v)
    {
        return new Version(
            v.Major,
            v.Minor,
            v.Build >= 0 ? v.Build : 0,
            v.Revision >= 0 ? v.Revision : 0
        );
    }
}
