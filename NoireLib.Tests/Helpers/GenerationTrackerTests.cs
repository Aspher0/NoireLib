using FluentAssertions;
using NoireLib.Helpers;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Ported from BypassEmote, where this arbitrated overlapping emote-swap builds. The contract is the same
/// wherever several async runs mutate one shared resource: the newest run owns it, a superseded run must be
/// able to tell, and a run that applied nothing has to hand the claim back.
/// </summary>
public class GenerationTrackerTests
{
    [Fact]
    public void ASoleRun_OwnsItsOwnCleanup()
    {
        var generations = new GenerationTracker();

        var generation = generations.TakeOwnership();

        generations.IsCurrent(generation).Should().BeTrue();
    }

    [Fact]
    public void TheLatestRun_Owns()
    {
        var generations = new GenerationTracker();

        generations.TakeOwnership();
        var generation = generations.TakeOwnership();

        generations.IsCurrent(generation).Should().BeTrue();
    }

    [Fact]
    public void ASupersededRun_NoLongerOwns()
    {
        var generations = new GenerationTracker();

        var first = generations.TakeOwnership();
        var second = generations.TakeOwnership();

        generations.IsCurrent(first).Should().BeFalse();
        generations.IsCurrent(second).Should().BeTrue();
    }

    [Fact]
    public void Relinquish_ByTheCurrentOwner_HandsTheClaimBack()
    {
        var generations = new GenerationTracker();

        var first = generations.TakeOwnership();
        var second = generations.TakeOwnership();

        generations.Relinquish(second).Should().BeTrue();
        generations.IsCurrent(first).Should().BeTrue("the older run is current again once the newer one bows out");
    }

    [Fact]
    public void Relinquish_ByASupersededRun_ChangesNothing()
    {
        var generations = new GenerationTracker();

        var first = generations.TakeOwnership();
        var second = generations.TakeOwnership();

        generations.Relinquish(first).Should().BeFalse();
        generations.IsCurrent(second).Should().BeTrue("a run that is already superseded has nothing left to hand back");
    }

    [Fact]
    public void Current_TracksTheLatestClaim()
    {
        var generations = new GenerationTracker();

        generations.Current.Should().Be(0);

        var first = generations.TakeOwnership();
        generations.Current.Should().Be(first);

        var second = generations.TakeOwnership();
        generations.Current.Should().Be(second);

        generations.Relinquish(second);
        generations.Current.Should().Be(first);
    }
}
