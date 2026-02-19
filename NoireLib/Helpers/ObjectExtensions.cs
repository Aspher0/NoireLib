using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NoireLib.Helpers.ObjectExtensions;

/// <summary>
/// Extension methods for objects.
/// </summary>
public static class ObjectExtensions
{
    /// <summary>
    /// Checks if the value is the default for its type.
    /// </summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    /// <param name="value">Value to check.</param>
    /// <returns>True if <paramref name="value"/> is null or equals default(T); otherwise false.</returns>
    public static bool IsDefault<T>(this T value)
    {
        if (value is null) return true;
        return EqualityComparer<T>.Default.Equals(value, default);
    }

    /// <summary>
    /// Checks if the value is not null.
    /// </summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    /// <param name="value">Value to check.</param>
    /// <returns>True if <paramref name="value"/> is not null; otherwise false.</returns>
    public static bool IsNotNull<T>(this T? value) => value is not null;

    /// <summary>
    /// Checks if the value is null.
    /// </summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    /// <param name="value">Value to check.</param>
    /// <returns>True if <paramref name="value"/> is null; otherwise false.</returns>
    public static bool IsNull<T>(this T? value) => value is null;

    /// <summary>
    /// Returns the value if not null, otherwise returns the default value.
    /// </summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    /// <param name="value">Value to check.</param>
    /// <param name="defaultValue">Default value to return if value is null.</param>
    /// <returns>The value if not null; otherwise the default value.</returns>
    public static T OrDefault<T>(this T? value, T defaultValue) => value ?? defaultValue;

    /// <summary>
    /// Executes an action on the value if it is not null.
    /// </summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    /// <param name="value">Value to check.</param>
    /// <param name="action">Action to execute if value is not null.</param>
    /// <returns>The original value for chaining.</returns>
    public static T? IfNotNull<T>(this T? value, Action<T> action)
    {
        if (value is not null)
            action(value);
        return value;
    }

    /// <summary>
    /// Transforms the value if it is not null using the provided function.
    /// </summary>
    /// <typeparam name="TSource">Type of the source value.</typeparam>
    /// <typeparam name="TResult">Type of the result.</typeparam>
    /// <param name="value">Value to transform.</param>
    /// <param name="selector">Function to transform the value.</param>
    /// <returns>The transformed value if not null; otherwise default.</returns>
    public static TResult? Map<TSource, TResult>(this TSource? value, Func<TSource, TResult> selector)
    {
        return value is not null ? selector(value) : default;
    }

    /// <summary>
    /// Checks if the object is of the specified type.
    /// </summary>
    /// <typeparam name="T">Type to check against.</typeparam>
    /// <param name="value">Value to check.</param>
    /// <returns>True if the value is of type T; otherwise false.</returns>
    public static bool Is<T>(this object? value) => value is T;

    /// <summary>
    /// Tries to cast the object to the specified type.
    /// </summary>
    /// <typeparam name="T">Type to cast to.</typeparam>
    /// <param name="value">Value to cast.</param>
    /// <returns>The casted value if successful; otherwise default.</returns>
    public static T? As<T>(this object? value) where T : class => value as T;

    /// <summary>
    /// Applies a fluent action to the object and returns the object.
    /// </summary>
    /// <typeparam name="T">Type of the object.</typeparam>
    /// <param name="value">Object to apply action to.</param>
    /// <param name="action">Action to apply.</param>
    /// <returns>The same object for chaining.</returns>
    public static T Apply<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }

    /// <summary>
    /// Creates a shallow clone of the object using JSON serialization.
    /// </summary>
    /// <typeparam name="T">Type of the object.</typeparam>
    /// <param name="value">Object to clone.</param>
    /// <returns>A cloned copy of the object.</returns>
    public static T? Clone<T>(this T value) where T : class
    {
        if (value is null) return null;
        var json = JsonConvert.SerializeObject(value);
        return JsonConvert.DeserializeObject<T>(json);
    }

    /// <summary>
    /// Throws an ArgumentNullException if the value is null.
    /// </summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    /// <param name="value">Value to check.</param>
    /// <param name="paramName">Name of the parameter for the exception.</param>
    /// <returns>The value if not null.</returns>
    /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
    public static T ThrowIfNull<T>(this T? value, string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        return value;
    }

    /// <summary>
    /// Checks if the value is in the provided collection.
    /// </summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    /// <param name="value">Value to check.</param>
    /// <param name="items">Collection to check against.</param>
    /// <returns>True if the value is in the collection; otherwise false.</returns>
    public static bool In<T>(this T value, params T[] items) => items.Contains(value);

    /// <summary>
    /// Checks if the value is in the provided collection.
    /// </summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    /// <param name="value">Value to check.</param>
    /// <param name="items">Collection to check against.</param>
    /// <returns>True if the value is in the collection; otherwise false.</returns>
    public static bool In<T>(this T value, IEnumerable<T> items) => items.Contains(value);

    /// <summary>
    /// Checks if the value is not in the provided collection.
    /// </summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    /// <param name="value">Value to check.</param>
    /// <param name="items">Collection to check against.</param>
    /// <returns>True if the value is not in the collection; otherwise false.</returns>
    public static bool NotIn<T>(this T value, params T[] items) => !items.Contains(value);

    /// <summary>
    /// Checks if the value is not in the provided collection.
    /// </summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    /// <param name="value">Value to check.</param>
    /// <param name="items">Collection to check against.</param>
    /// <returns>True if the value is not in the collection; otherwise false.</returns>
    public static bool NotIn<T>(this T value, IEnumerable<T> items) => !items.Contains(value);

    /// <summary>
    /// Converts an integer to a TimeSpan representing milliseconds.
    /// </summary>
    /// <param name="milliseconds">Number of milliseconds.</param>
    /// <returns>A TimeSpan representing the specified milliseconds.</returns>
    public static TimeSpan Milliseconds(this int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    /// <summary>
    /// Converts a double to a TimeSpan representing milliseconds.
    /// </summary>
    /// <param name="milliseconds">Number of milliseconds.</param>
    /// <returns>A TimeSpan representing the specified milliseconds.</returns>
    public static TimeSpan Milliseconds(this double milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    /// <summary>
    /// Converts an integer to a TimeSpan representing seconds.
    /// </summary>
    /// <param name="seconds">Number of seconds.</param>
    /// <returns>A TimeSpan representing the specified seconds.</returns>
    public static TimeSpan Seconds(this int seconds) => TimeSpan.FromSeconds(seconds);

    /// <summary>
    /// Converts a double to a TimeSpan representing seconds.
    /// </summary>
    /// <param name="seconds">Number of seconds.</param>
    /// <returns>A TimeSpan representing the specified seconds.</returns>
    public static TimeSpan Seconds(this double seconds) => TimeSpan.FromSeconds(seconds);

    /// <summary>
    /// Converts an integer to a TimeSpan representing minutes.
    /// </summary>
    /// <param name="minutes">Number of minutes.</param>
    /// <returns>A TimeSpan representing the specified minutes.</returns>
    public static TimeSpan Minutes(this int minutes) => TimeSpan.FromMinutes(minutes);

    /// <summary>
    /// Converts a double to a TimeSpan representing minutes.
    /// </summary>
    /// <param name="minutes">Number of minutes.</param>
    /// <returns>A TimeSpan representing the specified minutes.</returns>
    public static TimeSpan Minutes(this double minutes) => TimeSpan.FromMinutes(minutes);

    /// <summary>
    /// Converts an integer to a TimeSpan representing hours.
    /// </summary>
    /// <param name="hours">Number of hours.</param>
    /// <returns>A TimeSpan representing the specified hours.</returns>
    public static TimeSpan Hours(this int hours) => TimeSpan.FromHours(hours);

    /// <summary>
    /// Converts a double to a TimeSpan representing hours.
    /// </summary>
    /// <param name="hours">Number of hours.</param>
    /// <returns>A TimeSpan representing the specified hours.</returns>
    public static TimeSpan Hours(this double hours) => TimeSpan.FromHours(hours);

    /// <summary>
    /// Converts an integer to a TimeSpan representing days.
    /// </summary>
    /// <param name="days">Number of days.</param>
    /// <returns>A TimeSpan representing the specified days.</returns>
    public static TimeSpan Days(this int days) => TimeSpan.FromDays(days);

    /// <summary>
    /// Converts a double to a TimeSpan representing days.
    /// </summary>
    /// <param name="days">Number of days.</param>
    /// <returns>A TimeSpan representing the specified days.</returns>
    public static TimeSpan Days(this double days) => TimeSpan.FromDays(days);

    /// <summary>
    /// Copies all public and non-public instance properties and fields from the source object to the target object.
    /// </summary>
    /// <typeparam name="T">The type of the objects.</typeparam>
    /// <param name="source">The source object.</param>
    /// <param name="target">The target object.</param>
    public static void CopyMembersTo<T>(this T source, T target)
    {
        if (source == null || target == null)
            return;

        var type = typeof(T);

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (prop.CanRead && prop.CanWrite)
            {
                var value = prop.GetValue(source);
                prop.SetValue(target, value);
            }
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var value = field.GetValue(source);
            field.SetValue(target, value);
        }
    }
}
