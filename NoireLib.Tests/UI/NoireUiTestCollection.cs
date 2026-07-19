using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Groups every NoireUI test class into a single non-parallel collection.<br/>
/// The hub, the transient state store and the animation clock are process-wide statics by design (NoireLib is compiled
/// into each plugin rather than shared), so two test classes driving them at once would see each other's writes.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class NoireUiTestCollection
{
    /// <summary>The collection name to put on a NoireUI test class.</summary>
    public const string Name = "NoireUI";
}
