using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// One light read out of a payload the game uploaded for a frame.
/// </summary>
/// <param name="Position">Row 0, relative to the camera. Add the camera's own position to place it in the world.</param>
/// <param name="Direction">Row 1. Unit length, and matching the source object's forward axis in the capture that identified this layout.</param>
/// <param name="Color">Rows 2 and 3, which the game writes identically. Values above 1 are ordinary here - only an emitter has them.</param>
/// <param name="Radius">The reciprocal of the light volume's uniform scale, which is how such a volume encodes its reach.</param>
/// <param name="TransformDisagreement">
/// How far the volume transform's own idea of where the light is falls from <paramref name="Position"/>.<br/>
/// The two are independent encodings of one point, so this is near zero while the layout holds and grows if it
/// stops holding. It is a running check on the parse rather than a property of the light.
/// </param>
internal readonly record struct GameLight(
    Vector3 Position,
    Vector3 Direction,
    Vector3 Color,
    float Radius,
    float TransformDisagreement)
{
    /// <summary>The brightest channel, which is what separates a lit lamp from one contributing nothing.</summary>
    public float Intensity => MathF.Max(Color.X, MathF.Max(Color.Y, Color.Z));

    /// <summary>Whether this light contributes anything at all.</summary>
    public bool IsLit => Intensity > 0.0001f;

    /// <summary>
    /// Whether this is the scene's directional light rather than a lamp.<br/>
    /// A directional light has nowhere to be and no reach to limit, and the game says so by leaving both fields
    /// at their identity: no position and a volume scale of one. Measured across a day/night pair in the same
    /// room, where this record alone went from black to <c>(1.960, 1.869, 1.765)</c> while every lamp held still.
    /// </summary>
    public bool IsDirectional => Radius is > 0.99f and < 1.01f && Position.LengthSquared() < 0.0001f;

    /// <summary>Places this light in the world, given where the camera was when it was captured.</summary>
    /// <param name="camera">The camera position the capture was relative to.</param>
    public Vector3 WorldPosition(Vector3 camera) => IsDirectional ? Position : Position + camera;
}

/// <summary>
/// Reads the game's per-light records out of the payloads a write-log run captured.<br/>
/// <b>How this layout was established.</b> Not by recognising shapes - that route produced two confident wrong
/// readings of the specular map before it was abandoned. A lamp was removed from the room between two captures,
/// and one payload vanished. The object's own transform was zeroed in the same capture, and the third column of
/// that transform is byte-identical to row 1 of the payload that disappeared, which ties the record to the
/// object that went away. That is what makes this a measurement rather than another guess.
/// </summary>
internal static class GameLightHarvest
{
    /// <summary>Bytes in one record. The whole 512 B buffer carries a single light, with the tail rows unused.</summary>
    public const int RecordBytes = 512;

    /// <summary>Rows in one record.</summary>
    private const int RecordRows = RecordBytes / 16;

    /// <summary>Row holding the position.</summary>
    private const int PositionRow = 0;

    /// <summary>Row holding the direction.</summary>
    private const int DirectionRow = 1;

    /// <summary>First of the two rows holding the colour.</summary>
    private const int ColorRow = 2;

    /// <summary>First of the three rows holding the light's transform.</summary>
    private const int TransformRow = 13;

    /// <summary>How far from unit length a direction may be and still count as one.</summary>
    private const float DirectionTolerance = 0.01f;

    /// <summary>
    /// Reads a payload as a light, if it is one.<br/>
    /// The two tests together are what keep the material-parameter buffers out. Those share the 512 B class and
    /// would otherwise be picked up in quantity: they satisfy the duplicated-colour test easily, since whole runs
    /// of their rows are <c>(1, 1, 1, 1)</c>, but their row 1 has a length of 1.73 rather than 1.
    /// </summary>
    /// <param name="payload">A captured 512 B payload.</param>
    /// <param name="light">The light read out of it.</param>
    public static bool TryParse(byte[] payload, out GameLight light)
    {
        light = default;

        if (payload is null || payload.Length < RecordBytes)
            return false;

        var direction = Row(payload, DirectionRow);
        var axis = new Vector3(direction.X, direction.Y, direction.Z);
        if (MathF.Abs(axis.Length() - 1f) > DirectionTolerance)
            return false;

        // The game writes the diffuse and specular colours identically. Two adjacent rows agreeing exactly is a
        // convention rather than a coincidence, and it is the one part of the layout visible in every record.
        var color = Row(payload, ColorRow);
        if (color != Row(payload, ColorRow + 1))
            return false;

        var row0 = Row(payload, PositionRow);
        var position = new Vector3(row0.X, row0.Y, row0.Z);

        ReadTransform(payload, out var volumeCentre, out var radius);

        light = new GameLight(
            position,
            axis,
            new Vector3(color.X, color.Y, color.Z),
            radius,
            Vector3.Distance(position, volumeCentre));

        return true;
    }

    /// <summary>
    /// Reads the light volume's reach and where it sits.<br/>
    /// Rows 13-15 are a world-to-volume transform: a rotation scaled uniformly, with the translation in the `w`
    /// slots. Its scale is the reciprocal of the light's reach, because the volume is a unit shape stretched to
    /// that reach.<br/>
    /// <b>The translation is not the position.</b> It is expressed in the volume's own rotated frame, so reading
    /// it directly - which an earlier version of this did - yields a point that wanders as the light turns. The
    /// rotation has to be undone: for <c>M = [R*s | t]</c>, the centre is <c>-(R^T/s)*t</c>. Doing that on the
    /// capture that identified this layout gives <c>(0.207, 0.078, -4.696)</c> against a row 0 of
    /// <c>(0.199, 0.073, -4.701)</c>, which is how the two fields were shown to be one point rather than two.
    /// </summary>
    private static void ReadTransform(byte[] payload, out Vector3 centre, out float radius)
    {
        centre = Vector3.Zero;
        radius = 0f;

        if (payload.Length < (TransformRow + 3) * 16)
            return;

        var a = Row(payload, TransformRow);
        var b = Row(payload, TransformRow + 1);
        var c = Row(payload, TransformRow + 2);

        var scale = new Vector3(a.X, a.Y, a.Z).Length();
        if (scale <= 0.0001f)
            return;

        radius = 1f / scale;

        // Transposing the rotation inverts it, since it is orthonormal once the scale is divided out.
        var t = new Vector3(a.W, b.W, c.W);
        centre = -new Vector3(
            (a.X * t.X) + (b.X * t.Y) + (c.X * t.Z),
            (a.Y * t.X) + (b.Y * t.Y) + (c.Y * t.Z),
            (a.Z * t.X) + (b.Z * t.Y) + (c.Z * t.Z)) / (scale * scale);
    }

    /// <summary>Reads every light out of a captured payload set.</summary>
    /// <param name="payloads">Distinct payloads from a write-log run restricted to the 512 B class.</param>
    public static List<GameLight> FromPayloads(IReadOnlyList<byte[]> payloads)
    {
        var lights = new List<GameLight>();
        foreach (var payload in payloads)
        {
            if (TryParse(payload, out var light))
                lights.Add(light);
        }

        // Brightest first: in a room of many lamps the ones that matter to a nearby object are at the top.
        lights.Sort((x, y) => y.Intensity.CompareTo(x.Intensity));
        return lights;
    }

    /// <summary>Reports the harvested lights, brightest first.</summary>
    /// <param name="lights">The lights read from a capture.</param>
    /// <param name="candidates">How many payloads were examined, for saying how many were rejected.</param>
    public static string Describe(IReadOnlyList<GameLight> lights, int candidates)
    {
        var sb = new StringBuilder();

        if (lights.Count == 0)
        {
            sb.AppendLine($"No light records in {candidates} payload(s). Record with '/noire3d lights writes 512' first - a run over any other size cannot contain them.");
            return sb.ToString();
        }

        var lit = 0;
        foreach (var light in lights)
        {
            if (light.IsLit)
                lit++;
        }

        sb.AppendLine($"{lights.Count} light record(s) from {candidates} payload(s), {lit} of them contributing. Brightest first.");
        sb.AppendLine("Positions are relative to the camera - add the camera's own position for world space.");
        sb.AppendLine();

        var worst = 0f;

        for (var i = 0; i < lights.Count; i++)
        {
            var light = lights[i];
            var kind = light.IsDirectional ? "directional" : $"radius {light.Radius,7:F3}";
            worst = MathF.Max(worst, light.TransformDisagreement);

            sb.AppendLine($"[{i + 1}] colour ({light.Color.X:F3}, {light.Color.Y:F3}, {light.Color.Z:F3}){(light.IsLit ? string.Empty : "  (contributes nothing)")}");
            sb.AppendLine($"    direction ({light.Direction.X,7:F3},{light.Direction.Y,7:F3},{light.Direction.Z,7:F3})   {kind}");

            if (!light.IsDirectional)
                sb.AppendLine($"    position  ({light.Position.X,7:F3},{light.Position.Y,7:F3},{light.Position.Z,7:F3})");
        }

        sb.AppendLine();

        // The parse rests on row 0 and the volume transform being one point. Reporting how far apart they drifted
        // means a layout that quietly stops holding announces itself instead of producing plausible nonsense.
        sb.AppendLine(worst < 0.05f
            ? $"Layout check: the position row and the volume transform agree to within {worst:F3} units, so the parse holds."
            : $"LAYOUT CHECK FAILED: the position row and the volume transform disagree by up to {worst:F3} units. The record is not laid out the way this assumes and the values above cannot be trusted.");

        return sb.ToString();
    }

    /// <summary>Reads one 16-byte row of a payload.</summary>
    private static Vector4 Row(byte[] payload, int index)
    {
        var offset = index * 16;
        return new Vector4(
            BitConverter.ToSingle(payload, offset),
            BitConverter.ToSingle(payload, offset + 4),
            BitConverter.ToSingle(payload, offset + 8),
            BitConverter.ToSingle(payload, offset + 12));
    }
}
