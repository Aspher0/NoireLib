using FluentAssertions;
using NoireLib.Draw3D.Core;
using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the constant-buffer classifier the light search depends on. Its job is to narrow a frame buffer to
/// the rows that could carry a light; a classifier that admits everything, or that rejects a real light
/// direction, makes the search useless in opposite ways.
/// </summary>
public class Draw3DLightConstantProbeTests
{
    [Fact]
    public void Classify_NormalizedDirection_IsAUnitVector()
    {
        var down = Vector3.Normalize(new Vector3(0.3f, -0.9f, 0.2f));
        var kind = LightConstantProbe.Classify(new Vector4(down, 1f));

        kind.Should().HaveFlag(RowKind.UnitVector, because: "a light direction arrives normalized");
    }

    [Fact]
    public void Classify_ArbitraryVector_IsNotAUnitVector()
    {
        LightConstantProbe.Classify(new Vector4(0.3f, 0.4f, 0.9f, 0f))
            .Should().NotHaveFlag(RowKind.UnitVector, because: "a vector of length 1.03 is not a direction");
    }

    [Fact]
    public void Classify_Color_IsColorLikeAndNormalized()
    {
        var kind = LightConstantProbe.Classify(new Vector4(0.98f, 0.87f, 0.72f, 1f));

        kind.Should().HaveFlag(RowKind.ColorLike);
        kind.Should().HaveFlag(RowKind.Normalized);
    }

    [Fact]
    public void Classify_NegativeComponents_AreNotColorLike()
    {
        LightConstantProbe.Classify(new Vector4(0.5f, -0.2f, 0.5f, 1f))
            .Should().NotHaveFlag(RowKind.ColorLike, because: "no light has a negative channel");
    }

    [Fact]
    public void Classify_WorldPosition_IsLarge()
    {
        LightConstantProbe.Classify(new Vector4(120.5f, 8.25f, -430.75f, 1f))
            .Should().HaveFlag(RowKind.Large, because: "positions and matrix entries must be excluded from the light search");
    }

    [Fact]
    public void Classify_Zero_IsZero()
        => LightConstantProbe.Classify(Vector4.Zero).Should().Be(RowKind.Zero);

    [Fact]
    public void Classify_NotFinite_IsRejected()
        => LightConstantProbe.Classify(new Vector4(float.NaN, 0f, 0f, 0f)).Should().Be(RowKind.None);

    [Fact]
    public void Classify_ReadsRowsAtSixteenByteOffsets()
    {
        var bytes = new byte[32];
        Write(bytes, 0, 1f, 2f, 3f, 4f);
        Write(bytes, 16, 0.25f, 0.5f, 0.75f, 1f);

        var snapshot = LightConstantProbe.Classify(0x1234, bytes, bytes.Length);

        snapshot.Rows.Should().HaveCount(2);
        snapshot.Rows[0].Offset.Should().Be(0);
        snapshot.Rows[1].Offset.Should().Be(16);
        snapshot.Rows[1].Value.Should().Be(new Vector4(0.25f, 0.5f, 0.75f, 1f));
        snapshot.Pointer.Should().Be(0x1234);
    }

    [Fact]
    public void Classify_IgnoresBytesBeyondWhatWasWritten()
    {
        var bytes = new byte[64];
        Write(bytes, 0, 1f, 0f, 0f, 0f);

        // A buffer only partly written must not report rows built from whatever the rest holds.
        LightConstantProbe.Classify(0, bytes, 16).Rows.Should().HaveCount(1);
    }

    /// <summary>
    /// The diff is the step that actually discriminates: a row holding still across a lighting change is not a
    /// light, however light-like its shape.
    /// </summary>
    [Fact]
    public void Changed_ReportsOnlyRowsThatMoved()
    {
        var before = new byte[32];
        Write(before, 0, 0.1f, 0.2f, 0.3f, 1f);
        Write(before, 16, 0.9f, 0.9f, 0.9f, 1f);

        var after = new byte[32];
        Write(after, 0, 0.1f, 0.2f, 0.3f, 1f);   // held still
        Write(after, 16, 0.4f, 0.5f, 0.6f, 1f);  // moved

        var changed = LightConstantProbe.Changed(
            LightConstantProbe.Classify(0, before, before.Length),
            LightConstantProbe.Classify(0, after, after.Length));

        changed.Should().HaveCount(1);
        changed[0].After.Offset.Should().Be(16);
    }

    /// <summary>
    /// Bytes that are identical because nothing was captured must not be reported as bytes that are identical
    /// because nothing moved. Conflating the two turned a broken capture into a confident "no rows changed".
    /// </summary>
    [Fact]
    public void DescribeChanges_SaysSoWhenTheBufferWasNeverReCaptured()
    {
        var bytes = new byte[32];
        Write(bytes, 0, 0.5f, 0.5f, 0.5f, 1f);

        var before = LightConstantProbe.Classify(0xAB, bytes, bytes.Length, captures: 7);
        var after = LightConstantProbe.Classify(0xAB, bytes, bytes.Length, captures: 7);

        LightConstantProbe.DescribeChanges(before, after)
            .Should().Contain("NOT RE-CAPTURED", because: "no conclusion can be drawn from bytes that were never refreshed");
    }

    [Fact]
    public void DescribeChanges_ReportsNormallyOnceTheBufferIsBeingReCaptured()
    {
        var before = new byte[32];
        Write(before, 16, 0.9f, 0.9f, 0.9f, 1f);

        var after = new byte[32];
        Write(after, 16, 0.4f, 0.5f, 0.6f, 1f);

        var text = LightConstantProbe.DescribeChanges(
            LightConstantProbe.Classify(0xAB, before, before.Length, captures: 7),
            LightConstantProbe.Classify(0xAB, after, after.Length, captures: 19));

        text.Should().NotContain("NOT RE-CAPTURED");
        text.Should().Contain("1 of 2 rows changed");
    }

    /// <summary>
    /// A view matrix's rows are unit vectors, so without this test every one of them reads as a possible light
    /// direction. A frame buffer holding view matrices then produces pages of candidates and no signal, which is
    /// exactly what the first real run of this probe returned.
    /// </summary>
    [Fact]
    public void Classify_RotationRows_AreMarkedAsMatrixData()
    {
        var bytes = new byte[48];
        Write(bytes, 0, 1f, 0f, 0f, 12.5f);      // an orthonormal basis with translation in w,
        Write(bytes, 16, 0f, 1f, 0f, -3.25f);    // which is what a view matrix looks like
        Write(bytes, 32, 0f, 0f, 1f, 7f);

        var snapshot = LightConstantProbe.Classify(0, bytes, bytes.Length);

        foreach (var row in snapshot.Rows)
        {
            row.Kind.Should().HaveFlag(RowKind.UnitVector);
            row.Kind.Should().HaveFlag(RowKind.MatrixRow, because: "three mutually perpendicular unit vectors are a rotation");
        }
    }

    [Fact]
    public void Classify_UnitVectorsThatAreNotPerpendicular_AreNotMatrixData()
    {
        var bytes = new byte[48];
        Write(bytes, 0, 0f, -1f, 0f, 0f);
        Write(bytes, 16, 0f, -1f, 0f, 0f);   // the same direction three times is not a basis
        Write(bytes, 32, 0f, -1f, 0f, 0f);

        var snapshot = LightConstantProbe.Classify(0, bytes, bytes.Length);

        snapshot.Rows[0].Kind.Should().HaveFlag(RowKind.UnitVector);
        snapshot.Rows[0].Kind.Should().NotHaveFlag(RowKind.MatrixRow, because: "a light direction repeated is still a light direction");
    }

    [Fact]
    public void DescribeChanges_OmitsRotationRowsThatMoved()
    {
        var before = new byte[48];
        Write(before, 0, 1f, 0f, 0f, 0f);
        Write(before, 16, 0f, 1f, 0f, 0f);
        Write(before, 32, 0f, 0f, 1f, 0f);

        var after = new byte[48];
        Write(after, 0, 0f, 1f, 0f, 0f);   // the camera turned: every row moved
        Write(after, 16, 0f, 0f, 1f, 0f);
        Write(after, 32, 1f, 0f, 0f, 0f);

        var text = LightConstantProbe.DescribeChanges(
            LightConstantProbe.Classify(0, before, before.Length, captures: 1),
            LightConstantProbe.Classify(0, after, after.Length, captures: 2));

        text.Should().Contain("3 of 3 rows changed");
        text.Should().Contain("nothing changed that is not transform data",
            because: "a camera that turned must not be reported as a lighting change");
    }

    /// <summary>
    /// Rows taken verbatim from a real capture, so the ranking is pinned to data the game actually produced
    /// rather than to values invented to satisfy it.
    /// </summary>
    [Fact]
    public void Candidates_RanksTheColorAndDirectionFromARealCapture()
    {
        var buffer = new byte[64];
        Write(buffer, 0, 0.0052f, 0.5540f, -0.8325f, -1.7396f);   // a direction
        Write(buffer, 16, -0.0035f, 0.8325f, 0.5540f, -3.1749f);  // perpendicular to it
        Write(buffer, 32, 0.7994f, 0.7648f, 0.6653f, 1.0000f);    // a warm white with unit weight
        Write(buffer, 48, 12.5f, 900f, -4f, 1f);                  // transform data

        var candidates = LightConstantProbe.Candidates(
            [LightConstantProbe.Classify(0xA, buffer, buffer.Length)]);

        candidates.Should().Contain(c => c.Row.Offset == 32 && c.Reason.Contains("colour"),
            because: "rgb inside 0..1 with w exactly 1 and a colour cast is the shape of a light colour");

        candidates.Should().NotContain(c => c.Row.Offset == 48,
            because: "a row with a component in the hundreds is transform data");
    }

    [Fact]
    public void Candidates_RanksAValueSharedAcrossBuffersAbove_AOneOff()
    {
        var shared = new byte[16];
        Write(shared, 0, 0.0052f, 0.5540f, -0.8325f, 0f);

        var oneOff = new byte[16];
        Write(oneOff, 0, 0.4082f, 0.8165f, 0.4082f, 0f);

        var candidates = LightConstantProbe.Candidates(
        [
            LightConstantProbe.Classify(0xA, shared, shared.Length),
            LightConstantProbe.Classify(0xB, shared, shared.Length),
            LightConstantProbe.Classify(0xC, oneOff, oneOff.Length),
        ]);

        candidates[0].Corroboration.Should().Be(2, because: "the game feeds the same light to several passes");
        candidates[0].Row.Value.X.Should().BeApproximately(0.0052f, 1e-5f);
    }

    /// <summary>
    /// Canonical axes appear in nearly every buffer because they are defaults, so ranking on how widely a value
    /// is shared put every one of them above the real measurement. Values built only from 0 and 1 are excluded.
    /// </summary>
    [Fact]
    public void Candidates_ExcludesCanonicalAxesAndFlags()
    {
        var buffer = new byte[64];
        Write(buffer, 0, 0f, 0f, -1f, 0f);            // an axis, seen in ten buffers in a real capture
        Write(buffer, 16, 1f, 1f, 0f, 1f);            // a flag that reads as a colour
        Write(buffer, 32, 0f, 0f, 1f, 1f);            // another axis
        Write(buffer, 48, 0f, -0.5540f, -0.8325f, 0f); // the real measurement: fractional and exactly unit

        var candidates = LightConstantProbe.Candidates([LightConstantProbe.Classify(0xA, buffer, buffer.Length)]);

        candidates.Should().HaveCount(1, because: "only the fractional row is a measurement rather than a default");
        candidates[0].Row.Offset.Should().Be(48);
    }

    /// <summary>
    /// Responding to a lighting change is evidence; shape is not. A row that moved must outrank a row that
    /// merely looks right, however many buffers agree on the latter.
    /// </summary>
    [Fact]
    public void Candidates_RankAResponseAboveShapeAndCorroboration()
    {
        var before = new byte[32];
        Write(before, 0, 0.30f, 0.40f, 0.86f, 0f);    // widely shared, never moves
        Write(before, 16, 0.70f, 0.68f, 0.60f, 1f);   // moves when the light changes

        var after = new byte[32];
        Write(after, 0, 0.30f, 0.40f, 0.86f, 0f);
        Write(after, 16, 0.40f, 0.38f, 0.33f, 1f);

        var marked = new[] { LightConstantProbe.Classify(0xA, before, before.Length, captures: 1) };
        var current = new[] { LightConstantProbe.Classify(0xA, after, after.Length, captures: 9) };

        var candidates = LightConstantProbe.Candidates(current, marked);

        candidates[0].Row.Offset.Should().Be(16);
        candidates[0].Responded.Should().BeTrue();
        candidates[1].Responded.Should().BeFalse();
    }

    /// <summary>
    /// The game jitters sample kernels per frame, so those rows move across any comparison and would otherwise
    /// fill the top of the ranking with noise. A control taken with nothing changed names them.
    /// </summary>
    [Fact]
    public void VolatileRows_NamesTheRowsThatMoveOnTheirOwn()
    {
        var first = new byte[32];
        Write(first, 0, 0.6045f, -0.1197f, 0.7876f, 0f);   // a jittered kernel direction
        Write(first, 16, 0.70f, 0.68f, 0.60f, 1f);          // steady

        var second = new byte[32];
        Write(second, 0, 0.6147f, -0.1062f, 0.7812f, 0f);   // jittered again, nothing was changed
        Write(second, 16, 0.70f, 0.68f, 0.60f, 1f);

        var rows = LightConstantProbe.VolatileRows(
            [LightConstantProbe.Classify(0xA, first, first.Length, captures: 1)],
            [LightConstantProbe.Classify(0xA, second, second.Length, captures: 5)]);

        rows.Should().Contain((0xA, 0));
        rows.Should().NotContain((0xA, 16));
    }

    [Fact]
    public void Candidates_DoNotCreditASelfChangingRowWithResponding()
    {
        var before = new byte[32];
        Write(before, 0, 0.6045f, -0.1197f, 0.7876f, 0f);
        Write(before, 16, 0.70f, 0.68f, 0.60f, 1f);

        var after = new byte[32];
        Write(after, 0, 0.5889f, 0.3086f, -0.7471f, 0f);   // moved, but it always moves
        Write(after, 16, 0.40f, 0.38f, 0.33f, 1f);          // moved because the light changed

        var candidates = LightConstantProbe.Candidates(
            [LightConstantProbe.Classify(0xA, after, after.Length, captures: 9)],
            [LightConstantProbe.Classify(0xA, before, before.Length, captures: 1)],
            new HashSet<(nint, int)> { (0xA, 0) });

        candidates.Should().Contain(c => c.Row.Offset == 16 && c.Responded);
        candidates.Should().NotContain(c => c.Row.Offset == 0 && c.Responded,
            because: "a row that moves every frame proves nothing by moving again");
    }

    /// <summary>
    /// A projection's depth pair is a unit vector by shape and moves whenever a shadow frustum is refitted, so it
    /// survives every other test and looks like a light responding. These are the exact values that did.
    /// </summary>
    [Theory]
    [InlineData(0f, 0f, -1.0187f, -0.1019f)]
    [InlineData(0f, 0f, -1.0088f, -0.1009f)]
    [InlineData(0f, 0f, -1.0053f, -0.1508f)]
    public void Candidates_ExcludeProjectionDepthRows(float x, float y, float z, float w)
    {
        var buffer = new byte[16];
        Write(buffer, 0, x, y, z, w);

        LightConstantProbe.Candidates([LightConstantProbe.Classify(0xA, buffer, buffer.Length)])
            .Should().BeEmpty(because: "the ratio of those two slots is 1/near, which makes it a projection row");
    }

    [Fact]
    public void Candidates_KeepADirectionThatMerelyHasAZeroComponent()
    {
        var buffer = new byte[16];
        Write(buffer, 0, 0f, -0.8756f, -0.4830f, 1.0535f);

        LightConstantProbe.Candidates([LightConstantProbe.Classify(0xA, buffer, buffer.Length)])
            .Should().ContainSingle(because: "a direction in a plane is still a direction, not a projection row");
    }

    [Fact]
    public void Candidates_RejectsGreyTriples()
    {
        var buffer = new byte[16];
        Write(buffer, 0, 0.5f, 0.5f, 0.5f, 1f);

        LightConstantProbe.Candidates([LightConstantProbe.Classify(0xA, buffer, buffer.Length)])
            .Should().NotContain(c => c.Reason.Contains("colour"),
                because: "an even triple is more likely a uniform scale than a light colour");
    }

    [Fact]
    public void Changed_ToleratesBuffersOfDifferentLengths()
    {
        var before = new byte[16];
        var after = new byte[32];
        Write(after, 16, 1f, 1f, 1f, 1f);

        var act = () => LightConstantProbe.Changed(
            LightConstantProbe.Classify(0, before, before.Length),
            LightConstantProbe.Classify(0, after, after.Length));

        act.Should().NotThrow(because: "the game reallocates its buffers and a diff must survive it");
    }

    private static void Write(byte[] target, int offset, float x, float y, float z, float w)
    {
        BitConverter.TryWriteBytes(target.AsSpan(offset), x);
        BitConverter.TryWriteBytes(target.AsSpan(offset + 4), y);
        BitConverter.TryWriteBytes(target.AsSpan(offset + 8), z);
        BitConverter.TryWriteBytes(target.AsSpan(offset + 12), w);
    }
}
