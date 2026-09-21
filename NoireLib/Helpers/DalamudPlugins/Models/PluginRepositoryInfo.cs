namespace NoireLib.Helpers;

/// <summary>One plugin repository Dalamud is configured with.</summary>
/// <param name="Url">The repository's plugin master URL, its key.</param>
/// <param name="IsEnabled">Whether Dalamud reads it.</param>
/// <param name="IsThirdParty">False for the official repository.</param>
public readonly record struct PluginRepositoryInfo(string Url, bool IsEnabled, bool IsThirdParty);
