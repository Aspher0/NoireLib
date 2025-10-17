namespace NoireLib.Core.Modules;

/// <summary>
/// Internal class representing a module identifier.<br/>
/// Used in <see cref="NoireLibMain.AddModule"/> to construct modules with specific IDs.
/// </summary>
public class ModuleId
{
    public string Id { get; }
    public ModuleId(string id)
    {
        Id = id;
    }
}
