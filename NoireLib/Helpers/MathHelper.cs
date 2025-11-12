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
    /// Clamps a value between a minimum and maximum value.
    /// </summary>
    /// <param name="value">The value to clamp.</param>
    /// <param name="min">The minimum value.</param>
    /// <param name="max">The maximum value.</param>
    /// <returns>The clamped value.</returns>
    public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);

    /// <summary>
    /// Clamps a value between a minimum and maximum value.
    /// </summary>
    /// <param name="value">The value to clamp.</param>
    /// <param name="min">The minimum value.</param>
    /// <param name="max">The maximum value.</param>
    /// <returns>The clamped value.</returns>
    public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);

    /// <summary>
    /// Clamps a value between a minimum and maximum value.
    /// </summary>
    /// <param name="value">The value to clamp.</param>
    /// <param name="min">The minimum value.</param>
    /// <param name="max">The maximum value.</param>
    /// <returns>The clamped value.</returns>
    public static double Clamp(double value, double min, double max) => Math.Clamp(value, min, max);

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

    #region Rounding

    /// <summary>
    /// Rounds a value to the nearest integer.
    /// </summary>
    /// <param name="value">The value to round.</param>
    /// <returns>The rounded value.</returns>
    public static float Round(float value) => MathF.Round(value);

    /// <summary>
    /// Rounds a value to the nearest multiple of a given number.
    /// </summary>
    /// <param name="value">The value to round.</param>
    /// <param name="multiple">The multiple to round to.</param>
    /// <returns>The rounded value.</returns>
    public static float RoundToNearest(float value, float multiple)
    {
        if (Math.Abs(multiple) < Epsilon) return value;
        return MathF.Round(value / multiple) * multiple;
    }

    /// <summary>
    /// Rounds a value down to the nearest integer.
    /// </summary>
    /// <param name="value">The value to floor.</param>
    /// <returns>The floored value.</returns>
    public static float Floor(float value) => MathF.Floor(value);

    /// <summary>
    /// Rounds a value up to the nearest integer.
    /// </summary>
    /// <param name="value">The value to ceil.</param>
    /// <returns>The ceiled value.</returns>
    public static float Ceil(float value) => MathF.Ceiling(value);

    /// <summary>
    /// Converts a float to an integer by rounding.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The integer value.</returns>
    public static int RoundToInt(float value) => (int)MathF.Round(value);

    /// <summary>
    /// Converts a float to an integer by flooring.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The integer value.</returns>
    public static int FloorToInt(float value) => (int)MathF.Floor(value);

    /// <summary>
    /// Converts a float to an integer by ceiling.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The integer value.</returns>
    public static int CeilToInt(float value) => (int)MathF.Ceiling(value);

    #endregion

    #region Min/Max

    /// <summary>
    /// Returns the minimum of two values.
    /// </summary>
    public static int Min(int a, int b) => Math.Min(a, b);

    /// <summary>
    /// Returns the minimum of two values.
    /// </summary>
    public static float Min(float a, float b) => Math.Min(a, b);

    /// <summary>
    /// Returns the minimum of three values.
    /// </summary>
    public static float Min(float a, float b, float c) => Math.Min(Math.Min(a, b), c);

    /// <summary>
    /// Returns the maximum of two values.
    /// </summary>
    public static int Max(int a, int b) => Math.Max(a, b);

    /// <summary>
    /// Returns the maximum of two values.
    /// </summary>
    public static float Max(float a, float b) => Math.Max(a, b);

    /// <summary>
    /// Returns the maximum of three values.
    /// </summary>
    public static float Max(float a, float b, float c) => Math.Max(Math.Max(a, b), c);

    #endregion

    #region Sign and Absolute

    /// <summary>
    /// Returns the sign of a number (-1, 0, or 1).
    /// </summary>
    public static int Sign(float value) => Math.Sign(value);

    /// <summary>
    /// Returns the absolute value of a number.
    /// </summary>
    public static int Abs(int value) => Math.Abs(value);

    /// <summary>
    /// Returns the absolute value of a number.
    /// </summary>
    public static float Abs(float value) => Math.Abs(value);

    /// <summary>
    /// Returns the absolute value of a number.
    /// </summary>
    public static double Abs(double value) => Math.Abs(value);

    #endregion

    #region Power and Root

    /// <summary>
    /// Returns the value raised to the specified power.
    /// </summary>
    public static float Pow(float value, float power) => MathF.Pow(value, power);

    /// <summary>
    /// Returns the square root of a value.
    /// </summary>
    public static float Sqrt(float value) => MathF.Sqrt(value);

    /// <summary>
    /// Returns the square of a value.
    /// </summary>
    public static float Square(float value) => value * value;

    /// <summary>
    /// Returns the cube of a value.
    /// </summary>
    public static float Cube(float value) => value * value * value;

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

    /// <summary>
    /// Returns the sine of the specified angle in radians.
    /// </summary>
    public static float Sin(float radians) => MathF.Sin(radians);

    /// <summary>
    /// Returns the cosine of the specified angle in radians.
    /// </summary>
    public static float Cos(float radians) => MathF.Cos(radians);

    /// <summary>
    /// Returns the tangent of the specified angle in radians.
    /// </summary>
    public static float Tan(float radians) => MathF.Tan(radians);

    /// <summary>
    /// Returns the arc sine of the specified value.
    /// </summary>
    public static float Asin(float value) => MathF.Asin(value);

    /// <summary>
    /// Returns the arc cosine of the specified value.
    /// </summary>
    public static float Acos(float value) => MathF.Acos(value);

    /// <summary>
    /// Returns the arc tangent of the specified value.
    /// </summary>
    public static float Atan(float value) => MathF.Atan(value);

    /// <summary>
    /// Returns the angle whose tangent is the quotient of two specified numbers.
    /// </summary>
    public static float Atan2(float y, float x) => MathF.Atan2(y, x);

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
    /// Returns the distance between two points in 2D space.
    /// </summary>
    public static float Distance(Vector2 a, Vector2 b)
    {
        return Vector2.Distance(a, b);
    }

    /// <summary>
    /// Returns the squared distance between two points in 2D space (faster than Distance).
    /// </summary>
    public static float DistanceSquared(float x1, float y1, float x2, float y2)
    {
        return Vector2.DistanceSquared(new Vector2(x1, y1), new Vector2(x2, y2));
    }

    /// <summary>
    /// Returns the squared distance between two points in 2D space (faster than Distance).
    /// </summary>
    public static float DistanceSquared(Vector2 a, Vector2 b)
    {
        return Vector2.DistanceSquared(a, b);
    }

    /// <summary>
    /// Returns the distance between two points in 3D space.
    /// </summary>
    public static float Distance(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        return Vector3.Distance(new Vector3(x1, y1, z1), new Vector3(x2, y2, z2));
    }

    /// <summary>
    /// Returns the distance between two points in 3D space.
    /// </summary>
    public static float Distance(Vector3 a, Vector3 b)
    {
        return Vector3.Distance(a, b);
    }

    /// <summary>
    /// Returns the squared distance between two points in 3D space (faster than Distance).
    /// </summary>
    public static float DistanceSquared(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        return Vector3.DistanceSquared(new Vector3(x1, y1, z1), new Vector3(x2, y2, z2));
    }

    /// <summary>
    /// Returns the squared distance between two points in 3D space (faster than Distance).
    /// </summary>
    public static float DistanceSquared(Vector3 a, Vector3 b)
    {
        return Vector3.DistanceSquared(a, b);
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
