using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;

namespace NoireLib.Configuration;

// Reads [Range], [StringLength] and [MaxLength] off configuration properties, converted to the property's type.
internal static class ConfigRules
{
    private static readonly ConcurrentDictionary<Type, PropertyRule[]> RulesByType = new();

    internal readonly record struct Limits(object? Min, object? Max, int? MaxLength)
    {
        internal bool IsEmpty => Min == null && Max == null && MaxLength == null;
    }

    internal readonly record struct PropertyRule(PropertyInfo Property, Limits Limits);

    internal static PropertyRule[] For(Type type) => RulesByType.GetOrAdd(type, static type =>
    {
        var rules = new List<PropertyRule>();

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0)
                continue;

            var limits = Read(property);

            if (!limits.IsEmpty)
                rules.Add(new PropertyRule(property, limits));
        }

        return [.. rules];
    });

    internal static Limits Read(PropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        object? min = null;
        object? max = null;
        int? maxLength = null;

        if (property.GetCustomAttribute<RangeAttribute>() is { } range && typeof(IComparable).IsAssignableFrom(type))
        {
            try
            {
                min = ConvertBound(range.Minimum, type, range);
                max = ConvertBound(range.Maximum, type, range);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or NotSupportedException or ArgumentException)
            {
                NoireLogger.LogWarning<NoireConfigBase>($"Ignored the [Range] of {property.DeclaringType?.Name}.{property.Name}: {ex.Message}");
                min = null;
                max = null;
            }
        }

        if (type == typeof(string))
        {
            var length = property.GetCustomAttribute<StringLengthAttribute>()?.MaximumLength ?? property.GetCustomAttribute<MaxLengthAttribute>()?.Length;

            if (length is >= 0)
                maxLength = length;
        }

        return new Limits(min, max, maxLength);
    }

    internal static object? Apply(Limits limits, object? value)
    {
        if (value == null)
            return null;

        if (limits.Min != null && Comparer.Default.Compare(value, limits.Min) < 0)
            value = limits.Min;

        if (limits.Max != null && Comparer.Default.Compare(value, limits.Max) > 0)
            value = limits.Max;

        if (limits.MaxLength is { } length && value is string text && text.Length > length)
            value = text[..length];

        return value;
    }

    private static object ConvertBound(object bound, Type type, RangeAttribute range)
    {
        if (bound is string text)
        {
            var culture = range.ParseLimitsInInvariantCulture ? CultureInfo.InvariantCulture : CultureInfo.CurrentCulture;
            return TypeDescriptor.GetConverter(type).ConvertFromString(null, culture, text)
                ?? throw new FormatException($"'{text}' is not a {type.Name}.");
        }

        if (type.IsEnum)
            return Enum.ToObject(type, bound);

        return Convert.ChangeType(bound, type, CultureInfo.InvariantCulture);
    }
}
