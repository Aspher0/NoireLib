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

        // rtv3: red pinned to the half-float ceiling on world geometry, green zero everywhere, blue and alpha
        // at the top of the ranges the game uses (their meanings are unmeasured).
        options.Misc.Should().Be(new Vector4(65504f, 0f, 1f, 1f));

        // rtv0's alpha selects the shading model. Furniture and architecture carry this one; characters carry 32.
        options.ShadingModelId.Should().Be(128);

        // rtv1's fallback scalars, sampled per pixel off a real wood floor rather than averaged over a frame -
        // green is discrete (0.396 on floors, 0.780 on walls), so its frame-wide mean is a value no surface holds.
        options.MaterialParams.Should().Be(new Vector3(0.651f, 0.396f, 0f));
    }

    [Fact]
    public void Defaults_WriteNoStencilAndNoAlbedoOverride()
    {
        var options = new Draw3DGameLit();

        // These are levers for identifying a fault, not part of reproducing the surface, so the everyday path
        // must not carry them: a stencil value invents a category, and the two overrides replace what the
        // material actually authored.
        options.Stencil.Should().Be(0u);
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
            Stencil = 0x08,
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
    }
}
