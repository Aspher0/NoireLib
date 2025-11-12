namespace NoireLib.UpdateTracker;

/// <summary>
/// Represents a JSON plugin repository entry.
/// </summary>
public class RepoEntry
{
    /// <summary>
    /// The author of the plugin in the json repo entry.
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// The name of the plugin in the json repo entry.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The internal name of the plugin in the json repo entry.
    /// </summary>
    public string? InternalName { get; set; }

    /// <summary>
    /// The assembly version of the plugin in the json repo entry.
    /// </summary>
    public string? AssemblyVersion { get; set; }
}
