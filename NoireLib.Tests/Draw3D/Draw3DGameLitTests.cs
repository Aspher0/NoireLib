using FluentAssertions;
using NoireLib.Draw3D;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks <see cref="NoireDraw3D.GameLit"/>, the values written into the game's own G-buffer.<br/>
/// These are measurements, not preferences: each one was read back off the game's geometry, and the injection
/// is only correct while it reproduces them. A drive-by edit to any of them should fail here rather than show
/// up in-game as an object shaded as something it is not.
/// </summary>
public class Draw3DGameLitTests
{
    [Fact]
    public void Defaults_AreTheMeasuredValues()
    {
        var options = new Draw3DGameLit();

        // rtv3: red and green are what a paired sample reads on the game's own furniture, blue is a neutral
        // stand-in for a per-pixel term the game varies, alpha reads 1 on both.
        //
        // Red was 65504 here, taken off the target where the geometry pass had not written it rather than off
        // a surface. Sampling the game's copy of a model against ours in one frame is what caught it.
        options.Misc.Should().Be(new Vector4(0f, 0f, 1f, 1f));

        // rtv0's alpha selects the shading model. Furniture and architecture carry this one; characters carry 32.
        options.ShadingModelId.Should().Be(128);

        // rtv1's fallback scalars, sampled per pixel off a real wood floor rather than averaged over a frame -
        // green is discrete (0.396 on floors, 0.780 on walls), so its frame-wide mean is a value no surface holds.
        options.MaterialParams.Should().Be(new Vector3(0.651f, 0.396f, 0f));

        // rtv1's top of range selects a mode rather than a value - red at 0.999 turns the reflection green -
        // and a specular map reaches 1.0 in places, so the channels are held below it by default.
        options.MaterialCeiling.Should().Be(Draw3DGameLit.DefaultMaterialCeiling);
        options.MaterialCeiling.Should().BeLessThan(0.998f, "0.998 is the highest red that does not select the mode");
    }

    [Fact]
    public void Defaults_WriteNoStencilAndNoAlbedoOverride()
    {
        var options = new Draw3DGameLit();

        // The stencil mark is not optional: without it the game's light volumes skip the geometry entirely and
        // the object leaves the lighting pass black, so the everyday path has to carry it.
        options.Stencil.Should().Be(Draw3DGameLit.LitStencilMark);

        // The overrides are levers for identifying a fault rather than part of reproducing the surface, so
        // they must stay off - each one replaces something the material actually authored.
        options.AlbedoOverride.Should().Be(default(Vector4));
        options.MaterialOverride.Should().Be(0f);
    }

    [Fact]
    public void Defaults_WriteBothColorAndDepth()
    {
        var options = new Draw3DGameLit();

        // Both off is a diagnostic pose, not a configuration: an injection that writes neither the targets nor
        // depth has drawn nothing at all, so neither may default off.
        options.WriteColor.Should().BeTrue();
        options.WriteDepth.Should().BeTrue();
    }

    [Fact]
    public void Reset_RestoresEveryMeasuredValue()
    {
        var options = new Draw3DGameLit
        {
            Misc = new Vector4(0f, 0.5f, 0.25f, 0.75f),
            ShadingModelId = Draw3DGameLit.CharacterShadingModelId,
            MaterialParams = Vector3.One,
            MaterialOverride = 1f,
            MaterialCeiling = 1f,
            Stencil = 0,
            AlbedoOverride = new Vector4(1f, 0f, 1f, 1f),
            WriteColor = false,
            WriteDepth = false,
        };

        options.Reset();

        var fresh = new Draw3DGameLit();
        options.Misc.Should().Be(fresh.Misc);
        options.ShadingModelId.Should().Be(fresh.ShadingModelId);
        options.MaterialParams.Should().Be(fresh.MaterialParams);
        options.MaterialOverride.Should().Be(fresh.MaterialOverride);
        options.MaterialCeiling.Should().Be(fresh.MaterialCeiling);
        options.Stencil.Should().Be(fresh.Stencil);
        options.AlbedoOverride.Should().Be(fresh.AlbedoOverride);
        options.WriteColor.Should().Be(fresh.WriteColor);
        options.WriteDepth.Should().Be(fresh.WriteDepth);
    }

    [Fact]
    public void ShadingModelIds_AreDistinct()
    {
        // Writing a character's id gets an object shaded by the skin and hair path instead of as an object in
        // the room, which is why the two are named constants rather than numbers at the write site.
        Draw3DGameLit.WorldShadingModelId.Should().NotBe(Draw3DGameLit.CharacterShadingModelId);
        Draw3DGameLit.MiscRedSentinel.Should().Be(65504f, "it is exactly the largest value a half-float can hold");

        // It is offered for comparison, not written by default: the game's furniture reads 0 in that channel.
        new Draw3DGameLit().Misc.X.Should().NotBe(Draw3DGameLit.MiscRedSentinel);
    }
}
