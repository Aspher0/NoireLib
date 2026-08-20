using FluentAssertions;
using NoireLib.Hooking;
using System;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks what each target records, so a hook built from any address source resolves through the branch it named.
/// </summary>
public sealed class HookTargetTests
{
    private delegate int SampleDelegate(int value);

    [Fact]
    public void ClientStructs_RecordsTheDelegateItResolvesFrom()
    {
        var target = HookTarget.ClientStructs<SampleDelegate>();

        target.Kind.Should().Be(HookTargetKind.ClientStructs);
        target.DelegateType.Should().Be(typeof(SampleDelegate));
    }

    [Fact]
    public void Address_RecordsThePointer()
    {
        var target = HookTarget.Address(0x1234);

        target.Kind.Should().Be(HookTargetKind.Address);
        target.Pointer.Should().Be(0x1234);
        target.Describe().Should().Be("address 0x1234");
    }

    [Fact]
    public void Address_RefusesZeroRatherThanFailingLaterInsideDalamud()
    {
        var act = () => HookTarget.Address(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Vtable_RecordsTheTableAndSlot()
    {
        var target = HookTarget.Vtable(0x1000, 7);

        target.Kind.Should().Be(HookTargetKind.Vtable);
        target.Pointer.Should().Be(0x1000);
        target.VtableSlot.Should().Be(7);
        target.Describe().Should().Be("vtable 0x1000 slot 7");
    }

    [Fact]
    public void Vtable_RefusesANegativeSlot()
    {
        var act = () => HookTarget.Vtable(0x1000, -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Symbol_RecordsTheModuleAndExport()
    {
        var target = HookTarget.Symbol("kernel32.dll", "LoadLibraryW");

        target.Kind.Should().Be(HookTargetKind.Symbol);
        target.ModuleName.Should().Be("kernel32.dll");
        target.ExportName.Should().Be("LoadLibraryW");
    }

    [Fact]
    public void Signature_KeepsTheBytesItWillScanFor()
    {
        var signature = HookTarget.Signature("E8 ?? ?? ?? ?? 66 41 89 B7");

        signature.Kind.Should().Be(HookTargetKind.Signature);
        signature.SignatureText.Should().Be("E8 ?? ?? ?? ?? 66 41 89 B7");
    }

    [Fact]
    public void Deferred_KeepsTheResolverSoItCanBeRetried()
    {
        var calls = 0;
        var target = HookTarget.Deferred(() =>
        {
            calls++;
            return calls < 2 ? 0 : 0x5000;
        });

        target.Kind.Should().Be(HookTargetKind.Deferred);
        target.Resolver!().Should().Be(0);
        target.Resolver!().Should().Be(0x5000);
    }

    [Fact]
    public void Describe_NamesEveryKind()
    {
        HookTarget.Symbol("kernel32.dll", "LoadLibraryW").Describe().Should().Be("symbol kernel32.dll!LoadLibraryW");
        HookTarget.FunctionPointerVariable(0x40).Describe().Should().Be("function pointer variable 0x40");
        HookTarget.Import(null, "kernel32.dll", "LoadLibraryW").Describe().Should().Be("import kernel32.dll!LoadLibraryW");
        HookTarget.Deferred(static () => 0).Describe().Should().Be("deferred resolver");
        HookTarget.Signature("48 89").Describe().Should().Be("signature 48 89");
    }
}
