using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

public sealed partial class NoireGradient
{
    #region Scopes

    /// <summary>Opens a scope: everything drawn until it closes takes this gradient.</summary>
    /// <param name="drawList">The draw list to record. The default is the one <see cref="NoireShapes"/> paints into.</param>
    /// <param name="startedAt">The moment a gradient with <see cref="ForSeconds"/> starts animating, in <see cref="NoireUI.Time"/> seconds.</param>
    /// <param name="onceKey">The key a gradient with <see cref="Once"/> animates once for.</param>
    /// <returns>The open scope. Close it with <see langword="using"/> or <see cref="EffectScope.End"/>.</returns>
    public EffectScope Begin(ImDrawListPtr drawList = default, float startedAt = float.NaN, string? onceKey = null)
        => NoireEffects.Begin(this, null, drawList, startedAt, onceKey);

    /// <summary>Runs a block of drawing with this gradient.</summary>
    /// <param name="body">The drawing.</param>
    /// <returns>What was drawn.</returns>
    public EffectResult With(Action body) => NoireEffects.With(this, null, body);

    /// <summary>Runs a block of drawing with this gradient, passing a value through.</summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing.</param>
    /// <returns>What was drawn.</returns>
    public EffectResult With<TState>(TState state, Action<TState> body) => NoireEffects.With(this, null, state, body);

    /// <summary>Draws a line of text with this gradient.</summary>
    /// <param name="text">The text.</param>
    /// <returns>What was drawn.</returns>
    public EffectResult Text(string text) => NoireEffects.Text(text, this);

    /// <summary>Draws text wrapped at the edge of the window, with this gradient.</summary>
    /// <param name="text">The text.</param>
    /// <returns>What was drawn.</returns>
    public EffectResult TextWrapped(string text) => NoireEffects.TextWrapped(text, this);

    /// <summary>Fills a rectangle with this gradient, cut into cells fine enough to show every color.</summary>
    /// <param name="min">The top left corner, in screen space.</param>
    /// <param name="max">The bottom right corner, in screen space.</param>
    /// <param name="rounding">The corner radius, in pixels.</param>
    /// <param name="corners">Which corners are rounded.</param>
    /// <returns>What was drawn.</returns>
    public EffectResult FillRect(Vector2 min, Vector2 max, float rounding = 0f, RectCorners corners = RectCorners.All)
    {
        var scope = Begin();
        NoireShapes.Rect(min, max, Vector4.One, rounding > 0f ? CornerShape.Rounded : CornerShape.Square, rounding, corners);
        return scope.End();
    }

    /// <summary>Fills a circle with this gradient, cut into cells fine enough to show every color.</summary>
    /// <param name="center">The centre, in screen space.</param>
    /// <param name="circleRadius">The radius, in pixels.</param>
    /// <returns>What was drawn.</returns>
    public EffectResult FillCircle(Vector2 center, float circleRadius)
    {
        Span<Vector2> points = stackalloc Vector2[256];
        var count = NoireShapes.ArcPath(points, center, circleRadius, 0f, 1f, out _);

        var scope = Begin();

        if (count >= 3)
            NoireShapes.Fill(points[..count], Vector4.One);

        return scope.End();
    }

    #endregion

    #region Reading the gradient

    /// <summary>
    /// The color this gradient shows at a point of an area now, animations included. For a gradient that follows the
    /// characters or the lines of a text, this is the color of the first one.
    /// </summary>
    /// <param name="point">The point, in screen space.</param>
    /// <param name="min">The top left corner of the area.</param>
    /// <param name="max">The bottom right corner of the area.</param>
    /// <returns>The color, RGBA from 0 to 1.</returns>
    public Vector4 ColorAt(Vector2 point, Vector2 min, Vector2 max)
    {
        var reduced = NoireUI.ReducedMotion && !ignoreReducedMotion;
        return ColorAt(point, min, max, reduced || trigger != EffectTrigger.Always ? 0f : NoireUI.Time);
    }

    // Reads nothing but this gradient: safe off the draw thread.
    internal Vector4 ColorAt(Vector2 point, Vector2 min, Vector2 max, float time)
    {
        var t = PositionAt(point, min, max, 0, 1, 1, 1f, rotationSpeed * time);

        if (waveAmplitude != 0f)
            t += waveAmplitude * MathF.Sin(2f * MathF.PI * ((waveFrequency * t) + (waveSpeed * time)));

        var color = Lookup(Resolve(t, scrollSpeed * time));

        return hueSpeed != 0f ? ShiftHue(color, hueSpeed * time) : color;
    }

    #endregion

    #region Applying

    internal void Apply(Span<ImDrawVert> vertices, in EffectContext context)
    {
        NoireEffects.ResolveArea(area, rectMin, rectMax, in context, out var areaMin, out var areaMax);

        var time = NoireEffects.AnimationTime(trigger, triggerDuration, ignoreReducedMotion, in context, areaMin, areaMax, out var moving);
        var scroll = scrollSpeed * time;
        var rotation = rotationSpeed * time;
        var hueShift = hueSpeed * time;
        var pulse = hasPulse && moving ? NoireEffects.Wave(pulseWave, time * pulseSpeed) : 0f;

        var power = strength;

        if (breatheSpeed > 0f && moving)
            power *= 1f - (breatheDepth * (1f - NoireEffects.Wave(EffectWave.Sine, time * breatheSpeed)));

        var blinkAlpha = blinkRate > 0f && moving && NoireEffects.Frac(time * blinkRate) >= 0.5f ? blinkMinAlpha : 1f;

        var shimmerCenter = float.NaN;

        if (hasShimmer && moving)
        {
            var cycle = 1f + shimmerPause;
            var phase = NoireEffects.Frac(time * shimmerSpeed / cycle) * cycle;

            if (phase <= 1f)
                shimmerCenter = -shimmerWidth + (phase * (1f + (2f * shimmerWidth)));
        }

        var lines = shape == GradientShape.PerLine ? Math.Max(1, (int)MathF.Round((areaMax.Y - areaMin.Y) / context.LineHeight)) : 1;
        var white = context.White;

        var piece = -1;
        var pieceEnd = 0;
        var pieceMin = areaMin;
        var pieceMax = areaMax;

        for (var i = 0; i < vertices.Length; i++)
        {
            if (i >= pieceEnd)
            {
                piece++;
                pieceEnd = NoireEffects.PieceEnd(vertices, i, white, out pieceMin, out pieceMax);
            }

            ref var vertex = ref vertices[i];
            var textured = vertex.Uv != white;

            if (!NoireEffects.Targets(target, textured))
                continue;

            var min = area == EffectArea.Glyph ? pieceMin : areaMin;
            var max = area == EffectArea.Glyph ? pieceMax : areaMax;

            var t = PositionAt(vertex.Pos, min, max, piece, context.PieceCount, lines, context.LineHeight, rotation);

            if (waveAmplitude != 0f)
                t += waveAmplitude * MathF.Sin(2f * MathF.PI * ((waveFrequency * t) + (waveSpeed * time)));

            var color = Lookup(Resolve(t, scroll));

            if (hueShift != 0f)
                color = ShiftHue(color, hueShift);

            if (pulse > 0f)
            {
                if (pulseColor is { } towards)
                    color = Vector4.Lerp(color, towards with { W = color.W }, pulse * towards.W);

                color.W *= 1f - ((1f - pulseAlpha) * pulse);

                if (pulseBrightness != 0f)
                {
                    var scale = 1f + (pulseBrightness * pulse);
                    color = new Vector4(Math.Clamp(color.X * scale, 0f, 1f), Math.Clamp(color.Y * scale, 0f, 1f), Math.Clamp(color.Z * scale, 0f, 1f), color.W);
                }
            }

            if (!float.IsNaN(shimmerCenter))
            {
                var u = LinearPosition(vertex.Pos, areaMin, areaMax, shimmerAngle);
                var distance = MathF.Abs(u - shimmerCenter) / shimmerWidth;

                if (distance < 1f)
                {
                    var k = 1f - distance;
                    k = k * k * (3f - (2f * k));
                    color = Vector4.Lerp(color, shimmerColor with { W = color.W }, k * shimmerColor.W);
                }
            }

            if (hasSparkle && moving && NoireEffects.Hash(piece, seed) < sparkleDensity)
            {
                var phase = NoireEffects.Frac((time * sparkleSpeed) + NoireEffects.Hash(piece, seed + 7919));

                if (phase < 0.15f)
                    color = Vector4.Lerp(color, sparkleColor with { W = color.W }, MathF.Sin(MathF.PI * phase / 0.15f) * sparkleColor.W);
            }

            color.W *= blinkAlpha;
            vertex.Col = Combine(vertex.Col, color, power);
        }
    }

    private float PositionAt(Vector2 point, Vector2 min, Vector2 max, int piece, int pieces, int lines, float lineHeight, float rotation)
    {
        switch (shape)
        {
            case GradientShape.Reflected:
                return MathF.Abs((2f * LinearPosition(point, min, max, angle + rotation)) - 1f);
            case GradientShape.Radial:
            {
                var q = Relative(point, min, max);
                return q.Length();
            }
            case GradientShape.Conic:
            {
                var size = Vector2.Max(max - min, new Vector2(0.0001f));
                var c = min + (center * size);
                var degrees = MathF.Atan2(point.Y - c.Y, point.X - c.X) * (180f / MathF.PI);
                return NoireEffects.Frac((degrees - angle - rotation) / 360f);
            }
            case GradientShape.Diamond:
            {
                var q = Relative(point, min, max);
                return MathF.Abs(q.X) + MathF.Abs(q.Y);
            }
            case GradientShape.Square:
            {
                var q = Relative(point, min, max);
                return MathF.Max(MathF.Abs(q.X), MathF.Abs(q.Y));
            }
            case GradientShape.Noise:
                return ValueNoise((point - min) / noiseScale, seed);
            case GradientShape.PerGlyph:
                return glyphStep > 0f ? piece * glyphStep : pieces <= 1 ? 0f : piece / (float)(pieces - 1);
            case GradientShape.PerLine:
            {
                var line = Math.Clamp((int)MathF.Floor((point.Y - min.Y) / lineHeight), 0, lines - 1);
                return glyphStep > 0f ? line * glyphStep : lines <= 1 ? 0f : line / (float)(lines - 1);
            }
            case GradientShape.Custom when custom != null:
            {
                var size = Vector2.Max(max - min, new Vector2(0.0001f));
                return custom((point - min) / size);
            }
            default:
                return LinearPosition(point, min, max, angle + rotation);
        }
    }

    // Where a point falls along a line through the area's centre at an angle, measured so the line exactly spans the
    // area: its two ends touch opposite corners or edges whatever the angle, the way CSS measures a linear gradient.
    private float LinearPosition(Vector2 point, Vector2 min, Vector2 max, float degrees)
    {
        var size = Vector2.Max(max - min, new Vector2(0.0001f));
        var c = min + (center * size);
        var radians = degrees * (MathF.PI / 180f);
        var direction = new Vector2(MathF.Cos(radians), MathF.Sin(radians));
        var half = (MathF.Abs(size.X * direction.X) + MathF.Abs(size.Y * direction.Y)) * 0.5f;

        return 0.5f + (Vector2.Dot(point - c, direction) / (2f * MathF.Max(half, 0.0001f)));
    }

    private Vector2 Relative(Vector2 point, Vector2 min, Vector2 max)
    {
        var size = Vector2.Max(max - min, new Vector2(0.0001f));
        var c = min + (center * size);
        var reach = size * 0.5f * radius;

        if (circular)
            reach = new Vector2(MathF.Min(reach.X, reach.Y));

        return (point - c) / Vector2.Max(reach, new Vector2(0.0001f));
    }

    private static Vector4 ShiftHue(Vector4 color, float turns)
    {
        var (hue, saturation, value) = ColorHelper.ToHsv(color);
        return ColorHelper.FromHsv(hue + turns, saturation, value, color.W);
    }

    // Two octaves of smooth value noise, from 0 to 1.
    private static float ValueNoise(Vector2 point, int noiseSeed)
    {
        var total = (0.65f * Octave(point, noiseSeed)) + (0.35f * Octave(point * 2.13f, noiseSeed + 101));
        return Math.Clamp(total, 0f, 1f);
    }

    private static float Octave(Vector2 point, int noiseSeed)
    {
        var x0 = (int)MathF.Floor(point.X);
        var y0 = (int)MathF.Floor(point.Y);
        var fx = point.X - x0;
        var fy = point.Y - y0;

        fx = fx * fx * (3f - (2f * fx));
        fy = fy * fy * (3f - (2f * fy));

        var a = NoireEffects.Hash((x0 * 73856093) ^ noiseSeed, y0);
        var b = NoireEffects.Hash(((x0 + 1) * 73856093) ^ noiseSeed, y0);
        var c = NoireEffects.Hash((x0 * 73856093) ^ noiseSeed, y0 + 1);
        var d = NoireEffects.Hash(((x0 + 1) * 73856093) ^ noiseSeed, y0 + 1);

        var top = a + ((b - a) * fx);
        var bottom = c + ((d - c) * fx);

        return top + ((bottom - top) * fy);
    }

    // Combines the gradient's color with a vertex's packed ImGui color (0xAABBGGRR), channel by channel.
    private uint Combine(uint packed, Vector4 color, float power)
    {
        var original = new Vector3(packed & 0xFFu, (packed >> 8) & 0xFFu, (packed >> 16) & 0xFFu) / 255f;
        var alpha = ((packed >> 24) & 0xFFu) / 255f;
        var tint = new Vector3(color.X, color.Y, color.Z);

        Vector3 mixed;
        var mixedAlpha = alpha;

        switch (blend)
        {
            case GradientBlend.Multiply:
                mixed = original * tint;
                mixedAlpha = alpha * color.W;
                break;
            case GradientBlend.Add:
                mixed = Vector3.Min(original + (tint * color.W), Vector3.One);
                break;
            case GradientBlend.Screen:
                mixed = Vector3.Lerp(original, Vector3.One - ((Vector3.One - original) * (Vector3.One - tint)), color.W);
                break;
            case GradientBlend.Overlay:
                mixed = Vector3.Lerp(original, new Vector3(Overlay(original.X, tint.X), Overlay(original.Y, tint.Y), Overlay(original.Z, tint.Z)), color.W);
                break;
            default:
                mixed = tint;
                mixedAlpha = alpha * color.W;
                break;
        }

        var result = Vector3.Lerp(original, mixed, power);
        var resultAlpha = alpha + ((mixedAlpha - alpha) * power);

        return Pack(result.X) | (Pack(result.Y) << 8) | (Pack(result.Z) << 16) | (Pack(resultAlpha) << 24);
    }

    private static float Overlay(float below, float above)
        => below < 0.5f ? 2f * below * above : 1f - (2f * (1f - below) * (1f - above));

    private static uint Pack(float channel) => (uint)((Math.Clamp(channel, 0f, 1f) * 255f) + 0.5f);

    #endregion
}
