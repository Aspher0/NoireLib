using FluentAssertions;
using NoireLib.Draw3D.Core;
using System;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the per-light record layout that a removal experiment established.<br/>
/// The reference values here are the real ones: a lamp was taken out of a room between two captures, one payload
/// vanished with it, and the object's own transform was zeroed in the same capture with its forward axis matching
/// that payload's direction row. Anything that shifts this parse away from those numbers has broken the one part
/// of the light search that rests on evidence rather than on resemblance.
/// </summary>
public class Draw3DGameLightHarvestTests
{
    /// <summary>Builds a 512 B payload from the rows given, zero-filling the rest.</summary>
    private static byte[] Payload(params Vector4[] rows)
    {
        var bytes = new byte[GameLightHarvest.RecordBytes];
        for (var i = 0; i < rows.Length; i++)
        {
            BitConverter.GetBytes(rows[i].X).CopyTo(bytes, i * 16);
            BitConverter.GetBytes(rows[i].Y).CopyTo(bytes, i * 16 + 4);
            BitConverter.GetBytes(rows[i].Z).CopyTo(bytes, i * 16 + 8);
            BitConverter.GetBytes(rows[i].W).CopyTo(bytes, i * 16 + 12);
        }

        return bytes;
    }

    /// <summary>The record of the lamp that was removed, as captured.</summary>
    private static byte[] RemovedLamp()
    {
        var rows = new Vector4[16];
        rows[0] = new Vector4(0.199f, 0.073f, -4.701f, 0f);
        rows[1] = new Vector4(0.288f, -0.081f, 0.954f, 0.001f);
        rows[2] = new Vector4(1.440f, 0.977f, 0.278f, 1f);
        rows[3] = new Vector4(1.440f, 0.977f, 0.278f, 1f);
        rows[13] = new Vector4(0.208f, 0.005f, -0.062f, -0.334f);
        rows[14] = new Vector4(0.000f, 0.216f, 0.018f, 0.071f);
        rows[15] = new Vector4(0.062f, -0.018f, 0.207f, 0.963f);
        return Payload(rows);
    }

    [Fact]
    public void TryParse_TheRemovedLamp_ReadsItsColour()
    {
        GameLightHarvest.TryParse(RemovedLamp(), out var light).Should().BeTrue();

        // Above 1, which is the point: only an emitter carries a colour brighter than white.
        light.Color.X.Should().BeApproximately(1.440f, 0.001f);
        light.Color.Y.Should().BeApproximately(0.977f, 0.001f);
        light.Color.Z.Should().BeApproximately(0.278f, 0.001f);
        light.IsLit.Should().BeTrue();
    }

    [Fact]
    public void TryParse_TheRemovedLamp_ReadsTheObjectForwardAxis()
    {
        // This exact vector is the third column of the transform that was zeroed when the lamp was removed, which
        // is what ties this record to that object rather than to any other.
        GameLightHarvest.TryParse(RemovedLamp(), out var light).Should().BeTrue();

        light.Direction.X.Should().BeApproximately(0.288f, 0.001f);
        light.Direction.Y.Should().BeApproximately(-0.081f, 0.001f);
        light.Direction.Z.Should().BeApproximately(0.954f, 0.001f);
        light.Direction.Length().Should().BeApproximately(1f, 0.01f);
    }

    [Fact]
    public void TryParse_TheRemovedLamp_ReadsRadiusFromTheTransformScale()
    {
        // A light volume is a unit shape scaled to the light's reach, so the reach is the reciprocal of the scale.
        GameLightHarvest.TryParse(RemovedLamp(), out var light).Should().BeTrue();

        light.Radius.Should().BeApproximately(4.606f, 0.01f);
    }

    [Fact]
    public void TryParse_TheRemovedLamp_TransformAgreesWithThePositionRow()
    {
        // Row 0 and the volume transform are two encodings of one point, which is the load-bearing fact behind
        // reading row 0 as the position at all. Inverting the transform lands on (0.207, 0.078, -4.696) against a
        // row 0 of (0.199, 0.073, -4.701). Undoing the rotation is what makes them meet: dividing the translation
        // by the scale alone gives (-1.538, 0.327, 4.436), which is nowhere near.
        GameLightHarvest.TryParse(RemovedLamp(), out var light).Should().BeTrue();

        light.TransformDisagreement.Should().BeLessThan(0.05f);
    }

    [Fact]
    public void TryParse_TheSunAtDay_ReadsAsDirectional()
    {
        // Captured in a room with windows. The same record was black at night and this at midday, with no
        // position and a volume scale of one - which is how a light with nowhere to be says so.
        var rows = new Vector4[16];
        rows[1] = new Vector4(0.382f, 0.862f, 0.332f, 0f);
        rows[2] = new Vector4(1.960f, 1.869f, 1.765f, 1f);
        rows[3] = new Vector4(1.960f, 1.869f, 1.765f, 1f);

        // The volume transform is the identity, which is what yields the reported radius of exactly 1.
        rows[13] = new Vector4(1f, 0f, 0f, 0f);
        rows[14] = new Vector4(0f, 1f, 0f, 0f);
        rows[15] = new Vector4(0f, 0f, 1f, 0f);

        GameLightHarvest.TryParse(Payload(rows), out var light).Should().BeTrue();

        light.IsDirectional.Should().BeTrue();
        light.IsLit.Should().BeTrue();

        // A directional light is nowhere, so moving the camera must not move it.
        light.WorldPosition(new Vector3(100f, 50f, -20f)).Should().Be(Vector3.Zero);
    }

    [Fact]
    public void TryParse_ALamp_IsNotDirectional()
    {
        GameLightHarvest.TryParse(RemovedLamp(), out var light).Should().BeTrue();

        light.IsDirectional.Should().BeFalse();
    }

    [Fact]
    public void WorldPosition_AddsTheCamera()
    {
        // Positions arrive relative to the camera. That the frame is only translated and not rotated is visible
        // in the directions: furniture in a house reads as axis-aligned vectors 90 degrees apart, which a
        // camera-rotated frame would have scrambled.
        GameLightHarvest.TryParse(RemovedLamp(), out var light).Should().BeTrue();

        var world = light.WorldPosition(new Vector3(6f, 2f, -1.5f));

        world.X.Should().BeApproximately(6.199f, 0.001f);
        world.Y.Should().BeApproximately(2.073f, 0.001f);
        world.Z.Should().BeApproximately(-6.201f, 0.001f);
    }

    [Fact]
    public void TryParse_MaterialParameters_AreRejected()
    {
        // These share the 512 B class with the lights and outnumber them. They pass the duplicated-colour test
        // trivially, because whole runs of their rows are (1, 1, 1, 1) - the direction test is what excludes them.
        var materialParams = Payload(
            new Vector4(1f, 1f, 1f, 1f),
            new Vector4(1f, 1f, 1f, 1f),
            new Vector4(1f, 1f, 1f, 1f),
            new Vector4(1f, 1f, 1f, 1f),
            new Vector4(0.3f, 0.3f, 0.3f, 0.3f),
            new Vector4(4f, 4f, 4f, 4f));

        GameLightHarvest.TryParse(materialParams, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_MismatchedColourRows_AreRejected()
    {
        // The game writes the diffuse and specular colours identically. A payload with a unit row 1 that does not
        // do that is something else wearing a similar shape.
        var payload = Payload(
            new Vector4(0.2f, 0.1f, -4.7f, 0f),
            new Vector4(0.288f, -0.081f, 0.954f, 0f),
            new Vector4(1.44f, 0.977f, 0.278f, 1f),
            new Vector4(0.50f, 0.500f, 0.500f, 1f));

        GameLightHarvest.TryParse(payload, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_ShortPayload_IsRejected()
    {
        GameLightHarvest.TryParse(new byte[128], out _).Should().BeFalse();
        GameLightHarvest.TryParse(null!, out _).Should().BeFalse();
    }

    [Fact]
    public void FromPayloads_OrdersByBrightness()
    {
        var dim = Payload(
            Vector4.Zero,
            new Vector4(0f, 1f, 0f, 0f),
            new Vector4(0.2f, 0.2f, 0.2f, 1f),
            new Vector4(0.2f, 0.2f, 0.2f, 1f));

        var bright = Payload(
            Vector4.Zero,
            new Vector4(0f, 1f, 0f, 0f),
            new Vector4(12.8f, 11.7f, 6.9f, 1f),
            new Vector4(12.8f, 11.7f, 6.9f, 1f));

        var lights = GameLightHarvest.FromPayloads([dim, bright]);

        lights.Should().HaveCount(2);
        lights[0].Intensity.Should().BeApproximately(12.8f, 0.01f);
    }

    [Fact]
    public void FromPayloads_KeepsAnUnlitRecord_ButMarksIt()
    {
        // A lamp that is present but contributing nothing is a fact worth keeping: dropping it would make the
        // count disagree with the room, and the count is how this layout gets checked.
        var dark = Payload(
            Vector4.Zero,
            new Vector4(0f, 1f, 0f, 0f),
            new Vector4(0f, 0f, 0f, 1f),
            new Vector4(0f, 0f, 0f, 1f));

        var lights = GameLightHarvest.FromPayloads([dark]);

        lights.Should().HaveCount(1);
        lights[0].IsLit.Should().BeFalse();
    }

    [Fact]
    public void Describe_NoLights_SaysWhichRunWouldHaveThem()
    {
        // A run over the wrong size class cannot contain a light, and an empty report that does not say so reads
        // as though the lights were looked for and found absent.
        var report = GameLightHarvest.Describe([], 40);

        report.Should().Contain("512");
    }
}
