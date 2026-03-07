using System;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// A helper class containing various mathematical utility methods.
/// </summary>
public static class MathHelper
{
    /// <summary>
    /// The value of PI (π).
    /// </summary>
    public const float Pi = MathF.PI;

    /// <summary>
    /// The value of PI multiplied by 2.
    /// </summary>
    public const float TwoPi = MathF.PI * 2f;

    /// <summary>
    /// The value of PI divided by 2.
    /// </summary>
    public const float HalfPi = MathF.PI / 2f;

    /// <summary>
    /// Conversion factor from degrees to radians.
    /// </summary>
    public const float DegToRad = MathF.PI / 180f;

    /// <summary>
    /// Conversion factor from radians to degrees.
    /// </summary>
    public const float RadToDeg = 180f / MathF.PI;

    /// <summary>
    /// A small value used for floating-point comparisons.
    /// </summary>
    public const float Epsilon = 1e-6f;

    #region Clamping

    /// <summary>
    /// Clamps a value between 0 and 1.
    /// </summary>
    /// <param name="value">The value to clamp.</param>
    /// <returns>The clamped value.</returns>
    public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    /// <summary>
    /// Clamps a value between 0 and 1.
    /// </summary>
    /// <param name="value">The value to clamp.</param>
    /// <returns>The clamped value.</returns>
    public static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

    #endregion

    #region Interpolation

    /// <summary>
    /// Linearly interpolates between two values.
    /// </summary>
    /// <param name="a">The start value.</param>
    /// <param name="b">The end value.</param>
    /// <param name="t">The interpolation factor (0-1).</param>
    /// <returns>The interpolated value.</returns>
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    /// Linearly interpolates between two values with clamping.
    /// </summary>
    /// <param name="a">The start value.</param>
    /// <param name="b">The end value.</param>
    /// <param name="t">The interpolation factor (0-1).</param>
    /// <returns>The interpolated value.</returns>
    public static float LerpClamped(float a, float b, float t) => a + (b - a) * Clamp01(t);

    /// <summary>
    /// Inverse lerp - calculates the interpolation factor between two values.
    /// </summary>
    /// <param name="a">The start value.</param>
    /// <param name="b">The end value.</param>
    /// <param name="value">The value to get the factor for.</param>
    /// <returns>The interpolation factor.</returns>
    public static float InverseLerp(float a, float b, float value)
    {
        if (Math.Abs(a - b) < Epsilon) return 0f;
        return (value - a) / (b - a);
    }

    /// <summary>
    /// Remaps a value from one range to another.
    /// </summary>
    /// <param name="value">The value to remap.</param>
    /// <param name="fromMin">The minimum of the source range.</param>
    /// <param name="fromMax">The maximum of the source range.</param>
    /// <param name="toMin">The minimum of the target range.</param>
    /// <param name="toMax">The maximum of the target range.</param>
    /// <returns>The remapped value.</returns>
    public static float Remap(float value, float fromMin, float fromMax, float toMin, float toMax)
    {
        var t = InverseLerp(fromMin, fromMax, value);
        return Lerp(toMin, toMax, t);
    }

    /// <summary>
    /// Smoothly interpolates between two values using smoothstep.
    /// </summary>
    /// <param name="a">The start value.</param>
    /// <param name="b">The end value.</param>
    /// <param name="t">The interpolation factor (0-1).</param>
    /// <returns>The smoothly interpolated value.</returns>
    public static float SmoothStep(float a, float b, float t)
    {
        t = Clamp01(t);
        t = t * t * (3f - 2f * t);
        return Lerp(a, b, t);
    }

    #endregion

    #region Trigonometry

    /// <summary>
    /// Converts degrees to radians.
    /// </summary>
    public static float ToRadians(float degrees) => degrees * DegToRad;

    /// <summary>
    /// Converts radians to degrees.
    /// </summary>
    public static float ToDegrees(float radians) => radians * RadToDeg;

    #endregion

    #region Angle Operations

    /// <summary>
    /// Normalizes an angle to be within the range [0, 360).
    /// </summary>
    public static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle < 0f) angle += 360f;
        return angle;
    }

    /// <summary>
    /// Normalizes an angle to be within the range [-180, 180).
    /// </summary>
    public static float NormalizeAngleSigned(float angle)
    {
        angle = NormalizeAngle(angle);
        if (angle > 180f) angle -= 360f;
        return angle;
    }

    /// <summary>
    /// Returns the shortest angle difference between two angles.
    /// </summary>
    public static float DeltaAngle(float current, float target)
    {
        var delta = NormalizeAngleSigned(target - current);
        return delta;
    }

    /// <summary>
    /// Linearly interpolates between two angles, taking the shortest path.
    /// </summary>
    public static float LerpAngle(float a, float b, float t)
    {
        var delta = DeltaAngle(a, b);
        return a + delta * t;
    }

    #endregion

    #region Distance and Comparison

    /// <summary>
    /// Returns the distance between two points in 2D space.
    /// </summary>
    public static float Distance(float x1, float y1, float x2, float y2)
    {
        return Vector2.Distance(new Vector2(x1, y1), new Vector2(x2, y2));
    }

    /// <summary>
    /// Returns the squared distance between two points in 2D space (faster than Distance).
    /// </summary>
    public static float DistanceSquared(float x1, float y1, float x2, float y2)
    {
        return Vector2.DistanceSquared(new Vector2(x1, y1), new Vector2(x2, y2));
    }

    /// <summary>
    /// Returns the distance between two points in 3D space.
    /// </summary>
    public static float Distance(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        return Vector3.Distance(new Vector3(x1, y1, z1), new Vector3(x2, y2, z2));
    }

    /// <summary>
    /// Returns the squared distance between two points in 3D space (faster than Distance).
    /// </summary>
    public static float DistanceSquared(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        return Vector3.DistanceSquared(new Vector3(x1, y1, z1), new Vector3(x2, y2, z2));
    }

    /// <summary>
    /// Checks if two float values are approximately equal.
    /// </summary>
    public static bool Approximately(float a, float b, float epsilon = Epsilon)
    {
        return Math.Abs(a - b) < epsilon;
    }

    /// <summary>
    /// Checks if a value is approximately zero.
    /// </summary>
    public static bool IsZero(float value, float epsilon = Epsilon)
    {
        return Math.Abs(value) < epsilon;
    }

    #endregion

    #region Miscellaneous

    /// <summary>
    /// Returns the fractional part of a float value.
    /// </summary>
    public static float Frac(float value) => value - MathF.Floor(value);

    /// <summary>
    /// Wraps a value to be within the range [0, max).
    /// </summary>
    public static float Wrap(float value, float max)
    {
        value %= max;
        if (value < 0f) value += max;
        return value;
    }

    /// <summary>
    /// Wraps a value to be within the range [min, max).
    /// </summary>
    public static float Wrap(float value, float min, float max)
    {
        var range = max - min;
        return Wrap(value - min, range) + min;
    }

    /// <summary>
    /// Ping-pongs a value between 0 and length.
    /// </summary>
    public static float PingPong(float t, float length)
    {
        t = Wrap(t, length * 2f);
        return length - Math.Abs(t - length);
    }

    /// <summary>
    /// Returns 1 if the value is positive or zero, -1 if negative.
    /// </summary>
    public static float SignOrZero(float value)
    {
        return value >= 0f ? 1f : -1f;
    }

    /// <summary>
    /// Moves a value towards a target by a maximum delta.
    /// </summary>
    public static float MoveTowards(float current, float target, float maxDelta)
    {
        if (Math.Abs(target - current) <= maxDelta)
            return target;
        return current + Math.Sign(target - current) * maxDelta;
    }

    /// <summary>
    /// Calculates the percentage of a value within a range.
    /// </summary>
    public static float Percentage(float value, float min, float max)
    {
        if (Math.Abs(max - min) < Epsilon) return 0f;
        return (value - min) / (max - min) * 100f;
    }

    /// <summary>
    /// Checks if a value is within a range (inclusive).
    /// </summary>
    public static bool InRange(float value, float min, float max)
    {
        return value >= min && value <= max;
    }

    /// <summary>
    /// Checks if a value is within a range (exclusive).
    /// </summary>
    public static bool InRangeExclusive(float value, float min, float max)
    {
        return value > min && value < max;
    }

    #endregion
}
