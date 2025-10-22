namespace NoireLib.UpdateTracker;

/// <summary>
/// Represents a JSON plugin repository entry.
/// </summary>
public class RepoEntry
{
    public string? Author { get; set; }
    public string? Name { get; set; }
    public string? InternalName { get; set; }
    public string? AssemblyVersion { get; set; }
}
