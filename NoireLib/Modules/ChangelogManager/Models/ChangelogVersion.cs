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

    /// <summary>
    /// The version number of this changelog entry.
    /// </summary>
    public required Version Version
    {
        get => version;
        init => version = NormalizeVersion(value);
    }

    /// <summary>
    /// A string representation of the release date.
    /// </summary>
    public required string Date { get; init; }

    /// <summary>
    /// The list of changelog entries for this version.
    /// </summary>
    public required List<ChangelogEntry> Entries { get; init; }

    /// <summary>
    /// The title of this version's changelog entry. Optional.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The color of the <see cref="Title"/> in RGBA format. Optional.
    /// </summary>
    public Vector4? TitleColor { get; init; }

    /// <summary>
    /// The description of this version's changelog entry. Optional.
    /// </summary>
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
