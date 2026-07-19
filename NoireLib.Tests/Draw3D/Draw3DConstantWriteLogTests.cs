using FluentAssertions;
using NoireLib.Draw3D.Core;
using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the write-log comparison the light search now rests on.<br/>
/// Reading a buffer's shape has already produced confident wrong answers twice, so the search was moved onto
/// evidence instead: record every payload, alter one light in the world, record again, and report what moved.
/// These tests cover the ways that comparison could quietly lie - by matching payloads to the wrong buffer, by
/// calling an unchanged frame a response, or by reporting a silent result as though it were a finding.
/// </summary>
public class Draw3DConstantWriteLogTests
{
    /// <summary>Builds a payload of 16-byte rows, the layout every constant buffer uses.</summary>
    private static byte[] Payload(params Vector4[] rows)
    {
        var bytes = new byte[rows.Length * 16];
        for (var i = 0; i < rows.Length; i++)
        {
            BitConverter.GetBytes(rows[i].X).CopyTo(bytes, i * 16);
            BitConverter.GetBytes(rows[i].Y).CopyTo(bytes, i * 16 + 4);
            BitConverter.GetBytes(rows[i].Z).CopyTo(bytes, i * 16 + 8);
            BitConverter.GetBytes(rows[i].W).CopyTo(bytes, i * 16 + 12);
        }

        return bytes;
    }

    /// <summary>A record shaped like the ones the game writes: a position, a direction, then a colour twice.</summary>
    private static byte[] LightRecord(Vector3 position, Vector3 direction, Vector3 color) => Payload(
        new Vector4(position, 0f),
        new Vector4(direction, 0f),
        new Vector4(color, 1f),
        new Vector4(color, 1f));

    [Fact]
    public void DistinctPayloads_PoolsAcrossBuffers()
    {
        // The game cycles a ring of buffers so it never overwrites one the GPU is still reading, which puts the
        // same record in a different buffer each frame. Keyed by buffer, every entry would read as new.
        var log = new ConstantWriteLog();
        log.Arm(1);

        var lamp = LightRecord(new Vector3(2f, 1f, 3f), -Vector3.UnitY, new Vector3(12.8f, 11.7f, 6.9f));
        RecordPayload(log, 0x1000, lamp);
        RecordPayload(log, 0x2000, lamp);
        RecordPayload(log, 0x3000, lamp);

        log.DistinctPayloads().Should().HaveCount(1);
    }

    [Fact]
    public void DistinctPayloads_KeepsGenuinelyDifferentRecords()
    {
        var log = new ConstantWriteLog();
        log.Arm(1);

        RecordPayload(log, 0x1000, LightRecord(new Vector3(2f, 1f, 3f), -Vector3.UnitY, Vector3.One));
        RecordPayload(log, 0x1000, LightRecord(new Vector3(5f, 1f, 3f), -Vector3.UnitY, Vector3.One));

        log.DistinctPayloads().Should().HaveCount(2);
    }

    [Fact]
    public void DescribeDiff_UnchangedCapture_SaysNothingResponded()
    {
        // The control result. Without it, a comparison that finds nothing has no way to distinguish "this size
        // class does not carry the change" from "the change never reached the GPU", and silence reads as proof.
        var lamp = LightRecord(new Vector3(2f, 1f, 3f), -Vector3.UnitY, new Vector3(12.8f, 11.7f, 6.9f));

        var report = ConstantWriteLog.DescribeDiff([lamp], [lamp]);

        report.Should().Contain("NOTHING RESPONDED");
    }

    [Fact]
    public void DescribeDiff_RecolouredLight_ReportsItAsOneRecordRewritten()
    {
        // Switching a lamp off leaves its position and direction alone and rewrites its colour. Reported as an
        // unrelated removal plus an unrelated addition, the one row that actually carries the light would be
        // buried; paired up, it is the whole answer.
        var position = new Vector3(2f, 1f, 3f);
        var direction = -Vector3.UnitY;

        var lit = LightRecord(position, direction, new Vector3(12.8f, 11.7f, 6.9f));
        var dark = LightRecord(position, direction, Vector3.Zero);

        var report = ConstantWriteLog.DescribeDiff([lit], [dark]);

        report.Should().Contain("CHANGED");
        report.Should().Contain("r02 was");
        report.Should().Contain("r02 now");

        // The rows that did not move must not be listed, or the report is as unreadable as the raw dump.
        report.Should().NotContain("r00 was");
        report.Should().NotContain("r01 was");
    }

    [Fact]
    public void DescribeDiff_UnrelatedPayload_IsNotPairedWithOne()
    {
        // Pairing needs a floor. Two records that share nothing are two facts, and merging them into one invented
        // "change" would read as a light moving when no light did anything.
        var before = LightRecord(new Vector3(2f, 1f, 3f), -Vector3.UnitY, new Vector3(12.8f, 11.7f, 6.9f));
        var after = LightRecord(new Vector3(-40f, 8f, 17f), Vector3.UnitX, new Vector3(0.1f, 0.9f, 0.4f));

        var report = ConstantWriteLog.DescribeDiff([before], [after]);

        report.Should().Contain("NEW");
        report.Should().Contain("GONE");
        report.Should().NotContain("CHANGED");
    }

    [Fact]
    public void DescribeDiff_LightRemoved_IsReportedGone()
    {
        var kept = LightRecord(new Vector3(2f, 1f, 3f), -Vector3.UnitY, Vector3.One);
        var removed = LightRecord(new Vector3(9f, 2f, -4f), Vector3.UnitZ, new Vector3(51f, 48f, 44f));

        var report = ConstantWriteLog.DescribeDiff([kept, removed], [kept]);

        report.Should().Contain("GONE");
        report.Should().Contain("1 unchanged");
    }

    [Fact]
    public void Arm_SizeFilter_RecordsOnlyThatClass()
    {
        // Without the filter a frame's early passes spend the whole budget on object transforms and the log never
        // reaches the lighting pass, which runs at the end of the frame.
        var log = new ConstantWriteLog();
        log.Arm(1, 64);

        RecordPayload(log, 0x1000, Payload(new Vector4(1f), new Vector4(2f), new Vector4(3f), new Vector4(4f)));
        RecordPayload(log, 0x2000, Payload(new Vector4(5f), new Vector4(6f)));

        log.SizeFilter.Should().Be(64);
        log.Count.Should().Be(1);
    }

    [Fact]
    public void Describe_WholePayloadIsKept_SoAStrideIsReadable()
    {
        // A record's stride is only visible when the rows after the first few are present. Truncating to a prefix
        // hides where one entry ends and the next begins, which is the one thing a repeating layout is known by.
        var log = new ConstantWriteLog();
        log.Arm(1);

        var rows = new Vector4[32];
        for (var i = 0; i < rows.Length; i++)
            rows[i] = new Vector4(i, i, i, i);

        RecordPayload(log, 0x1000, Payload(rows));

        var report = log.Describe();

        report.Should().Contain("32 rows");
        report.Should().Contain("r31");
    }

    /// <summary>Feeds a payload through the log's unmanaged recording path.</summary>
    private static unsafe void RecordPayload(ConstantWriteLog log, nint pointer, byte[] payload)
    {
        fixed (byte* data = payload)
            log.Record(pointer, payload.Length, (nint)data, payload.Length);
    }
}
