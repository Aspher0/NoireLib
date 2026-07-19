using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// Records every payload written to the game's small constant buffers during a few frames, rather than only
/// the last one.<br/>
/// <b>Why the last write is not enough:</b> a deferred renderer draws one light at a time through the same
/// buffer, rewriting it per light. A tracker that keeps only the final contents sees a single buffer whose
/// value changes constantly, which reads as noise and gets subtracted as self-changing - so the lights are
/// discarded precisely because there are many of them. Keeping every write turns that noise back into the
/// list it always was.
/// </summary>
internal sealed class ConstantWriteLog
{
    /// <summary>Payloads kept per armed run.</summary>
    private const int MaxRecords = 4096;

    /// <summary>
    /// Bytes kept per payload.<br/>
    /// This has to cover a whole buffer, not a guessed-at prefix. A record's stride is only readable when the
    /// rows after the first few are present too: truncating to a prefix hides where one entry ends and the next
    /// begins, which is the single thing a repeating layout is recognised by.
    /// </summary>
    private const int MaxRecordBytes = 512;

    /// <summary>Rows printed on one line before a payload is broken out one row per line.</summary>
    private const int InlineRowLimit = 6;

    private readonly List<WriteRecord> records = new(256);
    private int framesRemaining;
    private long dropped;
    private int sizeFilter;

    private readonly record struct WriteRecord(nint Pointer, int ByteWidth, byte[] Bytes);

    /// <summary>Whether writes are currently being recorded.</summary>
    public bool Armed => framesRemaining > 0;

    /// <summary>How many payloads have been recorded.</summary>
    public int Count => records.Count;

    /// <summary>Whether the record cap was reached, which means later passes in the frame were never seen.</summary>
    public bool Truncated => dropped > 0;

    /// <summary>How many payloads were dropped after the cap.</summary>
    public long Dropped => dropped;

    /// <summary>The buffer size this run is restricted to, or zero for all of them.</summary>
    public int SizeFilter => sizeFilter;

    /// <summary>
    /// Starts recording for the next <paramref name="frames"/> world frames.
    /// </summary>
    /// <param name="frames">How many frames to record.</param>
    /// <param name="byteWidth">
    /// When above zero, only buffers of exactly this size are recorded.<br/>
    /// This matters more than it looks: a frame's early passes write thousands of object transforms, and an
    /// unfiltered log fills its budget on them before the deferred lighting pass runs at all. Restricting to one
    /// size class is what makes a late pass reachable.
    /// </param>
    public void Arm(int frames, int byteWidth = 0)
    {
        records.Clear();
        dropped = 0;
        sizeFilter = Math.Max(byteWidth, 0);
        framesRemaining = Math.Max(frames, 0);
    }

    /// <summary>Counts down the armed window. Called once per present.</summary>
    public void OnFrameBoundary()
    {
        if (framesRemaining > 0)
            framesRemaining--;
    }

    /// <summary>Records one payload, if there is room.</summary>
    /// <param name="pointer">The buffer written to.</param>
    /// <param name="byteWidth">The buffer's declared size.</param>
    /// <param name="data">The payload.</param>
    /// <param name="length">Valid bytes at <paramref name="data"/>.</param>
    public unsafe void Record(nint pointer, int byteWidth, nint data, int length)
    {
        if (framesRemaining <= 0 || data == 0)
            return;

        if (sizeFilter > 0 && byteWidth != sizeFilter)
            return;

        // Counted rather than silently ignored: a log that stops early has not seen the end of the frame, and
        // the lighting pass is at the end of the frame.
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

    /// <summary>
    /// Groups the recorded payloads and reports the ones that look like a list of lights.<br/>
    /// A light record carries a colour and a position, so a buffer written many times per frame with a
    /// colour-shaped row in the same place each time is the shape being looked for. The report shows the
    /// distinct payloads so a repeating layout is visible by eye.
    /// </summary>
    /// <param name="maxGroups">How many buffers to report.</param>
    /// <param name="maxPerGroup">How many distinct payloads to show per buffer.</param>
    public string Describe(int maxGroups = 8, int maxPerGroup = 12)
    {
        var sb = new StringBuilder();
        if (records.Count == 0)
        {
            sb.AppendLine("No writes recorded. Arm the log and let at least one frame render.");
            return sb.ToString();
        }

        // Group by buffer, and within a buffer keep only distinct payloads: a light list rewrites the same
        // buffer with different values, while a per-frame constant rewrites it with the same value.
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

        // Most distinct payloads first: that is what a list of many different lights looks like.
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

    /// <summary>
    /// The distinct payloads recorded, pooled across every buffer instead of grouped by one.<br/>
    /// Pooling is deliberate. The game cycles a small ring of buffers so it never overwrites one the GPU is still
    /// reading, which means the same record lands in a different buffer from one frame to the next. Keyed by
    /// buffer, every entry would read as new on every capture and a comparison between two captures would be
    /// entirely noise. The set of payloads is the thing that is actually stable.
    /// </summary>
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

    /// <summary>
    /// Reports how a recorded payload set changed against an earlier one.<br/>
    /// This is the step that turns a shape into a fact. Every reading so far has been "this looks like a light",
    /// which two wrong answers have already shown is not enough. A payload that appears, vanishes, or has one row
    /// rewritten precisely when a light in the room was switched is a light, whatever it looks like; a payload
    /// that sits unchanged across that switch is not one, however much it resembles a colour and a position.
    /// </summary>
    /// <param name="before">The payload set captured before the lighting was altered.</param>
    /// <param name="after">The payload set captured after it.</param>
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
            sb.AppendLine("repeat it with a change that is unmistakable on screen, such as removing a lamp rather than dimming one.");
            return sb.ToString();
        }

        // A light that was only recoloured leaves a payload whose other rows are untouched. Pairing those up says
        // which row carries the change, which a plain added/removed listing cannot.
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
                sb.AppendLine($"CHANGED ({bestShared} of {rows} rows identical, so this is one record rewritten rather than two unrelated ones):");
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

    /// <summary>Prints two paired payloads row by row, marking the rows that differ.</summary>
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

    /// <summary>How many 16-byte rows two payloads share at the same offset.</summary>
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

    /// <summary>Whether a payload set already holds these exact bytes.</summary>
    private static bool Contains(IReadOnlyList<byte[]> payloads, byte[] candidate)
    {
        foreach (var payload in payloads)
        {
            if (payload.AsSpan().SequenceEqual(candidate))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Writes one payload out as rows, flagging the ones shaped like a colour or a position.<br/>
    /// Anything past a handful of rows goes one row per line with its index: a repeating record is spotted by
    /// seeing the same kind of row recur at a fixed spacing, and a single wrapped line hides exactly that.
    /// </summary>
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

    /// <summary>Reads one 16-byte row of a payload as a vector.</summary>
    private static Vector4 Row(byte[] payload, int index)
    {
        var offset = index * 16;
        return new Vector4(
            BitConverter.ToSingle(payload, offset),
            BitConverter.ToSingle(payload, offset + 4),
            BitConverter.ToSingle(payload, offset + 8),
            BitConverter.ToSingle(payload, offset + 12));
    }

    /// <summary>Appends one row with a shape hint.</summary>
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
