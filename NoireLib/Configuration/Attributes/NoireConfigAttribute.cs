using System;

namespace NoireLib.Configuration;

/// <summary>
/// Marks a configuration class for source generation. The generator emits a static accessor class
/// whose members forward to the singleton instance resolved through <see cref="NoireConfigManager.GetConfig{T}"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NoireConfigAttribute : Attribute
{
    /// <summary>
    /// Gets the name of the generated static accessor class, or null to append "Static" to the class name.
    /// </summary>
    public string? StaticClassName { get; }

    /// <summary>
    /// The name of a generated static class holding one <see cref="NoireSetting{TValue}"/> per simple property, plus
    /// <c>All</c>; <see langword="null"/> generates none.
    /// </summary>
    public string? SettingsClassName { get; set; }

    /// <summary>
    /// Marks the class for source generation with the default accessor name
    /// (your configuration class name with the "Static" suffix, example: Configuration -> ConfigurationStatic).
    /// </summary>
    public NoireConfigAttribute()
    {
        StaticClassName = null;
    }

    /// <summary>Marks the class for source generation with a custom static accessor name.</summary>
    /// <param name="staticClassName">The name for the generated static accessor class.</param>
    public NoireConfigAttribute(string staticClassName)
    {
        StaticClassName = staticClassName;
    }
}
