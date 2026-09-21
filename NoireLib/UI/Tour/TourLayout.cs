using System;
using System.Numerics;

namespace NoireLib.UI;

internal static class TourLayout
{
    public static Vector4 Place(Vector2 viewMin, Vector2 viewMax, Vector4 spotlight, bool hasTarget, Vector2 size, TourPlacement placement, float gap)
    {
        if (!hasTarget)
        {
            var centre = viewMin + ((viewMax - viewMin) * 0.5f) - (size * 0.5f);
            return Sized(centre, size);
        }

        if (placement == TourPlacement.Auto)
        {
            if (spotlight.W + gap + size.Y <= viewMax.Y)
                placement = TourPlacement.Below;
            else if (spotlight.Y - gap - size.Y >= viewMin.Y)
                placement = TourPlacement.Above;
            else if (spotlight.Z + gap + size.X <= viewMax.X)
                placement = TourPlacement.Right;
            else
                placement = TourPlacement.Left;
        }

        var position = placement switch
        {
            TourPlacement.Above => new Vector2(((spotlight.X + spotlight.Z) * 0.5f) - (size.X * 0.5f), spotlight.Y - gap - size.Y),
            TourPlacement.Left => new Vector2(spotlight.X - gap - size.X, ((spotlight.Y + spotlight.W) * 0.5f) - (size.Y * 0.5f)),
            TourPlacement.Right => new Vector2(spotlight.Z + gap, ((spotlight.Y + spotlight.W) * 0.5f) - (size.Y * 0.5f)),
            _ => new Vector2(((spotlight.X + spotlight.Z) * 0.5f) - (size.X * 0.5f), spotlight.W + gap),
        };

        position.X = Math.Clamp(position.X, viewMin.X, Math.Max(viewMin.X, viewMax.X - size.X));
        position.Y = Math.Clamp(position.Y, viewMin.Y, Math.Max(viewMin.Y, viewMax.Y - size.Y));

        return Sized(position, size);
    }

    public static Vector4 Intersect(Vector4 rect, Vector4 clip)
    {
        if (IsEmpty(clip))
            return rect;

        return new Vector4(
            MathF.Max(rect.X, clip.X),
            MathF.Max(rect.Y, clip.Y),
            MathF.Min(rect.Z, clip.Z),
            MathF.Min(rect.W, clip.W));
    }

    public static bool IsEmpty(Vector4 rect) => rect.Z <= rect.X || rect.W <= rect.Y;

    public static TourDirection DirectionTo(Vector4 rect, Vector4 clip)
    {
        if (rect.W <= clip.Y)
            return TourDirection.Up;

        if (rect.Y >= clip.W)
            return TourDirection.Down;

        return rect.Z <= clip.X ? TourDirection.Left : TourDirection.Right;
    }

    public static Vector4 EdgeOf(Vector4 clip, TourDirection direction, float thickness)
        => direction switch
        {
            TourDirection.Up => new Vector4(clip.X, clip.Y, clip.Z, clip.Y + thickness),
            TourDirection.Down => new Vector4(clip.X, clip.W - thickness, clip.Z, clip.W),
            TourDirection.Left => new Vector4(clip.X, clip.Y, clip.X + thickness, clip.W),
            _ => new Vector4(clip.Z - thickness, clip.Y, clip.Z, clip.W),
        };

    public static Vector4 MovedTo(Vector4 rect, Vector2 position)
        => new(position.X, position.Y, position.X + (rect.Z - rect.X), position.Y + (rect.W - rect.Y));

    public static Vector4 Padded(Vector4 rect, float padding)
        => new(rect.X - padding, rect.Y - padding, rect.Z + padding, rect.W + padding);

    public static bool Contains(Vector4 rect, Vector2 point)
        => point.X >= rect.X && point.X <= rect.Z && point.Y >= rect.Y && point.Y <= rect.W;

    private static Vector4 Sized(Vector2 position, Vector2 size)
        => new(position.X, position.Y, position.X + size.X, position.Y + size.Y);
}
