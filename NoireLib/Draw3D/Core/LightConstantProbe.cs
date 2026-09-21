using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace NoireLib.Draw3D.Core;

internal readonly record struct ConstantRow(int Offset, Vector4 Value, RowKind Kind);

// Narrows the candidates. Many unrelated values share a shape.
[Flags]
internal enum RowKind
{
    None = 0,

    Zero = 1,

    UnitVector = 2,

    ColorLike = 4,

    Normalized = 8,

    Large = 16,

    // One axis of an orthonormal basis: rotation data.
    MatrixRow = 32,
}

internal sealed record ConstantSnapshot(nint Pointer, int ByteWidth, ConstantRow[] Rows, long Captures);

// Diffs two moments. A row that moves with the light and holds still otherwise is a candidate.
internal static class LightConstantProbe
{
    private const float UnitTolerance = 0.02f;

    private const float LargeThreshold = 8f;

    private const float ChangeEpsilon = 1e-4f;

    private const float OrthogonalTolerance = 0.03f;

    public static ConstantSnapshot Classify(nint pointer, ReadOnlySpan<byte> bytes, int validBytes, long captures = 0)
    {
        var usable = Math.Min(validBytes, bytes.Length);
        var rowCount = usable / 16;
        var rows = new ConstantRow[rowCount];

        for (var i = 0; i < rowCount; i++)
        {
            var offset = i * 16;
            var value = new Vector4(
                BitConverter.ToSingle(bytes[offset..]),
                BitConverter.ToSingle(bytes[(offset + 4)..]),
                BitConverter.ToSingle(bytes[(offset + 8)..]),
                BitConverter.ToSingle(bytes[(offset + 12)..]));

            rows[i] = new ConstantRow(offset, value, Classify(value));
        }

        MarkMatrixRows(rows);
        return new ConstantSnapshot(pointer, usable, rows, captures);
    }

    // A light direction is a unit vector too. Only a rotation row has two perpendicular partners.
    private static void MarkMatrixRows(ConstantRow[] rows)
    {
        for (var i = 0; i + 2 < rows.Length; i++)
        {
            var a = Xyz(rows[i].Value);
            var b = Xyz(rows[i + 1].Value);
            var c = Xyz(rows[i + 2].Value);

            if (!IsUnit(a) || !IsUnit(b) || !IsUnit(c))
                continue;

            if (Math.Abs(Vector3.Dot(a, b)) > OrthogonalTolerance
                || Math.Abs(Vector3.Dot(a, c)) > OrthogonalTolerance
                || Math.Abs(Vector3.Dot(b, c)) > OrthogonalTolerance)
                continue;

            for (var k = i; k <= i + 2; k++)
                rows[k] = rows[k] with { Kind = rows[k].Kind | RowKind.MatrixRow };
        }
    }

    private static Vector3 Xyz(Vector4 v) => new(v.X, v.Y, v.Z);

    private static bool IsUnit(Vector3 v) => Math.Abs(v.Length() - 1f) <= UnitTolerance;

    public static RowKind Classify(Vector4 v)
    {
        if (!IsFinite(v))
            return RowKind.None;

        if (v == Vector4.Zero)
            return RowKind.Zero;

        var kind = RowKind.None;
        var xyz = new Vector3(v.X, v.Y, v.Z);

        if (Math.Abs(xyz.Length() - 1f) <= UnitTolerance)
            kind |= RowKind.UnitVector;

        if (xyz.X >= 0f && xyz.Y >= 0f && xyz.Z >= 0f && xyz.X <= LargeThreshold && xyz.Y <= LargeThreshold && xyz.Z <= LargeThreshold)
            kind |= RowKind.ColorLike;

        if (InUnit(v.X) && InUnit(v.Y) && InUnit(v.Z) && InUnit(v.W))
            kind |= RowKind.Normalized;

        if (Math.Abs(v.X) > LargeThreshold || Math.Abs(v.Y) > LargeThreshold || Math.Abs(v.Z) > LargeThreshold)
            kind |= RowKind.Large;

        return kind;
    }

    public static IReadOnlyList<(ConstantRow Before, ConstantRow After)> Changed(ConstantSnapshot before, ConstantSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var changed = new List<(ConstantRow, ConstantRow)>();
        var count = Math.Min(before.Rows.Length, after.Rows.Length);

        for (var i = 0; i < count; i++)
        {
            if (Differs(before.Rows[i].Value, after.Rows[i].Value))
                changed.Add((before.Rows[i], after.Rows[i]));
        }

        return changed;
    }

    public static string Describe(ConstantSnapshot snapshot, int maxRows = 48)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var sb = new StringBuilder();
        sb.AppendLine($"buffer 0x{snapshot.Pointer:X} ({snapshot.ByteWidth} B, {snapshot.Rows.Length} rows)");

        var listed = 0;
        foreach (var row in snapshot.Rows)
        {
            if (listed >= maxRows)
            {
                sb.AppendLine("  ...");
                break;
            }

            if (row.Kind is RowKind.None or RowKind.Zero || row.Kind.HasFlag(RowKind.Large) || row.Kind.HasFlag(RowKind.MatrixRow))
                continue;

            sb.AppendLine($"  +{row.Offset,4}  {Format(row.Value)}  {row.Kind}");
            listed++;
        }

        if (listed == 0)
            sb.AppendLine("  (no rows of a light-like shape)");

        return sb.ToString();
    }

    public static string DescribeChanges(ConstantSnapshot before, ConstantSnapshot after, int maxRows = 32)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        // Without a new capture the rows are the same bytes.
        if (after.Captures == before.Captures)
        {
            return $"buffer 0x{after.Pointer:X}: NOT RE-CAPTURED since the mark ({after.Captures} payload copies both times) - "
                 + "these are the same bytes. Keep the capture armed across the whole comparison.\n";
        }

        var changed = Changed(before, after);
        var sb = new StringBuilder();
        sb.AppendLine($"buffer 0x{after.Pointer:X}: {changed.Count} of {after.Rows.Length} rows changed ({after.Captures - before.Captures} payload copies since the mark)");

        var listed = 0;
        foreach (var (b, a) in changed)
        {
            if (listed >= maxRows)
            {
                sb.AppendLine("  ...");
                break;
            }

            if (a.Kind.HasFlag(RowKind.Large) && b.Kind.HasFlag(RowKind.Large))
                continue;

            if (a.Kind.HasFlag(RowKind.MatrixRow) || b.Kind.HasFlag(RowKind.MatrixRow))
                continue;

            sb.AppendLine($"  +{a.Offset,4}  {Format(b.Value)} -> {Format(a.Value)}  {a.Kind}");
            listed++;
        }

        if (listed == 0)
            sb.AppendLine("  (nothing changed that is not transform data)");

        return sb.ToString();
    }

    internal readonly record struct LightCandidate(nint Pointer, ConstantRow Row, string Reason, int Corroboration, bool Responded);

    public static IReadOnlyList<LightCandidate> Candidates(
        IReadOnlyList<ConstantSnapshot> snapshots,
        IReadOnlyList<ConstantSnapshot>? marked = null,
        IReadOnlySet<(nint Pointer, int Offset)>? volatileRows = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var responded = new HashSet<(nint, int)>();
        if (marked is not null)
        {
            foreach (var after in snapshots)
            {
                foreach (var before in marked)
                {
                    if (before.Pointer != after.Pointer || before.Captures == after.Captures)
                        continue;

                    foreach (var (_, a) in Changed(before, after))
                    {
                        if (volatileRows is not null && volatileRows.Contains((after.Pointer, a.Offset)))
                            continue;

                        responded.Add((after.Pointer, a.Offset));
                    }

                    break;
                }
            }
        }

        var seenIn = new Dictionary<(int, int, int), HashSet<nint>>();
        foreach (var snapshot in snapshots)
        {
            foreach (var row in snapshot.Rows)
            {
                if (row.Kind is RowKind.None or RowKind.Zero || row.Kind.HasFlag(RowKind.MatrixRow))
                    continue;

                var key = Quantize(row.Value);
                if (!seenIn.TryGetValue(key, out var buffers))
                    seenIn[key] = buffers = [];

                buffers.Add(snapshot.Pointer);
            }
        }

        var candidates = new List<LightCandidate>();
        foreach (var snapshot in snapshots)
        {
            foreach (var row in snapshot.Rows)
            {
                if (row.Kind is RowKind.None or RowKind.Zero || row.Kind.HasFlag(RowKind.MatrixRow) || row.Kind.HasFlag(RowKind.Large))
                    continue;

                var reason = Reason(row);
                if (reason is null)
                    continue;

                var shared = seenIn.TryGetValue(Quantize(row.Value), out var buffers) ? buffers.Count : 1;
                var moved = responded.Contains((snapshot.Pointer, row.Offset));
                candidates.Add(new LightCandidate(snapshot.Pointer, row, reason, shared, moved));
            }
        }

        candidates.Sort((a, b) => a.Responded != b.Responded
            ? b.Responded.CompareTo(a.Responded)
            : b.Corroboration.CompareTo(a.Corroboration));

        return candidates;
    }

    public static IReadOnlySet<(nint Pointer, int Offset)> VolatileRows(
        IReadOnlyList<ConstantSnapshot> before,
        IReadOnlyList<ConstantSnapshot> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var rows = new HashSet<(nint, int)>();
        foreach (var a in after)
        {
            foreach (var b in before)
            {
                if (b.Pointer != a.Pointer || b.Captures == a.Captures)
                    continue;

                foreach (var (_, row) in Changed(b, a))
                    rows.Add((a.Pointer, row.Offset));

                break;
            }
        }

        return rows;
    }

    public static string DescribeCandidates(
        IReadOnlyList<ConstantSnapshot> snapshots,
        IReadOnlyList<ConstantSnapshot>? marked = null,
        IReadOnlySet<(nint Pointer, int Offset)>? volatileRows = null,
        int maxRows = 40)
    {
        var candidates = Candidates(snapshots, marked, volatileRows);
        var sb = new StringBuilder();

        var respondedCount = 0;
        foreach (var c in candidates)
        {
            if (c.Responded)
                respondedCount++;
        }

        sb.AppendLine($"{candidates.Count} candidate row(s). Rows built only from 0 and 1 are excluded: they are axes and flags, and they appear everywhere by being generic.");
        sb.AppendLine(marked is null
            ? "No mark to compare against. Ranked by shape and corroboration only. Mark, change the lighting, then run this again."
            : $"{respondedCount} of them responded to the lighting change and are listed first. Those are the ones with evidence behind them.");

        sb.AppendLine(volatileRows is null
            ? "NO CONTROL TAKEN: rows that change every frame on their own are still in here and will sit at the top. Run /noire3d lights baseline before changing anything."
            : $"{volatileRows.Count} row(s) known to change on their own were subtracted.");

        var listed = 0;
        foreach (var c in candidates)
        {
            if (listed++ >= maxRows)
            {
                sb.AppendLine("  ...");
                break;
            }

            var flag = c.Responded ? "RESPONDED" : "         ";
            sb.AppendLine($"  {flag}  0x{c.Pointer:X} +{c.Row.Offset,4}  {Format(c.Row.Value)}  in {c.Corroboration} buffer(s)  {c.Reason}");
        }

        if (candidates.Count == 0)
            sb.AppendLine("  (none - the light may be in a buffer larger than the tracker keeps, or not a constant at all)");

        return sb.ToString();
    }

    // Axes, identity rows and flags appear in many buffers and would top the ranking.
    private static bool IsTrivial(Vector4 v)
    {
        return Trivial(v.X) && Trivial(v.Y) && Trivial(v.Z);

        static bool Trivial(float f)
        {
            var a = Math.Abs(f);
            return a < 1e-4f || Math.Abs(a - 1f) < 1e-4f;
        }
    }

    // (0, 0, m22, m32) of a perspective projection. m22/m32 is 1/near.
    private static bool IsProjectionDepthRow(Vector4 v)
    {
        if (Math.Abs(v.X) > 1e-4f || Math.Abs(v.Y) > 1e-4f)
            return false;

        if (Math.Abs(v.Z) < 0.5f || Math.Abs(v.W) < 1e-6f)
            return false;

        var near = Math.Abs(v.Z / v.W);
        return near is > 0.1f and < 1000f;
    }

    private static string? Reason(ConstantRow row)
    {
        var v = row.Value;
        var xyz = Xyz(v);

        if (IsTrivial(v))
            return null;

        if (IsProjectionDepthRow(v))
            return null;

        if (row.Kind.HasFlag(RowKind.Normalized)
            && Math.Abs(v.W - 1f) < 1e-4f
            && (Math.Abs(xyz.X - xyz.Y) > 1e-3f || Math.Abs(xyz.Y - xyz.Z) > 1e-3f))
            return "colour-shaped (rgb in 0..1, w=1, non-grey)";

        if (row.Kind.HasFlag(RowKind.UnitVector))
            return "direction-shaped (unit vector outside any rotation)";

        return null;
    }

    // Buckets xyz against float noise.
    private static (int, int, int) Quantize(Vector4 v)
        => ((int)MathF.Round(v.X * 2048f), (int)MathF.Round(v.Y * 2048f), (int)MathF.Round(v.Z * 2048f));

    private static bool Differs(Vector4 a, Vector4 b)
        => Math.Abs(a.X - b.X) > ChangeEpsilon
        || Math.Abs(a.Y - b.Y) > ChangeEpsilon
        || Math.Abs(a.Z - b.Z) > ChangeEpsilon
        || Math.Abs(a.W - b.W) > ChangeEpsilon;

    private static bool InUnit(float f) => f is >= 0f and <= 1f;

    private static bool IsFinite(Vector4 v)
        => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W);

    private static string Format(Vector4 v) => $"({v.X,9:F4},{v.Y,9:F4},{v.Z,9:F4},{v.W,9:F4})";
}
