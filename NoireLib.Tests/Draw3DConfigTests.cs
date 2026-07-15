using FluentAssertions;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Enums;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the Stage-6 config grouping that needs no GPU: the native-UI knobs collected under
/// <see cref="NoireDraw3D.NativeUi"/>, their obsolete flat-property mirrors reading/writing the same storage, and the
/// <see cref="NoireDraw3D.Draw3DConfig"/> view surfacing render + interaction config (Q6).
/// </summary>
public class Draw3DConfigTests
{
    [Fact]
    public void NativeUi_Protect_MirrorsObsoleteFlatProperty()
    {
        var original = NoireDraw3D.NativeUi.Protect;
        try
        {
            NoireDraw3D.NativeUi.Protect = false;
#pragma warning disable CS0618 // exercising the deprecated forwarder on purpose
            NoireDraw3D.ProtectGameUi.Should().BeFalse("the obsolete flat property reads the grouped value");
            NoireDraw3D.ProtectGameUi = true;
#pragma warning restore CS0618
            NoireDraw3D.NativeUi.Protect.Should().BeTrue("the grouped property reads the obsolete setter's value");
        }
        finally
        {
            NoireDraw3D.NativeUi.Protect = original;
        }
    }

    [Fact]
    public void NativeUi_Protection_MirrorsObsoleteFlatProperty()
    {
        var original = NoireDraw3D.NativeUi.Protection;
        try
        {
            NoireDraw3D.NativeUi.Protection = NativeUiProtectionMode.AlwaysVisible;
#pragma warning disable CS0618
            NoireDraw3D.NativeUiProtection.Should().Be(NativeUiProtectionMode.AlwaysVisible);
#pragma warning restore CS0618
        }
        finally
        {
            NoireDraw3D.NativeUi.Protection = original;
        }
    }

    [Fact]
    public void NativeUi_DimFactor_MirrorsObsoleteFlatProperty()
    {
        var original = NoireDraw3D.NativeUi.DimFactor;
        try
        {
            NoireDraw3D.NativeUi.DimFactor = 0.42f;
#pragma warning disable CS0618
            NoireDraw3D.NativeUiProtectionDimFactor.Should().Be(0.42f);
#pragma warning restore CS0618
        }
        finally
        {
            NoireDraw3D.NativeUi.DimFactor = original;
        }
    }

    [Fact]
    public void NativeUi_DepthWrite_MirrorsObsoleteFlatProperty()
    {
        var original = NoireDraw3D.NativeUi.DepthWrite;
        try
        {
            NoireDraw3D.NativeUi.DepthWrite = false;
#pragma warning disable CS0618
            NoireDraw3D.NativeUiDepthWrite.Should().BeFalse();
#pragma warning restore CS0618
        }
        finally
        {
            NoireDraw3D.NativeUi.DepthWrite = original;
        }
    }

    [Fact]
    public void Draw3DConfig_View_ExposesNativeUiLightingAndInteraction()
    {
        // Q6: the Configure view surfaces interaction config alongside render config, without a live device.
        typeof(NoireDraw3D.Draw3DConfig).GetProperty("NativeUi").Should().NotBeNull();
        typeof(NoireDraw3D.Draw3DConfig).GetProperty("Lighting").Should().NotBeNull();
        typeof(NoireDraw3D.Draw3DConfig).GetProperty("Interaction").Should().NotBeNull();
    }
}
