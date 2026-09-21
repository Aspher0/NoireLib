namespace NoireLib.Helpers;

/// <summary>
/// Which copy of a plugin an operation acts on. The same plugin can be installed twice at once, once from a
/// repository and once from a dev directory, and the two are separate installations Dalamud tracks apart.
/// </summary>
public enum PluginSource
{
    /// <summary>Either copy, preferring the one installed from a repository when both are present.</summary>
    Any,

    /// <summary>Only the copy installed from a repository.</summary>
    Repository,

    /// <summary>Only the copy loaded from a dev directory.</summary>
    Dev,
}
