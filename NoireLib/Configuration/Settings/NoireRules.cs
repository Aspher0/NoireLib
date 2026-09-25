using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace NoireLib.Configuration;

/// <summary>
/// The limits a configuration property enforces, read from its <c>[Range]</c>, <c>[StringLength]</c> or
/// <c>[MaxLength]</c> attribute.
/// </summary>
/// <typeparam name="TValue">The property type.</typeparam>
public sealed class NoireRules<TValue>
{
    /// <summary>No limits.</summary>
    public static readonly NoireRules<TValue> None = new(default, false, default, false, null);

    private NoireRules(TValue? min, bool hasMin, TValue? max, bool hasMax, int? maxLength)
    {
        Min = min;
        HasMin = hasMin;
        Max = max;
        HasMax = hasMax;
        MaxLength = maxLength;
    }

    /// <summary>The smallest value allowed, meaningful when <see cref="HasMin"/> is set.</summary>
    public TValue? Min { get; }

    /// <summary>Whether a minimum is set.</summary>
    public bool HasMin { get; }

    /// <summary>The largest value allowed, meaningful when <see cref="HasMax"/> is set.</summary>
    public TValue? Max { get; }

    /// <summary>Whether a maximum is set.</summary>
    public bool HasMax { get; }

    /// <summary>The longest text allowed, or <see langword="null"/>.</summary>
    public int? MaxLength { get; }

    /// <summary>Brings a value inside the limits; <see langword="null"/> is left as it is.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value, clamped or truncated.</returns>
    public TValue Apply(TValue value)
    {
        if (value is null)
            return value;

        var comparer = Comparer<TValue>.Default;

        if (HasMin && comparer.Compare(value, Min!) < 0)
            value = Min!;

        if (HasMax && comparer.Compare(value, Max!) > 0)
            value = Max!;

        if (MaxLength is { } length && value is string text && text.Length > length)
            value = (TValue)(object)text[..length];

        return value;
    }

    /// <summary>
    /// Reads the limits of a configuration property. Called by generated code.
    /// </summary>
    /// <param name="configType">The configuration type.</param>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The limits, or <see cref="None"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static NoireRules<TValue> Of(Type configType, string propertyName)
    {
        var property = configType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);

        if (property == null)
            return None;

        var limits = ConfigRules.Read(property);

        if (limits.IsEmpty)
            return None;

        return new NoireRules<TValue>(
            limits.Min is { } min ? (TValue)min : default,
            limits.Min != null,
            limits.Max is { } max ? (TValue)max : default,
            limits.Max != null,
            limits.MaxLength);
    }
}
