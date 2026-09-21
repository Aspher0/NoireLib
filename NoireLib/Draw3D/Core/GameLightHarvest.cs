using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace NoireLib.Draw3D.Core;

internal readonly record struct GameLight(
    Vector3 Position,
    Vector3 Direction,
    Vector3 Color,
    float Radius,
    float TransformDisagreement)
{
    public float Intensity => MathF.Max(Color.X, MathF.Max(Color.Y, Color.Z));

    public bool IsLit => Intensity > 0.0001f;

    // The game leaves a directional light's position at zero and its volume scale at one.
    public bool IsDirectional => Radius is > 0.99f and < 1.01f && Position.LengthSquared() < 0.0001f;

    // Captured positions are camera-relative.
    public Vector3 WorldPosition(Vector3 camera) => IsDirectional ? Position : Position + camera;
}

internal static class GameLightHarvest
{
    // One 512 B buffer carries a single light.
    public const int RecordBytes = 512;

    private const int RecordRows = RecordBytes / 16;

    private const int PositionRow = 0;

    private const int DirectionRow = 1;

    private const int ColorRow = 2;

    private const int TransformRow = 13;

    private const float DirectionTolerance = 0.01f;

    // Material-parameter buffers pass the colour test. Their row 1 has length 1.73.
    public static bool TryParse(byte[] payload, out GameLight light)
    {
        light = default;

        if (payload is null || payload.Length < RecordBytes)
            return false;

        var direction = Row(payload, DirectionRow);
        var axis = new Vector3(direction.X, direction.Y, direction.Z);
        if (MathF.Abs(axis.Length() - 1f) > DirectionTolerance)
            return false;

        // The game writes the diffuse and specular colours identically, in adjacent rows.
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

    // Rows 13-15 are M = [R*s | t] with s = 1/reach. The centre is -(R^T/s)*t.
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

        var t = new Vector3(a.W, b.W, c.W);
        centre = -new Vector3(
            (a.X * t.X) + (b.X * t.Y) + (c.X * t.Z),
            (a.Y * t.X) + (b.Y * t.Y) + (c.Y * t.Z),
            (a.Z * t.X) + (b.Z * t.Y) + (c.Z * t.Z)) / (scale * scale);
    }

    public static List<GameLight> FromPayloads(IReadOnlyList<byte[]> payloads)
    {
        var lights = new List<GameLight>();
        foreach (var payload in payloads)
        {
            if (TryParse(payload, out var light))
                lights.Add(light);
        }

        lights.Sort((x, y) => y.Intensity.CompareTo(x.Intensity));
        return lights;
    }

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

        // A disagreement means the layout has changed.
        sb.AppendLine(worst < 0.05f
            ? $"Layout check: the position row and the volume transform agree to within {worst:F3} units. The parse holds."
            : $"LAYOUT CHECK FAILED: the position row and the volume transform disagree by up to {worst:F3} units. The record is not laid out the way this assumes and the values above cannot be trusted.");

        return sb.ToString();
    }

    private static Vector4 Row(byte[] payload, int index)
        => BufferHelper.ReadVector4(payload, index * 16);
}
