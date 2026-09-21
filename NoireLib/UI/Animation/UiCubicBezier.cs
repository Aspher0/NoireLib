using System;

namespace NoireLib.UI;

/// <summary>
/// A CSS <c>cubic-bezier(x1, y1, x2, y2)</c> timing curve, usable wherever <see cref="NoireAnim"/> takes a curve.
/// </summary>
public sealed class UiCubicBezier
{
    private const int SampleCount = 11;
    private const float SampleStep = 1f / (SampleCount - 1);

    private readonly float cx;
    private readonly float bx;
    private readonly float ax;
    private readonly float cy;
    private readonly float by;
    private readonly float ay;
    private readonly float[] samples = new float[SampleCount];

    /// <summary>Creates the curve from its two control points, as CSS writes them.</summary>
    /// <param name="x1">The first control point's x, from 0 to 1.</param>
    /// <param name="y1">The first control point's y.</param>
    /// <param name="x2">The second control point's x, from 0 to 1.</param>
    /// <param name="y2">The second control point's y.</param>
    public UiCubicBezier(float x1, float y1, float x2, float y2)
    {
        X1 = Math.Clamp(x1, 0f, 1f);
        Y1 = y1;
        X2 = Math.Clamp(x2, 0f, 1f);
        Y2 = y2;

        cx = 3f * X1;
        bx = (3f * (X2 - X1)) - cx;
        ax = 1f - cx - bx;
        cy = 3f * Y1;
        by = (3f * (Y2 - Y1)) - cy;
        ay = 1f - cy - by;

        for (var i = 0; i < SampleCount; i++)
            samples[i] = SampleX(i * SampleStep);

        Curve = Evaluate;
    }

    /// <summary>The first control point's x.</summary>
    public float X1 { get; }

    /// <summary>The first control point's y.</summary>
    public float Y1 { get; }

    /// <summary>The second control point's x.</summary>
    public float X2 { get; }

    /// <summary>The second control point's y.</summary>
    public float Y2 { get; }

    /// <summary>The curve as a delegate, built once, for <see cref="NoireAnim.Ease(string, string, float, float, Func{float, float})"/>.</summary>
    public Func<float, float> Curve { get; }

    /// <summary>CSS <c>ease</c>.</summary>
    public static UiCubicBezier Ease { get; } = new(0.25f, 0.1f, 0.25f, 1f);

    /// <summary>CSS <c>ease-in</c>.</summary>
    public static UiCubicBezier EaseIn { get; } = new(0.42f, 0f, 1f, 1f);

    /// <summary>CSS <c>ease-out</c>.</summary>
    public static UiCubicBezier EaseOut { get; } = new(0f, 0f, 0.58f, 1f);

    /// <summary>CSS <c>ease-in-out</c>.</summary>
    public static UiCubicBezier EaseInOut { get; } = new(0.42f, 0f, 0.58f, 1f);

    /// <summary>
    /// The eased progress at <paramref name="t"/>.
    /// </summary>
    /// <param name="t">The progress from 0 to 1, clamped.</param>
    /// <returns>The eased progress. It may leave 0 to 1 when a control point does.</returns>
    public float Evaluate(float t)
    {
        if (t <= 0f)
            return 0f;

        if (t >= 1f)
            return 1f;

        return SampleY(SolveX(t));
    }

    private float SampleX(float u) => ((((ax * u) + bx) * u) + cx) * u;

    private float SampleY(float u) => ((((ay * u) + by) * u) + cy) * u;

    private float SlopeX(float u) => (((3f * ax * u) + (2f * bx)) * u) + cx;

    private float SolveX(float x)
    {
        var interval = 0f;
        var index = 1;

        while (index < SampleCount - 1 && samples[index] <= x)
        {
            interval += SampleStep;
            index++;
        }

        index--;

        var span = samples[index + 1] - samples[index];
        var guess = interval + (span > 0f ? (x - samples[index]) / span * SampleStep : 0f);
        var slope = SlopeX(guess);

        if (slope >= 0.001f)
        {
            for (var i = 0; i < 6; i++)
            {
                slope = SlopeX(guess);

                if (slope == 0f)
                    break;

                guess -= (SampleX(guess) - x) / slope;
            }

            return Math.Clamp(guess, 0f, 1f);
        }

        if (slope == 0f)
            return guess;

        var low = interval;
        var high = interval + SampleStep;

        for (var i = 0; i < 20; i++)
        {
            guess = (low + high) * 0.5f;
            var delta = SampleX(guess) - x;

            if (MathF.Abs(delta) < 1e-6f)
                break;

            if (delta > 0f)
                high = guess;
            else
                low = guess;
        }

        return guess;
    }
}
