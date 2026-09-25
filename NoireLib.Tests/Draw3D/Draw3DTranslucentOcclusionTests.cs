using FluentAssertions;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Core;
using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Im;
using NoireLib.Draw3D.Materials;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks how Draw3D content chooses between the full scene depth and the opaque-depth snapshot. The game writes its water
/// surface into the scene depth after the opaque pass; a see-through item must test against the copy taken before that.
/// </summary>
public class Draw3DTranslucentOcclusionTests
{
    private const nint Full = 0x1000;
    private const nint Opaque = 0x2000;

    [Fact]
    public void Renderer_DefaultsTo_SeeThrough()
        => NoireDraw3D.TranslucentOcclusion.Should().Be(TranslucentOcclusion.SeeThrough);

    [Fact]
    public void Material_And_ImShapeStyle_FollowTheRendererByDefault()
    {
        Material.Unlit(Vector4.One).TranslucentOcclusion.Should().BeNull();
        new ImShapeStyle().TranslucentOcclusion.Should().BeNull();
        default(ImShapeStyle).TranslucentOcclusion.Should().BeNull();
    }

    [Fact]
    public void SeeThrough_UsesTheSnapshot_WhenOneExists()
    {
        var mat = Data(null);
        bool wanted = false, used = false;

        var srv = ScenePass.OcclusionSrv(in mat, TranslucentOcclusion.SeeThrough, Full, Opaque, ref wanted, ref used);

        srv.Should().Be(Opaque);
        wanted.Should().BeTrue();
        used.Should().BeTrue();
    }

    [Fact]
    public void SeeThrough_WithoutSnapshot_FallsBackToTheFullDepth_AndStillAsksForOne()
    {
        var mat = Data(null);
        bool wanted = false, used = false;

        var srv = ScenePass.OcclusionSrv(in mat, TranslucentOcclusion.SeeThrough, Full, 0, ref wanted, ref used);

        srv.Should().Be(Full);
        wanted.Should().BeTrue("the request is what arms the snapshot for the next frames");
        used.Should().BeFalse();
    }

    [Fact]
    public void Occlude_AlwaysUsesTheFullDepth()
    {
        var mat = Data(null);
        bool wanted = false, used = false;

        var srv = ScenePass.OcclusionSrv(in mat, TranslucentOcclusion.Occlude, Full, Opaque, ref wanted, ref used);

        srv.Should().Be(Full);
        wanted.Should().BeFalse();
        used.Should().BeFalse();
    }

    [Theory]
    [InlineData(TranslucentOcclusion.SeeThrough, TranslucentOcclusion.Occlude, true)]
    [InlineData(TranslucentOcclusion.Occlude, TranslucentOcclusion.SeeThrough, false)]
    public void MaterialOverride_BeatsTheRendererSetting(TranslucentOcclusion material, TranslucentOcclusion renderer, bool expectSnapshot)
    {
        var mat = Data(material);
        bool wanted = false, used = false;

        var srv = ScenePass.OcclusionSrv(in mat, renderer, Full, Opaque, ref wanted, ref used);

        srv.Should().Be(expectSnapshot ? Opaque : Full);
    }

    [Fact]
    public void DepthOff_StaysDepthOff()
    {
        var mat = Data(null);
        bool wanted = false, used = false;

        var srv = ScenePass.OcclusionSrv(in mat, TranslucentOcclusion.SeeThrough, 0, Opaque, ref wanted, ref used);

        srv.Should().Be((nint)0, "a snapshot never stands in for a frame whose game depth is unreadable");
        used.Should().BeFalse();
    }

    [Fact]
    public void Materials_DifferingOnlyInTheOverride_NeverBatchTogether()
    {
        MaterialData.TryFrom(Material.Unlit(Vector4.One), out var follow).Should().BeTrue();
        MaterialData.TryFrom(Material.Unlit(Vector4.One) with { TranslucentOcclusion = TranslucentOcclusion.Occlude }, out var occlude).Should().BeTrue();

        follow.Translucent.Should().BeNull();
        occlude.Translucent.Should().Be(TranslucentOcclusion.Occlude);
        follow.Equals(occlude).Should().BeFalse();
    }

    [Fact]
    public void ConfigureView_ExposesTheSetting()
        => typeof(NoireDraw3D.Draw3DConfig).GetProperty(nameof(NoireDraw3D.Draw3DConfig.TranslucentOcclusion)).Should().NotBeNull();

    private static MaterialData Data(TranslucentOcclusion? translucent)
        => new() { Domain = MaterialDomain.Unlit, Blend = BlendMode.Premultiplied, Depth = DepthMode.TestOnly, Translucent = translucent };
}
