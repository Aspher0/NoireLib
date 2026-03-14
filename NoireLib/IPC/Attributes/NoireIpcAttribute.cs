using System;
using System.Reflection;

namespace NoireLib.IPC;

/// <summary>
/// Marks a method, property, or event for IPC registration or binding through <see cref="NoireIPC.Initialize(object, string?, bool, Type?, BindingFlags)"/> or <see cref="NoireIPC.RegisterType(Type, string?, bool, Type?, BindingFlags)"/>.
/// Methods are automatically registered as IPC providers unless configured as consumer-side subscriptions. Properties with delegate types or <see cref="NoireIpcConsumer{TDelegate}"/> are automatically bound as IPC consumers. Events can publish IPC messages or subscribe to them depending on the configured mode.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Event)]
public sealed class NoireIpcAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireIpcAttribute"/> class.
    /// </summary>
    public NoireIpcAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NoireIpcAttribute"/> class with an explicit IPC name.
    /// </summary>
    /// <param name="name">The IPC name to register for the annotated member.</param>
    public NoireIpcAttribute(string? name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets or sets the IPC name to register for the annotated member.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets or sets the explicit prefix to apply to the annotated member.
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether default prefix resolution remains enabled for the annotated member.
    /// </summary>
    public bool UseDefaultPrefix { get; init; } = true;

    /// <summary>
    /// Gets or sets how the annotated member should be processed.
    /// </summary>
    public NoireIpcMode Mode { get; init; } = NoireIpcMode.Auto;

    /// <summary>
    /// Gets or sets the target IPC behavior for the annotated member.
    /// </summary>
    public NoireIpcTargetKind Target { get; init; } = NoireIpcTargetKind.Auto;

    /// <summary>
    /// Gets or sets the registration kind to use for the annotated member.
    /// </summary>
    public NoireIpcRegistrationKind Kind { get; init; } = NoireIpcRegistrationKind.Auto;

    /// <summary>
    /// Gets or sets the trailing generic type to use for message-oriented annotated members.<br/>
    /// </summary>
    public Type? MessageResultType { get; init; }
}
