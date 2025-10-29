using System;

namespace NoireLib.Configuration;

/// <summary>
/// Marks a configuration class for source generation.
/// Generates static property accessors for all public properties.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NoireConfigAttribute : Attribute
{
    /// <summary>
    /// Gets the name of the generated static accessor class.
    /// If null, the generator will use the instance class name.
    /// </summary>
    public string? StaticClassName { get; }

    /// <summary>
    /// Marks a configuration class for source generation.
    /// The generated static class will have the same name as the instance class.
    /// </summary>
    public NoireConfigAttribute()
    {
        StaticClassName = null;
    }

    /// <summary>
    /// Marks a configuration class for source generation with a custom static class name.
    /// </summary>
    /// <param name="staticClassName">The name for the generated static accessor class.</param>
    public NoireConfigAttribute(string staticClassName)
    {
        StaticClassName = staticClassName;
    }
}
