using System;

namespace NoireLib.Remote;

/// <summary>
/// Describes one parameter of a published member: what it means, what range it accepts and what control a console
/// draws for it.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class NoireRemoteParamAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteParamAttribute"/> class with no description.
    /// </summary>
    public NoireRemoteParamAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteParamAttribute"/> class with a description.
    /// </summary>
    /// <param name="description">What the parameter means, written as help under a form field.</param>
    public NoireRemoteParamAttribute(string? description)
    {
        Description = description;
    }

    /// <summary>
    /// Gets what the parameter means, written as help under a form field.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>Gets the smallest accepted value. NaN, the default, means no bound.</summary>
    public double Minimum { get; init; } = double.NaN;

    /// <summary>Gets the largest accepted value. NaN, the default, means no bound.</summary>
    public double Maximum { get; init; } = double.NaN;

    /// <summary>
    /// Gets the step a console's number field moves by. Not a number lets the console pick.
    /// </summary>
    public double Step { get; init; } = double.NaN;

    /// <summary>
    /// Gets a value a console offers as an example, written the way the wire carries it.
    /// </summary>
    public string? Example { get; init; }

    /// <summary>
    /// Gets the control a console draws. Auto picks from the schema.
    /// </summary>
    public NoireRemoteControl Control { get; init; } = NoireRemoteControl.Auto;
}

/// <summary>
/// Which control a console draws for a parameter.
/// </summary>
public enum NoireRemoteControl
{
    /// <summary>The console picks from the schema.</summary>
    Auto,

    /// <summary>A single-line text field.</summary>
    Text,

    /// <summary>A text area.</summary>
    Multiline,

    /// <summary>A slider. Needs a minimum and a maximum.</summary>
    Slider,

    /// <summary>A JSON text area, whatever the schema says.</summary>
    Json,
}
