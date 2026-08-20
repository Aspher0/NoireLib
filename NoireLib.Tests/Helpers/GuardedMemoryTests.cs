using FluentAssertions;
using NoireLib.Helpers.Memory;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Ported from BypassEmote. Both rules under test are pure, which is the whole point of having them separate
/// from the VirtualQuery call: the decision about whether an address is safe to dereference is the part that
/// can be got wrong, and it is the part that can be pinned without a running process.
/// </summary>
public class GuardedMemoryTests
{
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint PageReadWrite = 0x04;
    private const uint PageExecuteRead = 0x20;
    private const uint PageNoAccess = 0x01;
    private const uint PageGuard = 0x100;

    [Fact]
    public void CommittedReadWrite_IsReadable()
        => GuardedMemory.IsReadableProtection(MemCommit, PageReadWrite).Should().BeTrue();

    [Fact]
    public void CommittedExecuteRead_IsReadable()
        => GuardedMemory.IsReadableProtection(MemCommit, PageExecuteRead).Should().BeTrue();

    [Fact]
    public void ReservedButUncommitted_IsNotReadable()
        => GuardedMemory.IsReadableProtection(MemReserve, PageReadWrite).Should().BeFalse();

    [Fact]
    public void AGuardPage_IsNotReadable()
        => GuardedMemory.IsReadableProtection(MemCommit, PageReadWrite | PageGuard).Should().BeFalse(
            "touching a guard page raises rather than reads");

    [Fact]
    public void NoAccess_IsNotReadable()
        => GuardedMemory.IsReadableProtection(MemCommit, PageNoAccess).Should().BeFalse();

    [Fact]
    public void ZeroProtection_IsNotReadable()
        => GuardedMemory.IsReadableProtection(MemCommit, 0).Should().BeFalse();

    [Fact]
    public void ASpanInsideTheRegion_IsCovered()
        => GuardedMemory.RegionCovers(0x1000, 0x1000, 0x1F00, 0x100).Should().BeTrue();

    [Fact]
    public void ASpanRunningPastTheRegionEnd_IsNotCovered()
        => GuardedMemory.RegionCovers(0x1000, 0x1000, 0x1F80, 0x100).Should().BeFalse(
            "a span that merely starts inside the region is the failure this guards against");

    [Fact]
    public void AnAddressBeforeTheRegion_IsNotCovered()
        => GuardedMemory.RegionCovers(0x1000, 0x1000, 0xFF0, 8).Should().BeFalse();

    [Fact]
    public void AZeroLengthSpan_IsNeverCovered()
        => GuardedMemory.RegionCovers(0x1000, 0x1000, 0x1500, 0).Should().BeFalse();
}
