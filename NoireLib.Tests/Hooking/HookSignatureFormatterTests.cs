using FluentAssertions;
using NoireLib.Hooking;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the signature rendering against the exact text Dalamud's hook verification prints, so a report
/// from either side can be compared against the other without translating it first.
/// </summary>
public sealed class HookSignatureFormatterTests
{
    private delegate bool ExecuteCommandDelegate(int command, int first, int second, int third, int fourth);

    private unsafe delegate bool ExecuteLocationCommandDelegate(int command, Vector3* location, int first, int second, int third, int fourth);

    private delegate void MismatchedExecuteCommandDelegate(uint command, uint first, uint second, uint third, uint fourth);

    private delegate void EmptyDelegate();

    [Fact]
    public void Format_RendersReturnTypeThenParameters()
        => HookSignatureFormatter.Format(typeof(ExecuteCommandDelegate))
            .Should().Be("Boolean (Int32, Int32, Int32, Int32, Int32)");

    [Fact]
    public void Format_RendersAPointerParameterWithItsStar()
        => HookSignatureFormatter.Format(typeof(ExecuteLocationCommandDelegate))
            .Should().Be("Boolean (Int32, Vector3*, Int32, Int32, Int32, Int32)");

    [Fact]
    public void Format_RendersAVoidReturnAndUnsignedParameters()
        => HookSignatureFormatter.Format(typeof(MismatchedExecuteCommandDelegate))
            .Should().Be("Void (UInt32, UInt32, UInt32, UInt32, UInt32)");

    [Fact]
    public void Format_RendersAnEmptyParameterList()
        => HookSignatureFormatter.Format(typeof(EmptyDelegate)).Should().Be("Void ()");

    [Fact]
    public void Format_FallsBackToTheTypeNameWhenTheTypeIsNotADelegate()
        => HookSignatureFormatter.Format(typeof(int)).Should().Be("Int32");

    [Fact]
    public void FormatAddress_ReportsAnUnresolvedAddressRatherThanZero()
        => HookSignatureFormatter.FormatAddress(0).Should().Be("unresolved");

    [Fact]
    public void FormatAddress_WritesAnAddressOutsideEveryModuleInHex()
        => HookSignatureFormatter.FormatAddress(0x10).Should().Be("0x10");
}
