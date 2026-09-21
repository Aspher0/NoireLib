using System;

namespace NoireLib.Remote;

/// <summary>
/// Names a type's published surface and configures every <see cref="NoireRemoteAttribute"/> member inside it.<br/>
/// Members are reachable at <c>/noire/v1/{name}/{member}</c> and over the API socket. Required, or the member
/// attributes are inert until <see cref="NoireRemoteServer.Publish(Type, string)"/> is called by hand.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public sealed class NoireRemoteClassAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteClassAttribute"/> class, taking the name from the type name.
    /// </summary>
    public NoireRemoteClassAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteClassAttribute"/> class with an explicit name.
    /// </summary>
    /// <param name="name">The name on the wire. It may not start with an underscore and may not contain a slash.</param>
    public NoireRemoteClassAttribute(string? name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets the name on the wire. When null the type name is used, with a leading I dropped from an interface.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the wires the members are reachable on. Defaults to <see cref="NoireRemoteTransport.All"/>.
    /// </summary>
    public NoireRemoteTransport Transports { get; init; } = NoireRemoteTransport.All;

    /// <summary>
    /// Gets the thread members run on unless they override it. Inherit asks the listener options, then the host.
    /// </summary>
    public NoireRemoteThread Thread { get; init; } = NoireRemoteThread.Inherit;

    /// <summary>
    /// Gets the readiness gate members pass unless they override it.
    /// </summary>
    public NoireRemoteReadiness Requires { get; init; } = NoireRemoteReadiness.Inherit;

    /// <summary>
    /// Gets the per-call deadline in seconds. Zero takes the listener default.
    /// </summary>
    public double TimeoutSeconds { get; init; }

    /// <summary>
    /// Gets whether the surface is reachable from another machine. Defaults to <see cref="NoireRemoteAccess.Local"/>.
    /// </summary>
    public NoireRemoteAccess Access { get; init; } = NoireRemoteAccess.Local;

    /// <summary>
    /// Gets what the surface is for. It reaches the manifest and every console generated from it.
    /// </summary>
    public string? Description { get; init; }
}
