using FluentAssertions;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Enums;
using System;
using System.Linq;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the parts of the config surface that need no GPU: the native-UI knobs collected under
/// <see cref="NoireDraw3D.NativeUi"/>, and the <see cref="NoireDraw3D.Draw3DConfig"/> view surfacing render +
/// interaction config.
/// </summary>
public class Draw3DConfigTests
{
    [Fact]
    public void NativeUi_DefaultsTo_UnderGameUi_WithDepthAwareNameplates()
    {
        // The defaults are the whole contract: the layer reads under the game's UI, and the game's nameplates are
        // occluded by 3D objects in front of them. Nothing needs configuring to get correct layering.
        // Read-only on purpose: assigning Layering arms the render-thread injection, which has no device here.
        NoireDraw3D.NativeUi.Layering.Should().Be(Draw3DLayering.UnderGameUi);
        NoireDraw3D.NativeUi.Nameplates.Should().Be(NameplateOcclusion.DepthAware);
    }

    [Fact]
    public void NativeUi_Nameplates_RoundTrips()
    {
        var original = NoireDraw3D.NativeUi.Nameplates;
        try
        {
            NoireDraw3D.NativeUi.Nameplates = NameplateOcclusion.AlwaysVisible;
            NoireDraw3D.NativeUi.Nameplates.Should().Be(NameplateOcclusion.AlwaysVisible);

            NoireDraw3D.NativeUi.Nameplates = NameplateOcclusion.DepthAware;
            NoireDraw3D.NativeUi.Nameplates.Should().Be(NameplateOcclusion.DepthAware);
        }
        finally
        {
            NoireDraw3D.NativeUi.Nameplates = original;
        }
    }

    [Fact]
    public void NativeUi_ExposesTheFourLayeringKnobs()
    {
        typeof(NoireDraw3D.NativeUiConfig).GetProperties()
            .Select(p => p.Name)
            .Should().BeEquivalentTo("Layering", "KeepUiOnTop", "Nameplates", "NameplateDim");
    }

    [Fact]
    public void NameplateOcclusion_Covered_ExistsForOverEverythingOnly()
    {
        // Covered is the one mode the under-UI path cannot express: the game draws the plates after the layer
        // composites, so nothing can paint over them there. It exists because OverEverything CAN express it,
        // which is the whole reason that path keeps its own masking rather than deferring to the injection.
        Enum.IsDefined(NameplateOcclusion.Covered).Should().BeTrue();
    }

    [Fact]
    public void Draw3DConfig_View_ExposesNativeUiLightingAndInteraction()
    {
        typeof(NoireDraw3D.Draw3DConfig).GetProperty("NativeUi").Should().NotBeNull();
        typeof(NoireDraw3D.Draw3DConfig).GetProperty("Lighting").Should().NotBeNull();
        typeof(NoireDraw3D.Draw3DConfig).GetProperty("Interaction").Should().NotBeNull();
    }
}
