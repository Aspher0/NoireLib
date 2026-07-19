using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace NoireLib.Draw3D.Core;

/// <summary>One 16-byte row of a game constant buffer, with what its contents could plausibly be.</summary>
/// <param name="Offset">Byte offset of the row within its buffer.</param>
/// <param name="Value">The four floats at that offset.</param>
/// <param name="Kind">What the row's shape allows it to be.</param>
internal readonly record struct ConstantRow(int Offset, Vector4 Value, RowKind Kind);

/// <summary>
/// What a constant-buffer row's shape allows it to be. A classification narrows the candidates; it never
/// identifies a row on its own, because many unrelated values share a shape.
/// </summary>
[Flags]
internal enum RowKind
{
    /// <summary>Nothing recognizable, or all zero.</summary>
    None = 0,

    /// <summary>Every component is zero.</summary>
    Zero = 1,

    /// <summary>The xyz components form a unit vector: a direction, possibly a light's.</summary>
    UnitVector = 2,

    /// <summary>The xyz components are non-negative and within a plausible color range.</summary>
    ColorLike = 4,

    /// <summary>Every component is between 0 and 1.</summary>
    Normalized = 8,

    /// <summary>At least one component is large enough to be a position or a matrix entry rather than a color.</summary>
    Large = 16,

    /// <summary>
    /// The row is one axis of an orthonormal basis formed with its neighbours: a rotation, so transform data.<br/>
    /// This is the single most useful exclusion. A rotation's rows are unit vectors, so shape alone calls every one
    /// of them a possible light direction, and a frame buffer full of view matrices then drowns anything real.
    /// </summary>
    MatrixRow = 32,
}

/// <summary>A buffer's rows at one moment, kept so two moments can be compared.</summary>
/// <param name="Pointer">The buffer's resource pointer, which identifies it across snapshots.</param>
/// <param name="ByteWidth">The buffer's declared size.</param>
/// <param name="Rows">Its rows, classified.</param>
/// <param name="Captures">
/// How many whole payloads had been copied into this buffer when the snapshot was taken.<br/>
/// Two snapshots with the same count hold the same bytes by construction, so a comparison between them says
/// nothing. Without this, a tracker that had stopped copying would report every row as unchanged, which reads
/// exactly like a result.
/// </param>
internal sealed record ConstantSnapshot(nint Pointer, int ByteWidth, ConstantRow[] Rows, long Captures);

/// <summary>
/// Reads the game's tracked constant buffers and reports what is in them, so the values that drive its
/// lighting can be found rather than guessed at.<br/>
/// <b>Why this exists:</b> matching the game's light by hand never converges, and the constants that
/// produce it are already flowing through the buffers the camera capture tracks. What is missing is
/// knowing which bytes they are. A single dump cannot say - too many rows share a shape - so this also
/// diffs two moments: a row that moves when the light moves and holds still otherwise is a candidate,
/// and one that never moves is not a light at all.
/// </summary>
internal static class LightConstantProbe
{
    /// <summary>A direction's length may drift this far from 1 and still count as normalized.</summary>
    private const float UnitTolerance = 0.02f;

    /// <summary>Above this, a component is a position or a matrix entry rather than a color or a direction.</summary>
    private const float LargeThreshold = 8f;

    /// <summary>Components differing by less than this are treated as unchanged between two snapshots.</summary>
    private const float ChangeEpsilon = 1e-4f;

    /// <summary>Two unit vectors whose dot product is under this are treated as perpendicular.</summary>
    private const float OrthogonalTolerance = 0.03f;

    /// <summary>Classifies every row of one buffer.</summary>
    /// <param name="pointer">The buffer's resource pointer.</param>
    /// <param name="bytes">Its contents.</param>
    /// <param name="validBytes">How much of <paramref name="bytes"/> was actually written.</param>
    /// <param name="captures">How many whole payloads had been copied into the buffer, so staleness is detectable.</param>
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

    /// <summary>
    /// Flags every run of three consecutive rows whose xyz form an orthonormal basis.<br/>
    /// A rotation matrix is three mutually perpendicular unit vectors, and testing that is what separates a view
    /// matrix from a light direction: both are unit vectors, but only one comes with two perpendicular partners.
    /// </summary>
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

    /// <summary>What a single row's shape allows it to be.</summary>
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

    /// <summary>
    /// Rows that changed between two snapshots of the same buffer.<br/>
    /// This is the discriminating step: taken across a change in the game's light (a different time of day,
    /// indoors against outdoors), the rows that moved are the short list, and everything constant is ruled out.
    /// </summary>
    /// <param name="before">The earlier snapshot.</param>
    /// <param name="after">The later snapshot of the same buffer.</param>
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

    /// <summary>
    /// Renders a snapshot for the log, listing only the rows whose shape allows them to be a light value.<br/>
    /// Zero rows and matrix-sized runs are dropped: they are the bulk of a frame buffer and none of them is
    /// a colour or a direction.
    /// </summary>
    /// <param name="snapshot">The snapshot to describe.</param>
    /// <param name="maxRows">How many rows to list at most.</param>
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

    /// <summary>Renders the rows that moved between two snapshots, which is the short list worth investigating.</summary>
    /// <param name="before">The earlier snapshot.</param>
    /// <param name="after">The later snapshot of the same buffer.</param>
    /// <param name="maxRows">How many rows to list at most.</param>
    public static string DescribeChanges(ConstantSnapshot before, ConstantSnapshot after, int maxRows = 32)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        // Identical bytes because nothing was captured is not the same finding as identical bytes because
        // nothing moved, and the two are indistinguishable from the rows alone.
        if (after.Captures == before.Captures)
        {
            return $"buffer 0x{after.Pointer:X}: NOT RE-CAPTURED since the mark ({after.Captures} payload copies both times) - "
                 + "these are the same bytes, so no conclusion can be drawn. Keep the capture armed across the whole comparison.\n";
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

            // Transform data moves every frame and says nothing about the light: a row large at both ends is a
            // position or a projection entry, and one that sits in an orthonormal basis at either end is a rotation.
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

    /// <summary>A row that could be a light value, with why it is worth looking at.</summary>
    /// <param name="Pointer">Buffer it was found in.</param>
    /// <param name="Row">The row itself.</param>
    /// <param name="Reason">What makes it a candidate.</param>
    /// <param name="Corroboration">How many other buffers hold the same xyz. A value shared across buffers is far stronger than one seen once.</param>
    /// <param name="Responded">Whether this row changed when the lighting was changed, which is the only direct evidence available.</param>
    internal readonly record struct LightCandidate(nint Pointer, ConstantRow Row, string Reason, int Corroboration, bool Responded);

    /// <summary>
    /// Ranks the rows across every buffer that could carry a light, applying the tests that survived the first
    /// two rounds of looking at this by hand.<br/>
    /// <b>Colour:</b> three components inside 0..1 that are not all equal, with w exactly 1. A light's colour is a
    /// tint with unit weight; a grey triple is more likely a scale, and a w that is not 1 is usually a distance.<br/>
    /// <b>Direction:</b> a unit vector that is not part of a contiguous rotation.<br/>
    /// <b>Corroboration across buffers is the strongest signal available</b> without knowing the layout: the game
    /// feeds the same light to several passes, so a direction appearing in more than one buffer is far more likely
    /// real than one seen once. A matrix row repeated by coincidence does not survive this.
    /// </summary>
    /// <param name="snapshots">Every buffer's current contents.</param>
    /// <param name="marked">
    /// Optional earlier snapshots taken before the lighting was changed. When given, a row that moved is ranked
    /// above every row that did not: <b>responding to a lighting change is the only direct evidence here</b>, and
    /// shape alone has already produced two confident wrong answers on this project.
    /// </param>
    /// <param name="volatileRows">
    /// Rows already known to change on their own, from a control comparison taken with nothing altered.<br/>
    /// <b>Without this the responded flag is close to worthless</b>: a jittered sample kernel changes every frame,
    /// so it moves across any comparison and outranks everything that moved for a reason.
    /// </param>
    public static IReadOnlyList<LightCandidate> Candidates(
        IReadOnlyList<ConstantSnapshot> snapshots,
        IReadOnlyList<ConstantSnapshot>? marked = null,
        IReadOnlySet<(nint Pointer, int Offset)>? volatileRows = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        // Rows that moved between the mark and now, by buffer and offset.
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
                        // A row that moves on its own tells us nothing by moving again.
                        if (volatileRows is not null && volatileRows.Contains((after.Pointer, a.Offset)))
                            continue;

                        responded.Add((after.Pointer, a.Offset));
                    }

                    break;
                }
            }
        }

        // How many distinct buffers hold each xyz, so a shared value can be told from a one-off.
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

        // Responding to the lighting change outranks everything: it is evidence rather than shape. Corroboration
        // only breaks ties among rows that are equally direct.
        candidates.Sort((a, b) => a.Responded != b.Responded
            ? b.Responded.CompareTo(a.Responded)
            : b.Corroboration.CompareTo(a.Corroboration));

        return candidates;
    }

    /// <summary>
    /// Every row that differs between two snapshots taken with nothing deliberately changed: the rows that move
    /// on their own.<br/>
    /// This is the control the search needs. The game jitters sample kernels per frame and animates constants
    /// nothing asked it to, and those rows move across any comparison, so without subtracting them a lighting
    /// comparison reports mostly noise ranked at the top.
    /// </summary>
    /// <param name="before">The earlier snapshots.</param>
    /// <param name="after">Later snapshots taken with nothing altered in between.</param>
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

    /// <summary>Renders the ranked candidates for the log.</summary>
    /// <param name="snapshots">Every buffer's current contents.</param>
    /// <param name="maxRows">How many to list at most.</param>
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
            ? "No mark to compare against, so these are ranked by shape and corroboration only. Mark, change the lighting, then run this again - a row that responds is worth more than any number of buffers agreeing."
            : $"{respondedCount} of them responded to the lighting change and are listed first. Those are the ones with evidence behind them.");

        sb.AppendLine(volatileRows is null
            ? "NO CONTROL TAKEN: rows that change every frame on their own are still in here and will sit at the top. Run /noire3d lights baseline before changing anything."
            : $"{volatileRows.Count} row(s) known to change on their own were subtracted, so what responded did so for a reason.");

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

    /// <summary>
    /// Whether every component is 0 or plus/minus 1: a canonical axis, an identity row, or a flag.<br/>
    /// These must be excluded before ranking by corroboration, because they appear in many buffers precisely
    /// by being generic. Ranking on how widely a value is shared otherwise puts every default axis above the
    /// real measurement, which is what the first ranked run did.
    /// </summary>
    private static bool IsTrivial(Vector4 v)
    {
        return Trivial(v.X) && Trivial(v.Y) && Trivial(v.Z);

        static bool Trivial(float f)
        {
            var a = Math.Abs(f);
            return a < 1e-4f || Math.Abs(a - 1f) < 1e-4f;
        }
    }

    /// <summary>
    /// Whether a row is the depth pair of a projection matrix: <c>(0, 0, m22, m32)</c>.<br/>
    /// A perspective projection puts <c>far/(near-far)</c> and <c>near*far/(near-far)</c> in those two slots, so
    /// their ratio is exactly <c>1/near</c> and the third component sits near 1. Such a row is a unit vector by
    /// shape and moves whenever a shadow frustum is refitted, which makes it a convincing false positive: it was
    /// the entire responded list once the per-frame noise had been removed.
    /// </summary>
    private static bool IsProjectionDepthRow(Vector4 v)
    {
        if (Math.Abs(v.X) > 1e-4f || Math.Abs(v.Y) > 1e-4f)
            return false;

        if (Math.Abs(v.Z) < 0.5f || Math.Abs(v.W) < 1e-6f)
            return false;

        // A near plane between a millimetre and ten metres covers every frustum the game plausibly builds.
        var near = Math.Abs(v.Z / v.W);
        return near is > 0.1f and < 1000f;
    }

    /// <summary>Why a row is worth looking at, or null when it is not.</summary>
    private static string? Reason(ConstantRow row)
    {
        var v = row.Value;
        var xyz = Xyz(v);

        // A light's direction and colour are measurements, so their components are fractional. A row built
        // only from 0 and 1 is a default, an identity row or a flag.
        if (IsTrivial(v))
            return null;

        if (IsProjectionDepthRow(v))
            return null;

        // A colour is a tint with unit weight. All-equal components are more likely a uniform scale.
        if (row.Kind.HasFlag(RowKind.Normalized)
            && Math.Abs(v.W - 1f) < 1e-4f
            && (Math.Abs(xyz.X - xyz.Y) > 1e-3f || Math.Abs(xyz.Y - xyz.Z) > 1e-3f))
            return "colour-shaped (rgb in 0..1, w=1, not grey)";

        if (row.Kind.HasFlag(RowKind.UnitVector))
            return "direction-shaped (unit vector outside any rotation)";

        return null;
    }

    /// <summary>Buckets an xyz so the same value written by two passes compares equal despite float noise.</summary>
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
