using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The collection for tests touching NoireLib.Configuration static state: the manager cache, the pending-writer set,
/// the watch's armed list and the save timing knobs.<br/>
/// Parallelization is off for the whole collection. A test class touching that state joins this collection.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConfigStateCollection
{
    /// <summary>
    /// The collection name shared by the definition and its member classes.
    /// </summary>
    public const string Name = "NoireLib.Configuration state";
}
