using FluentAssertions;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Materials;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the ground-decal footprint pick test (<c>NoireDraw3D.InsideDecalShape</c>) against the shape SDF in
/// <c>GroundDecal.hlsl</c>: hovering must match the <i>rendered</i> shape - the hole of a ring and the gap outside a
/// sector's arc must miss, not the whole volume box. Footprint space is <c>p = lp.xz · 2</c>, edge at |p| = 1.
/// </summary>
public class Draw3DDecalPickTests
{
    [Fact]
    public void Ring_MissesTheHole_HitsTheBand_MissesOutside()
    {
        var ring = Material.Telegraph(DecalShape.Ring, Vector4.One, new Vector4(0.5f, 0f, 0f, 0.6f)); // inner ratio 0.5

        NoireDraw3D.InsideDecalShape(ring, Vector3.Zero).Should().BeFalse("the centre is the ring's hole");
        NoireDraw3D.InsideDecalShape(ring, new Vector3(0.375f, 0f, 0f)).Should().BeTrue("r ≈ 0.75 is inside the band");
        NoireDraw3D.InsideDecalShape(ring, new Vector3(0.5f, 0f, 0.5f)).Should().BeFalse("the box corner is past the outer radius");
    }

    [Fact]
    public void Sector_HitsInsideTheArc_MissesOutsideTheArc()
    {
        var sector = Material.Telegraph(DecalShape.Sector, Vector4.One, new Vector4(MathF.PI / 4f, 0f, 0f, 0.55f)); // ±45° about +Z

        NoireDraw3D.InsideDecalShape(sector, new Vector3(0f, 0f, 0.4f)).Should().BeTrue("along +Z, inside the wedge");
        NoireDraw3D.InsideDecalShape(sector, new Vector3(0.4f, 0f, 0f)).Should().BeFalse("along +X is 90° off centre, outside the 45° half-angle");
    }

    [Fact]
    public void Circle_HitsCentre_MissesCorner()
    {
        var circle = Material.Telegraph(DecalShape.Circle, Vector4.One);

        NoireDraw3D.InsideDecalShape(circle, Vector3.Zero).Should().BeTrue();
        NoireDraw3D.InsideDecalShape(circle, new Vector3(0.5f, 0f, 0.5f)).Should().BeFalse("the box corner (|p| = √2) is outside the inscribed disc");
    }
}
