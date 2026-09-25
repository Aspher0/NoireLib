using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

public sealed partial class NoireMotion
{
    private const float Tau = MathF.PI * 2f;

    // The movement of the whole drawing at one moment: scale, slant and turn around a pivot, then a shift.
    private readonly struct Pose(Matrix3x2 transform, Vector2 offset)
    {
        public readonly Matrix3x2 Transform = transform;
        public readonly Vector2 Offset = offset;

        public Vector2 Apply(Vector2 point, Vector2 pivot) => pivot + Vector2.Transform(point - pivot, Transform) + Offset;
    }

    private bool MovesPieces => glyphWaveHeight != 0f || wobbleDegrees != 0f || jitterIntensity != 0f || glitchIntensity != 0f || hasReveal;

    #region Measuring

    /// <summary>The box a rectangle takes with this motion now. A motion waiting for a trigger measures at rest.</summary>
    /// <param name="min">The top left the layout gave the drawing.</param>
    /// <param name="max">The bottom right the layout gave the drawing.</param>
    /// <returns>The box after the motion.</returns>
    public (Vector2 Min, Vector2 Max) Measure(Vector2 min, Vector2 max)
    {
        var reduced = NoireUI.ReducedMotion && !ignoreReducedMotion;
        var moving = trigger == EffectTrigger.Always && !reduced;
        var pose = PoseAt(moving ? NoireUI.Time : 0f, moving);
        var pivotPoint = min + (pivot * (max - min));

        var (boxMin, boxMax) = Corners(pose, pivotPoint, min, max);
        var margin = moving ? PieceMargin() : Vector2.Zero;

        return (boxMin - margin, boxMax + margin);
    }

    /// <summary>The size a drawing takes on screen with this motion right now.</summary>
    /// <param name="size">The size the layout gave the drawing.</param>
    /// <returns>The size after the motion.</returns>
    public Vector2 Measure(Vector2 size)
    {
        var (min, max) = Measure(Vector2.Zero, size);
        return max - min;
    }

    /// <summary>
    /// The largest box a rectangle ever takes on screen with this motion, whatever the moment: reserve it once and the
    /// page makes room for the motion without moving while it runs.
    /// </summary>
    /// <param name="min">The top left corner the layout gave the drawing.</param>
    /// <param name="max">The bottom right corner the layout gave the drawing.</param>
    /// <returns>A box the drawing never leaves.</returns>
    public (Vector2 Min, Vector2 Max) Envelope(Vector2 min, Vector2 max)
    {
        var pivotPoint = min + (pivot * (max - min));
        var largest = MathF.Max(MathF.Abs(scaleMin), MathF.Abs(scaleMax));
        var scale = Vector2.Abs(staticScale) * (hasScalePulse ? largest : 1f) * (1f + MathF.Abs(squashAmount));
        var skew = MathF.Abs(staticSkew) + MathF.Abs(skewAmount);

        Vector2 boxMin;
        Vector2 boxMax;

        if (spinSpeed != 0f)
        {
            var reach = 0f;

            foreach (var corner in CornersOf(min, max))
                reach = MathF.Max(reach, ((corner - pivotPoint) * scale).Length());

            reach *= 1f + skew;
            boxMin = pivotPoint - new Vector2(reach);
            boxMax = pivotPoint + new Vector2(reach);
        }
        else
        {
            boxMin = new Vector2(float.MaxValue);
            boxMax = new Vector2(float.MinValue);

            // The rock and the lean reach their extremes at either end and pass through the middle.
            for (var step = -4; step <= 4; step++)
            {
                var degrees = staticRotation + (MathF.Abs(rockDegrees) * step / 4f);

                for (var lean = -1; lean <= 1; lean++)
                {
                    var matrix = Matrix3x2.CreateScale(scale) * Shear(staticSkew + (skewAmount * lean)) * Matrix3x2.CreateRotation(degrees * (MathF.PI / 180f));
                    var (cornerMin, cornerMax) = Corners(new Pose(matrix, Vector2.Zero), pivotPoint, min, max);

                    boxMin = Vector2.Min(boxMin, cornerMin);
                    boxMax = Vector2.Max(boxMax, cornerMax);
                }
            }
        }

        var drift = new Vector2(
            MathF.Abs(swayPixels) + MathF.Abs(orbitRadius) + MathF.Abs(shakeIntensity),
            MathF.Abs(floatPixels) + MathF.Abs(orbitRadius) + MathF.Abs(shakeIntensity));

        boxMin += staticOffset - drift - new Vector2(0f, MathF.Abs(bounceHeight));
        boxMax += staticOffset + drift;

        var margin = PieceMargin();
        return (boxMin - margin, boxMax + margin);
    }

    /// <summary>The largest size a drawing ever takes on screen with this motion.</summary>
    /// <param name="size">The size the layout gave the drawing.</param>
    /// <returns>A size the drawing never exceeds.</returns>
    public Vector2 Envelope(Vector2 size)
    {
        var (min, max) = Envelope(Vector2.Zero, size);
        return max - min;
    }

    // How far a single character can move on its own, which no whole-drawing transform accounts for.
    private Vector2 PieceMargin()
    {
        var sideways = MathF.Abs(jitterIntensity) + MathF.Abs(glitchIntensity);
        var upwards = MathF.Abs(glyphWaveHeight) + MathF.Abs(jitterIntensity) + (MathF.Abs(glitchIntensity) * 0.25f);
        return new Vector2(sideways, upwards);
    }

    private static (Vector2 Min, Vector2 Max) Corners(Pose pose, Vector2 pivotPoint, Vector2 min, Vector2 max)
    {
        var boxMin = new Vector2(float.MaxValue);
        var boxMax = new Vector2(float.MinValue);

        foreach (var corner in CornersOf(min, max))
        {
            var moved = pose.Apply(corner, pivotPoint);
            boxMin = Vector2.Min(boxMin, moved);
            boxMax = Vector2.Max(boxMax, moved);
        }

        return (boxMin, boxMax);
    }

    private static CornerSet CornersOf(Vector2 min, Vector2 max) => new(min, max);

    // The four corners of a rectangle, enumerable without allocating.
    private readonly struct CornerSet(Vector2 min, Vector2 max)
    {
        public Enumerator GetEnumerator() => new(min, max);

        public struct Enumerator(Vector2 min, Vector2 max)
        {
            private int index = -1;

            public readonly Vector2 Current => index switch
            {
                0 => min,
                1 => new Vector2(max.X, min.Y),
                2 => max,
                _ => new Vector2(min.X, max.Y),
            };

            public bool MoveNext() => ++index < 4;
        }
    }

    #endregion

    #region Applying

    internal void Apply(Span<ImDrawVert> vertices, in EffectContext context)
    {
        NoireEffects.ResolveArea(area, rectMin, rectMax, in context, out var areaMin, out var areaMax);

        var time = NoireEffects.AnimationTime(trigger, triggerDuration, ignoreReducedMotion, in context, areaMin, areaMax, out var moving);
        var pose = PoseAt(time, moving);
        var areaPivot = areaMin + (pivot * (areaMax - areaMin));
        var perPiece = area == EffectArea.Glyph || (moving && MovesPieces) || (hasReveal && !moving && trigger == EffectTrigger.Always);
        var white = context.White;

        if (!perPiece)
        {
            foreach (ref var vertex in vertices)
            {
                if (NoireEffects.Targets(target, vertex.Uv != white))
                    vertex.Pos = pose.Apply(vertex.Pos, areaPivot);
            }

            return;
        }

        var piece = 0;

        for (var start = 0; start < vertices.Length; piece++)
        {
            var end = NoireEffects.PieceEnd(vertices, start, white, out var pieceMin, out var pieceMax);
            var pieceCenter = (pieceMin + pieceMax) * 0.5f;
            var local = PieceMatrix(piece, context.PieceCount, time, moving, context.LineHeight, out var shift);
            var pivotPoint = area == EffectArea.Glyph ? pieceMin + (pivot * (pieceMax - pieceMin)) : areaPivot;

            for (var i = start; i < end; i++)
            {
                ref var vertex = ref vertices[i];

                if (!NoireEffects.Targets(target, vertex.Uv != white))
                    continue;

                var point = pieceCenter + Vector2.Transform(vertex.Pos - pieceCenter, local) + shift;
                vertex.Pos = pose.Apply(point, pivotPoint);
            }

            start = end;
        }
    }

    private Pose PoseAt(float time, bool moving)
    {
        var degrees = staticRotation;
        var scale = staticScale;
        var skew = staticSkew;
        var offset = staticOffset;

        if (moving)
        {
            degrees += spinSpeed * time;

            if (rockDegrees != 0f)
                degrees += rockDegrees * Signed(rockWave, time * rockSpeed);

            if (hasScalePulse)
                scale *= scaleMin + ((scaleMax - scaleMin) * NoireEffects.Wave(scaleWave, time * scaleSpeed));

            if (squashAmount != 0f)
            {
                var squash = squashAmount * MathF.Sin(Tau * squashSpeed * time);
                scale *= new Vector2(1f + squash, 1f - squash);
            }

            if (flipSpeed != 0f)
            {
                var turn = MathF.Cos(Tau * flipSpeed * time);
                scale *= flipVertical ? new Vector2(1f, turn) : new Vector2(turn, 1f);
            }

            if (skewAmount != 0f)
                skew += skewAmount * MathF.Sin(Tau * skewSpeed * time);

            offset += new Vector2(swayPixels * MathF.Sin(Tau * swaySpeed * time), -floatPixels * MathF.Sin(Tau * floatSpeed * time));

            if (bounceHeight != 0f)
                offset.Y -= bounceHeight * MathF.Abs(MathF.Sin(MathF.PI * bounceSpeed * time));

            if (orbitRadius != 0f)
                offset += orbitRadius * new Vector2(MathF.Cos(Tau * orbitSpeed * time), MathF.Sin(Tau * orbitSpeed * time));

            if (shakeIntensity != 0f && NoireEffects.InBurst(time, shakeEvery, shakeLength))
            {
                var step = (int)MathF.Floor(time * shakeFrequency);
                offset += new Vector2(NoireEffects.Hash(step, 17) - 0.5f, NoireEffects.Hash(step, 29) - 0.5f) * (2f * shakeIntensity);
            }
        }

        var matrix = Matrix3x2.CreateScale(scale) * Shear(skew) * Matrix3x2.CreateRotation(degrees * (MathF.PI / 180f));
        return new Pose(matrix, offset);
    }

    // The movement of one character around its own centre, and its shift.
    private Matrix3x2 PieceMatrix(int piece, int pieces, float time, bool moving, float lineHeight, out Vector2 shift)
    {
        shift = Vector2.Zero;
        var degrees = 0f;
        var scale = 1f;

        if (moving)
        {
            if (glyphWaveHeight != 0f)
            {
                var phase = (glyphWaveSpeed * time) - (piece * glyphWaveSpacing);
                shift.Y -= glyphWaveHeight * (glyphWaveShape == EffectWave.Sine ? MathF.Sin(Tau * phase) : NoireEffects.Wave(glyphWaveShape, phase));
            }

            if (wobbleDegrees != 0f)
                degrees += wobbleDegrees * MathF.Sin(Tau * ((wobbleSpeed * time) - (piece * wobbleSpacing)));

            if (jitterIntensity != 0f && NoireEffects.InBurst(time, jitterEvery, jitterLength))
            {
                var step = (int)MathF.Floor(time * jitterFrequency);
                shift += new Vector2(NoireEffects.Hash((piece * 7919) + step, 3) - 0.5f, NoireEffects.Hash((piece * 7919) + step, 5) - 0.5f) * (2f * jitterIntensity);
            }

            if (glitchIntensity != 0f && NoireEffects.InBurst(time, glitchEvery, glitchLength))
            {
                var step = (int)MathF.Floor(time * glitchRate);

                if (NoireEffects.Hash(piece, (step * 31) + 7) < glitchChance)
                {
                    shift += new Vector2(
                        (NoireEffects.Hash(piece, (step * 31) + 11) - 0.5f) * 2f * glitchIntensity,
                        (NoireEffects.Hash(piece, (step * 31) + 13) - 0.5f) * 0.5f * glitchIntensity);
                }
            }
        }

        if (hasReveal && moving)
        {
            var total = (pieces * revealDelay) + revealLength + revealHold;
            var local = trigger == EffectTrigger.Always ? NoireEffects.Frac(time / total) * total : time;
            var progress = Math.Clamp((local - (piece * revealDelay)) / revealLength, 0f, 1f);

            scale *= EaseOutBack(progress);
            shift.Y -= (1f - progress) * lineHeight * 0.35f;
        }

        return Matrix3x2.CreateScale(scale) * Matrix3x2.CreateRotation(degrees * (MathF.PI / 180f));
    }

    // A value from -1 to 1 that is 0 at rest, for movements that go both ways.
    private static float Signed(EffectWave wave, float cycles)
        => wave == EffectWave.Sine ? MathF.Sin(Tau * cycles) : (2f * NoireEffects.Wave(wave, cycles + 0.25f)) - 1f;

    // Leans the top to the right by 'amount' per pixel of height, screen y growing downwards.
    private static Matrix3x2 Shear(float amount) => new(1f, 0f, -amount, 1f, 0f, 0f);

    private static float EaseOutBack(float t)
    {
        const float Overshoot = 1.70158f;
        var u = t - 1f;
        return 1f + ((Overshoot + 1f) * u * u * u) + (Overshoot * u * u);
    }

    #endregion
}
