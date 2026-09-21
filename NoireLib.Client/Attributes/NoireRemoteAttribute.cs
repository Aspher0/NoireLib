using System;

namespace NoireLib.Remote;

/// <summary>
/// Publishes one member of a <see cref="NoireRemoteClassAttribute"/> type and overrides how it is served.<br/>
/// A method publishes itself. An event publishes what it raises. A property whose type is a delegate or a NoireRemote
/// wrapper consumes someone else's member. Any other property publishes its value, read only.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Event)]
public sealed class NoireRemoteAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteAttribute"/> class, taking the name from the member name.
    /// </summary>
    public NoireRemoteAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteAttribute"/> class with an explicit name.
    /// </summary>
    /// <param name="name">The member name on the wire, or an event's topic. It may not contain a slash.</param>
    public NoireRemoteAttribute(string? name)
    {
        Name = name;
    }

    /// <summary>Gets the member name on the wire. Null uses the member name. On an event it is the topic, by default the surface and event names joined by a dot, lowercased.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the wires the member is reachable on. Inherit takes the value the class declares.
    /// </summary>
    public NoireRemoteTransport Transports { get; init; } = NoireRemoteTransport.Inherit;

    /// <summary>
    /// Gets the thread the member runs on.
    /// </summary>
    public NoireRemoteThread Thread { get; init; } = NoireRemoteThread.Inherit;

    /// <summary>
    /// Gets the readiness gate the member passes.
    /// </summary>
    public NoireRemoteReadiness Requires { get; init; } = NoireRemoteReadiness.Inherit;

    /// <summary>
    /// Gets the per-call deadline in seconds. Zero takes the class or listener default.
    /// </summary>
    public double TimeoutSeconds { get; init; }

    /// <summary>
    /// Gets whether the member is reachable from another machine.
    /// </summary>
    public NoireRemoteAccess Access { get; init; } = NoireRemoteAccess.Inherit;

    /// <summary>
    /// Gets whether a call answers with the result or with a job to poll.
    /// </summary>
    public NoireRemoteCallMode Mode { get; init; } = NoireRemoteCallMode.Inherit;

    /// <summary>
    /// Gets what the member does. It reaches the manifest and every console generated from it.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets whether a console asks before firing the member. Set it on anything that changes the world.
    /// </summary>
    public bool Confirm { get; init; }

    /// <summary>
    /// Gets the group a console lists the member under.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// Gets where the member sits in its group. Lower comes first and equal orders keep their name order.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// Gets whether the member is on its way out. It stays callable and a console says so.
    /// </summary>
    public bool Deprecated { get; init; }

    /// <summary>
    /// Gets the channel an event publishes on. Null takes the events channel. It is read on events only.
    /// </summary>
    public string? Channel { get; init; }
}
