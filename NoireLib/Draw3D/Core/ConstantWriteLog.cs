using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace NoireLib.Draw3D.Core;

// The deferred renderer rewrites the same buffer once per light.
internal sealed class ConstantWriteLog
{
    private const int MaxRecords = 4096;

    private const int MaxRecordBytes = 512;

    private const int InlineRowLimit = 6;

    private readonly List<WriteRecord> records = new(256);
    private int framesRemaining;
    private long dropped;
    private int sizeFilter;

    private readonly record struct WriteRecord(nint Pointer, int ByteWidth, byte[] Bytes);

    public bool Armed => framesRemaining > 0;

    public int Count => records.Count;

    public bool Truncated => dropped > 0;

    public long Dropped => dropped;

    public int SizeFilter => sizeFilter;

    // Unfiltered, the early passes' object transforms fill the budget before the lighting pass runs.
    public void Arm(int frames, int byteWidth = 0)
    {
        records.Clear();
        dropped = 0;
        sizeFilter = Math.Max(byteWidth, 0);
        framesRemaining = Math.Max(frames, 0);
    }

    public void OnFrameBoundary()
    {
        if (framesRemaining > 0)
            framesRemaining--;
    }

    public unsafe void Record(nint pointer, int byteWidth, nint data, int length)
    {
        if (framesRemaining <= 0 || data == 0)
            return;

        if (sizeFilter > 0 && byteWidth != sizeFilter)
            return;

        if (records.Count >= MaxRecords)
        {
            dropped++;
            return;
        }

        var keep = Math.Min(Math.Min(length, byteWidth), MaxRecordBytes);
        if (keep <= 0)
            return;

        var bytes = new byte[keep];
        fixed (byte* dst = bytes)
            Buffer.MemoryCopy((void*)data, dst, keep, keep);

        records.Add(new WriteRecord(pointer, byteWidth, bytes));
    }

    public string Describe(int maxGroups = 8, int maxPerGroup = 12)
    {
        var sb = new StringBuilder();
        if (records.Count == 0)
        {
            sb.AppendLine("No writes recorded. Arm the log and let at least one frame render.");
            return sb.ToString();
        }

        var byBuffer = new Dictionary<nint, (int ByteWidth, int Writes, List<byte[]> Distinct)>();
        foreach (var record in records)
        {
            if (!byBuffer.TryGetValue(record.Pointer, out var group))
                group = (record.ByteWidth, 0, []);

            group.Writes++;

            var seen = false;
            foreach (var existing in group.Distinct)
            {
                if (existing.AsSpan().SequenceEqual(record.Bytes))
                {
                    seen = true;
                    break;
                }
            }

            if (!seen && group.Distinct.Count < 64)
                group.Distinct.Add(record.Bytes);

            byBuffer[record.Pointer] = group;
        }

        var ordered = new List<KeyValuePair<nint, (int ByteWidth, int Writes, List<byte[]> Distinct)>>(byBuffer);
        ordered.Sort((a, b) => b.Value.Distinct.Count.CompareTo(a.Value.Distinct.Count));

        sb.AppendLine($"{records.Count} write(s) across {byBuffer.Count} buffer(s), most-varied first.");
        sb.AppendLine("A buffer rewritten many times per frame with DIFFERENT contents is a per-item list; one rewritten with the same contents is a frame constant.");

        if (dropped > 0)
        {
            sb.AppendLine($"TRUNCATED: {dropped} further write(s) were dropped after the cap. Early passes fill the budget with object transforms, "
                        + "so the end of the frame - where the lighting pass runs - is NOT in here. Re-run restricted to one size class.");
        }

        if (sizeFilter > 0)
            sb.AppendLine($"Restricted to {sizeFilter} B buffers.");

        var groups = 0;
        foreach (var pair in ordered)
        {
            if (groups++ >= maxGroups)
            {
                sb.AppendLine("  ...");
                break;
            }

            sb.AppendLine();
            sb.AppendLine($"buffer 0x{pair.Key:X} ({pair.Value.ByteWidth} B): {pair.Value.Writes} write(s), {pair.Value.Distinct.Count} distinct");

            var shown = 0;
            foreach (var payload in pair.Value.Distinct)
            {
                if (shown++ >= maxPerGroup)
                {
                    sb.AppendLine("    ...");
                    break;
                }

                AppendPayload(sb, shown, payload);
            }
        }

        return sb.ToString();
    }

    // The game cycles a small ring of buffers.
    public List<byte[]> DistinctPayloads()
    {
        var distinct = new List<byte[]>();
        foreach (var record in records)
        {
            if (!Contains(distinct, record.Bytes))
                distinct.Add(record.Bytes);
        }

        return distinct;
    }

    public static string DescribeDiff(IReadOnlyList<byte[]> before, IReadOnlyList<byte[]> after)
    {
        var sb = new StringBuilder();

        var added = new List<byte[]>();
        foreach (var payload in after)
        {
            if (!Contains(before, payload))
                added.Add(payload);
        }

        var removed = new List<byte[]>();
        foreach (var payload in before)
        {
            if (!Contains(after, payload))
                removed.Add(payload);
        }

        var held = before.Count - removed.Count;

        sb.AppendLine($"Write-log diff: {before.Count} payload(s) before, {after.Count} after - {held} unchanged, {removed.Count} gone, {added.Count} new.");
        sb.AppendLine("Anything listed below responded to the change you made. Anything unchanged did not, no matter what shape it has.");

        if (added.Count == 0 && removed.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("NOTHING RESPONDED. Either the change does not reach this size class, or it never reached the GPU at all -");
            sb.AppendLine("repeat it with a change that is unmistakable on screen, such as removing a lamp.");
            return sb.ToString();
        }

        var pairedRemoved = new bool[removed.Count];
        var shown = 0;

        for (var i = 0; i < added.Count && shown < 16; i++)
        {
            var match = -1;
            var bestShared = 0;

            for (var j = 0; j < removed.Count; j++)
            {
                if (pairedRemoved[j])
                    continue;

                var shared = SharedRows(added[i], removed[j]);
                if (shared > bestShared)
                {
                    bestShared = shared;
                    match = j;
                }
            }

            var rows = added[i].Length / 16;
            sb.AppendLine();

            if (match >= 0 && bestShared * 2 >= rows)
            {
                pairedRemoved[match] = true;
                sb.AppendLine($"CHANGED ({bestShared} of {rows} rows identical, one record rewritten):");
                AppendRowDiff(sb, removed[match], added[i]);
            }
            else
            {
                sb.AppendLine("NEW (no earlier payload resembles it):");
                AppendPayload(sb, shown + 1, added[i]);
            }

            shown++;
        }

        for (var j = 0; j < removed.Count && shown < 24; j++)
        {
            if (pairedRemoved[j])
                continue;

            sb.AppendLine();
            sb.AppendLine("GONE (no later payload resembles it):");
            AppendPayload(sb, shown + 1, removed[j]);
            shown++;
        }

        return sb.ToString();
    }

    private static void AppendRowDiff(StringBuilder sb, byte[] before, byte[] after)
    {
        var rows = Math.Min(before.Length, after.Length) / 16;
        for (var i = 0; i < rows; i++)
        {
            var b = Row(before, i);
            var a = Row(after, i);
            if (b.Equals(a))
                continue;

            var line = new StringBuilder();
            AppendRow(line, b);
            sb.AppendLine($"        r{i:D2} was {line}");

            line.Clear();
            AppendRow(line, a);
            sb.AppendLine($"        r{i:D2} now {line}");
        }
    }

    private static int SharedRows(byte[] a, byte[] b)
    {
        var rows = Math.Min(a.Length, b.Length) / 16;
        var shared = 0;

        for (var i = 0; i < rows; i++)
        {
            if (a.AsSpan(i * 16, 16).SequenceEqual(b.AsSpan(i * 16, 16)))
                shared++;
        }

        return shared;
    }

    private static bool Contains(IReadOnlyList<byte[]> payloads, byte[] candidate)
    {
        foreach (var payload in payloads)
        {
            if (payload.AsSpan().SequenceEqual(candidate))
                return true;
        }

        return false;
    }

    private static void AppendPayload(StringBuilder sb, int index, byte[] payload)
    {
        var rows = payload.Length / 16;

        if (rows <= InlineRowLimit)
        {
            var inline = new StringBuilder();
            for (var i = 0; i < rows; i++)
            {
                if (i > 0)
                    inline.Append("  |  ");

                AppendRow(inline, Row(payload, i));
            }

            sb.AppendLine($"    [{index}] {inline}");
            return;
        }

        sb.AppendLine($"    [{index}] {rows} rows:");
        for (var i = 0; i < rows; i++)
        {
            var line = new StringBuilder();
            AppendRow(line, Row(payload, i));
            sb.AppendLine($"        r{i:D2} {line}");
        }
    }

    private static Vector4 Row(byte[] payload, int index)
        => BufferHelper.ReadVector4(payload, index * 16);

    private static void AppendRow(StringBuilder sb, Vector4 v)
    {
        sb.Append($"({v.X,10:F3},{v.Y,10:F3},{v.Z,10:F3},{v.W,10:F3})");

        var kind = LightConstantProbe.Classify(v);
        if (kind.HasFlag(RowKind.ColorLike) && kind.HasFlag(RowKind.Normalized))
            sb.Append(" col?");
        else if (kind.HasFlag(RowKind.UnitVector))
            sb.Append(" dir?");
        else if (kind.HasFlag(RowKind.Large))
            sb.Append(" pos?");
    }
}
