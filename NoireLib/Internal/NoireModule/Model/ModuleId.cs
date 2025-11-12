namespace NoireLib.Core.Modules;

/// <summary>
/// Internal class representing a module identifier.<br/>
/// Used in <see cref="NoireLibMain.AddModule{T}(string?)"/> to construct modules with specific IDs.
/// </summary>
public class ModuleId
{
    /// <summary>
    /// The module identifier string.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Creates a new ModuleId instance with the specified identifier.
    /// </summary>
    /// <param name="id">The identifier string.</param>
    public ModuleId(string id)
    {
        Id = id;
    }
}
