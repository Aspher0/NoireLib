using System.Collections.Generic;

namespace NoireLib.Internal;

/// <summary>
/// Internal extension methods for objects.
/// </summary>
public static class ObjectExtensions
{
    /// <summary>
    /// Checks if the value is the default for its type.
    /// </summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    /// <param name="value">Value to check.</param>
    /// <returns>True if <paramref name="value"/> is null or equals default(T); otherwise false.</returns>
    internal static bool IsDefault<T>(this T value)
    {
        if (value is null) return true;
        return EqualityComparer<T>.Default.Equals(value, default);
    }
}
