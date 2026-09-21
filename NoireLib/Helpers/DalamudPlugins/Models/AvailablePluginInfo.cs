using System;

namespace NoireLib.Helpers;

/// <summary>One plugin a repository offers, whether or not it is installed.</summary>
/// <param name="InternalName">The stable name the repository and the plugin folder are keyed by.</param>
/// <param name="Name">The display name, which can change between releases.</param>
/// <param name="Version">The version on the stable track.</param>
/// <param name="TestingVersion">The version on the testing track, or null when the plugin offers none.</param>
/// <param name="RepositoryUrl">The repository offering it, empty for the official one.</param>
/// <param name="Punchline">The one-line description the repository carries.</param>
public readonly record struct AvailablePluginInfo(
    string InternalName,
    string Name,
    Version? Version,
    Version? TestingVersion,
    string RepositoryUrl,
    string Punchline);
