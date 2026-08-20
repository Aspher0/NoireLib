using FluentAssertions;
using NoireLib.Hooking;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the delegate comparison that decides whether a hook is allowed to exist. The case that motivated it
/// is the last one here: a detour written with a void return against a function that returns a value leaves the
/// caller reading a register nobody wrote.
/// </summary>
public sealed class ClientStructsIndexTests
{
    private delegate bool ExecuteCommandDelegate(int command, int first, int second, int third, int fourth);

    private delegate void VoidExecuteCommandDelegate(uint command, uint first, uint second, uint third, uint fourth);

    private delegate bool UnsignedExecuteCommandDelegate(uint command, uint first, uint second, uint third, uint fourth);

    private delegate bool ShortExecuteCommandDelegate(int command, int first);

    private unsafe delegate bool PointerLocationDelegate(int command, Vector3* location, int first);

    private delegate bool NativeIntLocationDelegate(int command, nint location, int first);

    private delegate bool NarrowLocationDelegate(int command, int location, int first);

    private delegate bool UnsignedLongLocationDelegate(int command, ulong location, int first);

    private delegate float FloatReturnDelegate(int value);

    private delegate int IntReturnDelegate(int value);

    [Fact]
    public void CompareDelegates_AcceptsAnIdenticalSignature()
        => ClientStructsIndex.CompareDelegates(typeof(ExecuteCommandDelegate), typeof(ExecuteCommandDelegate), strict: false)
            .Should().BeNull();

    [Fact]
    public void CompareDelegates_RejectsAVoidReturnAgainstAValueReturn()
        => ClientStructsIndex.CompareDelegates(typeof(VoidExecuteCommandDelegate), typeof(ExecuteCommandDelegate), strict: false)
            .Should().Be("return type is Void, expected Boolean");

    [Fact]
    public void CompareDelegates_RejectsAParameterCountThatDoesNotLineUp()
        => ClientStructsIndex.CompareDelegates(typeof(ShortExecuteCommandDelegate), typeof(ExecuteCommandDelegate), strict: false)
            .Should().Be("takes 2 parameters, expected 5");

    [Fact]
    public void CompareDelegates_AcceptsSignednessDifferencesBecauseTheyPassIdentically()
        => ClientStructsIndex.CompareDelegates(typeof(UnsignedExecuteCommandDelegate), typeof(ExecuteCommandDelegate), strict: false)
            .Should().BeNull();

    /// <summary>
    /// A handle or address written as <c>ulong</c> is how plenty of existing detours are declared, and on x64 it is
    /// passed in the same register as a pointer, so reporting it would be a false alarm.
    /// </summary>
    [Fact]
    public void CompareDelegates_AcceptsAPointerWrittenAsAnUnsignedLong()
        => ClientStructsIndex.CompareDelegates(typeof(UnsignedLongLocationDelegate), typeof(PointerLocationDelegate), strict: false)
            .Should().BeNull();

    [Fact]
    public void CompareDelegates_AcceptsAPointerWrittenAsANativeInteger()
        => ClientStructsIndex.CompareDelegates(typeof(NativeIntLocationDelegate), typeof(PointerLocationDelegate), strict: false)
            .Should().BeNull();

    [Fact]
    public void CompareDelegates_RejectsAPointerWrittenAsAFourByteInteger()
        => ClientStructsIndex.CompareDelegates(typeof(NarrowLocationDelegate), typeof(PointerLocationDelegate), strict: false)
            .Should().Be("parameter 2 is Int32, expected Vector3*");

    [Fact]
    public void CompareDelegates_RejectsAFloatAgainstAnIntegerBecauseTheyUseDifferentRegisters()
        => ClientStructsIndex.CompareDelegates(typeof(FloatReturnDelegate), typeof(IntReturnDelegate), strict: false)
            .Should().Be("return type is Single, expected Int32");

    [Fact]
    public void CompareDelegates_UnderStrictComparisonRejectsSignednessDifferences()
        => ClientStructsIndex.CompareDelegates(typeof(UnsignedExecuteCommandDelegate), typeof(ExecuteCommandDelegate), strict: true)
            .Should().Be("parameter 1 is UInt32, expected Int32");

    [Fact]
    public void CompareDelegates_UnderStrictComparisonRejectsAPointerWrittenAsANativeInteger()
        => ClientStructsIndex.CompareDelegates(typeof(NativeIntLocationDelegate), typeof(PointerLocationDelegate), strict: true)
            .Should().Be("parameter 2 is IntPtr, expected Vector3*");

    /// <summary>
    /// Pairs a detour reads identically, so reporting one would be a false alarm on correct code.
    /// </summary>
    [Theory]
    [InlineData(typeof(nint), typeof(ulong))]
    [InlineData(typeof(nuint), typeof(long))]
    [InlineData(typeof(bool), typeof(byte))]
    [InlineData(typeof(short), typeof(char))]
    [InlineData(typeof(int), typeof(uint))]
    public void TypesAgree_AcceptsTypesADetourReadsIdentically(System.Type passed, System.Type expected)
        => ClientStructsIndex.TypesAgree(passed, expected, strict: false).Should().BeTrue();

    /// <summary>
    /// Pairs where the detour would read something other than what the game wrote: a truncated address, a
    /// register nobody set, or bytes past the end of the value.
    /// </summary>
    [Theory]
    [InlineData(typeof(int), typeof(nint))]
    [InlineData(typeof(float), typeof(int))]
    [InlineData(typeof(double), typeof(long))]
    [InlineData(typeof(byte), typeof(int))]
    [InlineData(typeof(short), typeof(long))]
    [InlineData(typeof(void), typeof(bool))]
    public void TypesAgree_RejectsTypesADetourWouldReadDifferently(System.Type passed, System.Type expected)
        => ClientStructsIndex.TypesAgree(passed, expected, strict: false).Should().BeFalse();

    [Fact]
    public void TypesAgree_ReadsAnEnumAsItsUnderlyingType()
        => ClientStructsIndex.TypesAgree(typeof(SampleEnum), typeof(byte), strict: false).Should().BeTrue();

    /// <summary>
    /// Two different structs are both aggregates, and that grouping says nothing about their layout, so agreeing
    /// on the group alone would wave through a genuinely different argument.
    /// </summary>
    [Fact]
    public void TypesAgree_RejectsTwoDifferentStructsEvenThoughBothAreAggregates()
        => ClientStructsIndex.TypesAgree(typeof(SampleStruct), typeof(OtherStruct), strict: false)
            .Should().BeFalse();

    [Fact]
    public void TypesAgree_AcceptsTheSameStruct()
        => ClientStructsIndex.TypesAgree(typeof(SampleStruct), typeof(SampleStruct), strict: false)
            .Should().BeTrue();

    private enum SampleEnum : byte
    {
        None = 0,
    }

    private struct SampleStruct
    {
        public int Value;
    }

    private struct OtherStruct
    {
        public int Value;
    }
}
