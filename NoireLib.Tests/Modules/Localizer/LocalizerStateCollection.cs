using Xunit;

namespace NoireLib.Tests;

/// <summary>Serializes the tests touching the localizer's process-wide state: the declared-text localizer, registry and config.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalizerStateCollection
{
    /// <summary>The collection name shared by the definition and its member classes.</summary>
    public const string Name = "NoireLib.Localizer state";
}
