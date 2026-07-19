using FluentAssertions;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Scene;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the Draw3D math conventions: the row-vector reversed-Z worked example (projection through
/// the viewport transform), inverse round-trips, frustum extraction test vectors, reversed-Z
/// linearization, instance-row layout and sort-key ordering.
/// </summary>
public class Draw3DMathTests
{
    /// <summary>The reference reversed-Z infinite-far projection: FoV 90°, aspect 1, near 0.1, row-vector form.</summary>
    private static readonly Matrix4x4 Proj = new(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 0, 1,
        0, 0, 0.1f, 0);

    private static FrameContext MakeFrame()
    {
        Matrix4x4.Invert(Proj, out var inv).Should().BeTrue();
        return new FrameContext(
            Proj, inv, Matrix4x4.Identity, Proj,
            Vector3.Zero, 0f, new Vector2(1000f, 1000f), Vector2.One,
            reversedZ: true, nearPlane: 0.1f, hasDepth: true, usedFallbackCamera: false, frameId: 1);
    }

    [Fact]
    public void WorkedExample_CenterPoint_ProjectsToScreenCenter()
    {
        var frame = MakeFrame();
        frame.TryWorldToScreen(new Vector3(0, 0, 10), out var screen).Should().BeTrue();
        screen.X.Should().BeApproximately(500f, 0.001f);
        screen.Y.Should().BeApproximately(500f, 0.001f);
    }

    [Fact]
    public void WorkedExample_OffsetPoint_ProjectsToRightOfCenter()
    {
        var frame = MakeFrame();
        frame.TryWorldToScreen(new Vector3(5, 0, 10), out var screen).Should().BeTrue();
        screen.X.Should().BeApproximately(750f, 0.001f);
        screen.Y.Should().BeApproximately(500f, 0.001f);
    }

    [Fact]
    public void WorkedExample_BehindCamera_IsRejected()
    {
        var frame = MakeFrame();
        frame.TryWorldToScreen(new Vector3(0, 0, -10), out _).Should().BeFalse();
    }

    [Fact]
    public void InvViewProj_RoundTripsWorldPositions()
    {
        var frame = MakeFrame();
        var world = new Vector3(3.2f, -1.5f, 25f);
        var clip = Vector4.Transform(new Vector4(world, 1f), frame.ViewProj);
        var ndc = new Vector4(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W, 1f);
        var back = Vector4.Transform(ndc, frame.InvViewProj);
        var result = new Vector3(back.X, back.Y, back.Z) / back.W;
        Vector3.Distance(result, world).Should().BeLessThan(1e-4f);
    }

    [Fact]
    public void ScreenToRay_PointsAtProjectedPixel()
    {
        var frame = MakeFrame();
        var world = new Vector3(5, 2, 30f);
        frame.TryWorldToScreen(world, out var screen).Should().BeTrue();
        frame.TryScreenToRay(screen, out var origin, out var direction).Should().BeTrue();

        // The ray must pass within numerical distance of the original point.
        var toPoint = world - origin;
        var closest = origin + direction * Vector3.Dot(toPoint, direction);
        Vector3.Distance(closest, world).Should().BeLessThan(1e-2f);
    }

    [Fact]
    public void Frustum_PointsAroundTheBoundary_ClassifiedByTheHalfWidth()
    {
        var frustum = FrustumPlanes.FromViewProj(Proj);

        frustum.Intersects(new BoundingSphere(new Vector3(0, 0, 10), 0f)).Should().BeTrue("point on the view axis is inside");
        frustum.Intersects(new BoundingSphere(new Vector3(-11, 0, 10), 0f)).Should().BeFalse("at z=10 the 90° frustum half-width is exactly 10");
        frustum.Intersects(new BoundingSphere(new Vector3(0, 0, 0.05f), 0f)).Should().BeFalse("between eye and near plane");
        frustum.Intersects(new BoundingSphere(new Vector3(-11, 0, 10), 2f)).Should().BeTrue("radius reaches back inside");
    }

    [Fact]
    public void SceneSurfaceW_CalibratedMapping_RoundTripsAndHandlesSky()
    {
        // C# mirror of the Common.hlsli helper: sample = a + b/w  =>  w = b/(sample - a),
        // invalid (sky/clear) when (sample - a) doesn't share b's sign.
        static float SceneSurfaceW(float a, float b, float sample)
        {
            var denom = sample - a;
            return denom * b > 1e-12f ? b / denom : 1e30f;
        }

        // Reversed-Z infinite far (a = 0, b = near): round trip and sky.
        SceneSurfaceW(0f, 0.1f, 0.1f / 12.5f).Should().BeApproximately(12.5f, 1e-3f);
        SceneSurfaceW(0f, 0.1f, 0f).Should().Be(1e30f, "reversed-Z clear value 0 is sky");

        // Standard-Z finite far (near 0.1, far 100): a = far/(far-near), b = -far*near/(far-near).
        const float a2 = 100f / (100f - 0.1f);
        const float b2 = -100f * 0.1f / (100f - 0.1f);
        SceneSurfaceW(a2, b2, a2 + b2 / 7f).Should().BeApproximately(7f, 1e-3f);
        SceneSurfaceW(a2, b2, 1f).Should().BeApproximately(100f, 0.2f, "standard clear value 1 maps to the far plane");
    }

    [Fact]
    public void DecalDeviceZ_FromCalibratedW_MatchesOwnProjection()
    {
        // The decal path converts the calibrated surface w back to OUR device z via
        // deviceZ = M33 + M43/w, which must equal clip.z/clip.w of the same projection.
        var proj = Proj; // the reference worked-example projection
        const float viewZ = 17f;
        var clip = Vector4.Transform(new Vector4(3f, -2f, viewZ, 1f), proj);
        var direct = clip.Z / clip.W;
        var viaMap = proj.M33 + proj.M43 / clip.W;
        viaMap.Should().BeApproximately(direct, 1e-6f);
    }

    [Fact]
    public void InstanceData_RowsAreUntransposedWorldRows()
    {
        var world = Matrix4x4.CreateScale(2f) * Matrix4x4.CreateTranslation(1f, 2f, 3f);
        var data = InstanceData.From(in world, Vector4.One);

        data.W0.Should().Be(new Vector4(world.M11, world.M12, world.M13, world.M14));
        data.W3.Should().Be(new Vector4(world.M41, world.M42, world.M43, world.M44));
        data.W3.X.Should().Be(1f, "translation lives in row 4 of a row-vector matrix");
    }

    [Fact]
    public void SortKey_BucketsDominate()
    {
        var opaque = SortKey.MakeGrouped(0, 0, 10, 500, 100, 5);
        var decal = SortKey.Make(1, 0, 0, 0, 0, 0, backToFront: false);
        var transparent = SortKey.Make(2, 0, 100, 1, 1, 0, backToFront: true);

        opaque.Should().BeLessThan(decal);
        decal.Should().BeLessThan(transparent);
    }

    [Fact]
    public void SortKey_TransparentSortsBackToFront()
    {
        var near = SortKey.Make(2, 0, SortKey.QuantizeDistance(5f), 1, 1, 0, backToFront: true);
        var far = SortKey.Make(2, 0, SortKey.QuantizeDistance(200f), 1, 1, 0, backToFront: true);
        far.Should().BeLessThan(near, "farther items must draw first in the transparent bucket");
    }

    [Fact]
    public void SortKey_GroupedKeysClusterIdenticalMaterials()
    {
        // Two items with the same pipeline+material but wildly different depths must be adjacent
        // (differ only below the depth bits), while a different material lands elsewhere.
        var a = SortKey.MakeGrouped(0, 0, 3, 42, SortKey.QuantizeDistance(1f), 0);
        var b = SortKey.MakeGrouped(0, 0, 3, 42, SortKey.QuantizeDistance(500f), 1);
        var other = SortKey.MakeGrouped(0, 0, 3, 43, SortKey.QuantizeDistance(2f), 2);

        (a >> 30).Should().Be(b >> 30, "same material+pipeline share all bits above depth");
        (a >> 30).Should().NotBe(other >> 30);
    }

    [Fact]
    public void SortKey_LayerOrdersDecals()
    {
        var low = SortKey.Make(1, 0, 0, 0, 0, 7, backToFront: false);
        var high = SortKey.Make(1, 3, 0, 0, 0, 1, backToFront: false);
        low.Should().BeLessThan(high, "higher layers draw later within the decal bucket");
    }

    [Fact]
    public void BoundingSphere_TransformScalesConservatively()
    {
        var sphere = new BoundingSphere(new Vector3(1, 0, 0), 1f);
        var world = Matrix4x4.CreateScale(2f, 1f, 1f) * Matrix4x4.CreateTranslation(0f, 5f, 0f);
        var moved = sphere.Transform(world);

        moved.Center.Should().Be(new Vector3(2f, 5f, 0f));
        moved.Radius.Should().BeApproximately(2f, 1e-5f, "the largest axis scale drives the radius");
    }

    [Fact]
    public void Camera3D_ViewProj_IsReversedZInfiniteFar()
    {
        var camera = new Camera3D(new Vector3(0, 0, -10f), Vector3.Zero, verticalFovRad: MathF.PI / 2f);
        var vp = camera.BuildViewProj(1f);

        // A point right in front of the camera near the near plane gives device depth near 1.
        var nearClip = Vector4.Transform(new Vector4(0, 0, -10f + 0.1001f, 1f), vp);
        (nearClip.Z / nearClip.W).Should().BeGreaterThan(0.99f);

        // A far point gives device depth near 0 (reversed-Z, infinite far).
        var farClip = Vector4.Transform(new Vector4(0, 0, 10000f, 1f), vp);
        (farClip.Z / farClip.W).Should().BeLessThan(0.001f);
    }
}
