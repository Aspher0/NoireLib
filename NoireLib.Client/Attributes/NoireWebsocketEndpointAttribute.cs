using NoireLib.Remote;
using System;

namespace NoireLib.Websocket;

/// <summary>Publishes a type's <see cref="NoireWebsocketOnAttribute"/> methods as a socket, reachable at <c>/noire/ws/{name}</c>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class NoireWebsocketEndpointAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireWebsocketEndpointAttribute"/> class, taking the socket name from the type name.
    /// </summary>
    public NoireWebsocketEndpointAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NoireWebsocketEndpointAttribute"/> class with an explicit socket name.
    /// </summary>
    /// <param name="name">The socket name. It may not start with an underscore and may not contain a slash.</param>
    public NoireWebsocketEndpointAttribute(string? name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets the socket name. When null the type name is used.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the sub-protocols the socket speaks, in order of preference.
    /// </summary>
    public string[]? SubProtocols { get; init; }

    /// <summary>
    /// Gets whether a handshake offering none of <see cref="SubProtocols"/> is refused. False accepts it with none negotiated.
    /// </summary>
    public bool RequireSubProtocol { get; init; }

    /// <summary>
    /// Gets the thread handlers run on. Inherit asks the server options, then the host.
    /// </summary>
    public NoireRemoteThread Thread { get; init; } = NoireRemoteThread.Inherit;

    /// <summary>
    /// Gets the character state a message needs before it is delivered, checked once per drain.
    /// </summary>
    public NoireRemoteReadiness Requires { get; init; } = NoireRemoteReadiness.Inherit;

    /// <summary>
    /// Gets whether a peer on another machine may upgrade. Defaults to <see cref="NoireRemoteAccess.Local"/>.
    /// </summary>
    public NoireRemoteAccess Access { get; init; } = NoireRemoteAccess.Local;
}
