using FluentAssertions;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the surface that needs no GPU: Material.Decal and
/// Material.Custom, the dev-owned decal exclusion collector wiring, and the safe no-op of ShowOutline with no renderer.
/// </summary>
public class Draw3DMaterialExclusionTests
{
    [Fact]
    public void Decal_BuildsGroundDecalMaterial()
    {
        var mat = Material.Decal(DecalShape.Ring, new Vector4(1f, 0.5f, 0f, 0.9f), outlineWidth: 0.1f);
        mat.Domain.Should().Be(MaterialDomain.GroundDecal);
        mat.Shape.Should().Be(DecalShape.Ring);
        mat.OutlineWidth.Should().Be(0.1f);
        mat.Cull.Should().Be(CullMode.Front);
    }

    [Fact]
    public void Custom_SetsPipelineAndColor()
    {
        var mat = Material.Custom("hologram", new Vector4(0f, 1f, 1f, 1f), BlendMode.Additive);
        mat.CustomPipeline.Should().Be("hologram");
        mat.Color.Should().Be(new Vector4(0f, 1f, 1f, 1f));
        mat.Blend.Should().Be(BlendMode.Additive);
    }

    [Fact]
    public void Resolve_CarriesSurfaceParams_ForCustomPipelines()
    {
        var surface = new Vector4(0.25f, 0.5f, 0.75f, 1f);
        var mat = Material.Custom("hologram", Vector4.One) with { SurfaceParams = surface };

        MaterialData.TryFrom(mat, out var data).Should().BeTrue();
        data.SurfaceParams.Should().Be(surface);
    }

    /// <summary>
    /// Surface parameters take part in the batching key. Without this, two materials that shade differently
    /// would compare equal and the second would be drawn with the first one's constants.
    /// </summary>
    [Fact]
    public void Resolve_SurfaceParams_ParticipateInMaterialIdentity()
    {
        var plain = Material.Custom("hologram", Vector4.One);
        var shaded = plain with { SurfaceParams = new Vector4(1f, 0f, 0f, 0f) };

        MaterialData.TryFrom(plain, out var a).Should().BeTrue();
        MaterialData.TryFrom(shaded, out var b).Should().BeTrue();

        a.Equals(b).Should().BeFalse(because: "materials that shade differently must not batch together");

        MaterialData.TryFrom(plain, out var again).Should().BeTrue();
        a.Equals(again).Should().BeTrue(because: "the same material must resolve to the same batching key");
        a.GetHashCode().Should().Be(again.GetHashCode());
    }

    [Fact]
    public void ExcludeObjects_RegistersACollector_ThatIsSafeHeadless()
    {
        var scene = new Scene3D("t");
        var node = scene.CreateNode();

        node.ExcludeObjects(o => true);
        node.ExclusionCollector.Should().NotBeNull();
        // Object table unavailable in tests, so the collector returns an empty set rather than throwing.
        node.ExclusionCollector!().Should().BeEmpty();
    }

    [Fact]
    public void ExcludeVolumes_StaticList_ClearsTheDynamicCollector()
    {
        var scene = new Scene3D("t");
        var node = scene.CreateNode();

        node.ExcludeObjects(o => true);
        node.ExclusionCollector.Should().NotBeNull();

        node.ExcludeVolumes(new List<ExcludeVolume>());
        node.ExclusionCollector.Should().BeNull("a static volume list needs no per-frame recompute");
    }

    [Fact]
    public void ExcludeVolumes_Collector_ThenClear()
    {
        var scene = new Scene3D("t");
        var node = scene.CreateNode();

        node.ExcludeVolumes(() => new List<ExcludeVolume>());
        node.ExclusionCollector.Should().NotBeNull();

        node.ClearExclusions();
        node.ExclusionCollector.Should().BeNull();
    }

    [Fact]
    public void ExclusionApi_IsFluent()
    {
        var scene = new Scene3D("t");
        var node = scene.CreateNode();
        node.ExcludeObjects(o => true).Should().BeSameAs(node);
        node.ClearExclusions().Should().BeSameAs(node);
    }

    [Fact]
    public void ShowOutline_WithNoRenderer_IsSafeNoOp()
    {
        var scene = new Scene3D("t");
        var node = scene.CreateNode();

        node.ShowOutline(new Vector4(1f, 1f, 1f, 1f));
        node.HasOutline.Should().BeFalse("with no renderer there is no mesh to hull; ShowOutline is a logged no-op");

        var act = node.HideOutline;
        act.Should().NotThrow();
    }
}
