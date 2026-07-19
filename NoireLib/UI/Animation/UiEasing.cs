using System;

namespace NoireLib.UI;

/// <summary>
/// The shape an animation follows between its start and its end.<br/>
/// <c>In</c> curves start slow, <c>Out</c> curves end slow, <c>InOut</c> curves do both. <see cref="UiEasing.OutCubic"/>
/// is the usual choice for interface motion: it arrives quickly and settles softly.
/// </summary>
public enum UiEasing
{
    /// <summary>No easing; the value moves at a constant rate.</summary>
    Linear,

    /// <summary>Gentle acceleration.</summary>
    InSine,

    /// <summary>Gentle deceleration.</summary>
    OutSine,

    /// <summary>Gentle acceleration then deceleration.</summary>
    InOutSine,

    /// <summary>Quadratic acceleration.</summary>
    InQuad,

    /// <summary>Quadratic deceleration.</summary>
    OutQuad,

    /// <summary>Quadratic acceleration then deceleration.</summary>
    InOutQuad,

    /// <summary>Cubic acceleration.</summary>
    InCubic,

    /// <summary>Cubic deceleration. The default for interface motion.</summary>
    OutCubic,

    /// <summary>Cubic acceleration then deceleration.</summary>
    InOutCubic,

    /// <summary>Quartic acceleration.</summary>
    InQuart,

    /// <summary>Quartic deceleration.</summary>
    OutQuart,

    /// <summary>Quartic acceleration then deceleration.</summary>
    InOutQuart,

    /// <summary>Exponential acceleration, very slow to start.</summary>
    InExpo,

    /// <summary>Exponential deceleration, very abrupt to start.</summary>
    OutExpo,

    /// <summary>Exponential acceleration then deceleration.</summary>
    InOutExpo,

    /// <summary>Pulls back slightly before moving forward.</summary>
    InBack,

    /// <summary>Overshoots slightly then settles back.</summary>
    OutBack,

    /// <summary>Pulls back, then overshoots, then settles.</summary>
    InOutBack,

    /// <summary>Overshoots and oscillates to a stop.</summary>
    OutElastic,

    /// <summary>Falls and bounces to a stop.</summary>
    OutBounce,
}

/// <summary>
/// Evaluates a <see cref="UiEasing"/> curve.
/// </summary>
public static class UiEasingExtensions
{
    private const float BackOvershoot = 1.70158f;

    /// <summary>
    /// Evaluates the curve at <paramref name="t"/>.<br/>
    /// Pure and side-effect free, so it composes with anything: pass it a normalised progress and use the result to
    /// interpolate a colour, a size, or anything else.
    /// </summary>
    /// <param name="easing">The curve.</param>
    /// <param name="t">The progress from 0 to 1. Values outside that range are clamped.</param>
    /// <returns>The eased progress. Most curves return 0 at 0 and 1 at 1; the back and elastic curves overshoot in between.</returns>
    public static float Apply(this UiEasing easing, float t)
    {
        t = Math.Clamp(t, 0f, 1f);

        switch (easing)
        {
            case UiEasing.Linear:
                return t;

            case UiEasing.InSine:
                return 1f - MathF.Cos(t * MathF.PI / 2f);

            case UiEasing.OutSine:
                return MathF.Sin(t * MathF.PI / 2f);

            case UiEasing.InOutSine:
                return -(MathF.Cos(MathF.PI * t) - 1f) / 2f;

            case UiEasing.InQuad:
                return t * t;

            case UiEasing.OutQuad:
                return 1f - ((1f - t) * (1f - t));

            case UiEasing.InOutQuad:
                return t < 0.5f ? 2f * t * t : 1f - (MathF.Pow((-2f * t) + 2f, 2f) / 2f);

            case UiEasing.InCubic:
                return t * t * t;

            case UiEasing.OutCubic:
                return 1f - MathF.Pow(1f - t, 3f);

            case UiEasing.InOutCubic:
                return t < 0.5f ? 4f * t * t * t : 1f - (MathF.Pow((-2f * t) + 2f, 3f) / 2f);

            case UiEasing.InQuart:
                return t * t * t * t;

            case UiEasing.OutQuart:
                return 1f - MathF.Pow(1f - t, 4f);

            case UiEasing.InOutQuart:
                return t < 0.5f ? 8f * t * t * t * t : 1f - (MathF.Pow((-2f * t) + 2f, 4f) / 2f);

            case UiEasing.InExpo:
                return t <= 0f ? 0f : MathF.Pow(2f, (10f * t) - 10f);

            case UiEasing.OutExpo:
                return t >= 1f ? 1f : 1f - MathF.Pow(2f, -10f * t);

            case UiEasing.InOutExpo:
                if (t <= 0f)
                    return 0f;
                if (t >= 1f)
                    return 1f;
                return t < 0.5f
                    ? MathF.Pow(2f, (20f * t) - 10f) / 2f
                    : (2f - MathF.Pow(2f, (-20f * t) + 10f)) / 2f;

            case UiEasing.InBack:
                return ((BackOvershoot + 1f) * t * t * t) - (BackOvershoot * t * t);

            case UiEasing.OutBack:
                return 1f + ((BackOvershoot + 1f) * MathF.Pow(t - 1f, 3f)) + (BackOvershoot * MathF.Pow(t - 1f, 2f));

            case UiEasing.InOutBack:
                const float InOutOvershoot = BackOvershoot * 1.525f;
                return t < 0.5f
                    ? MathF.Pow(2f * t, 2f) * (((InOutOvershoot + 1f) * 2f * t) - InOutOvershoot) / 2f
                    : ((MathF.Pow((2f * t) - 2f, 2f) * (((InOutOvershoot + 1f) * ((t * 2f) - 2f)) + InOutOvershoot)) + 2f) / 2f;

            case UiEasing.OutElastic:
                if (t <= 0f)
                    return 0f;
                if (t >= 1f)
                    return 1f;
                return (MathF.Pow(2f, -10f * t) * MathF.Sin(((t * 10f) - 0.75f) * (2f * MathF.PI / 3f))) + 1f;

            case UiEasing.OutBounce:
                return OutBounce(t);

            default:
                return t;
        }
    }

    private static float OutBounce(float t)
    {
        const float N = 7.5625f;
        const float D = 2.75f;

        if (t < 1f / D)
            return N * t * t;

        if (t < 2f / D)
        {
            t -= 1.5f / D;
            return (N * t * t) + 0.75f;
        }

        if (t < 2.5f / D)
        {
            t -= 2.25f / D;
            return (N * t * t) + 0.9375f;
        }

        t -= 2.625f / D;
        return (N * t * t) + 0.984375f;
    }
}
