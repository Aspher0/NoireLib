using NoireLib.Remote;
using System;

namespace NoireLib.Websocket;

/// <summary>
/// Attaches a method of a <see cref="NoireWebsocketEndpointAttribute"/> type to the socket. The method signature
/// picks the callback: <c>(NoireWebsocketConnection, NoireWebsocketMessage)</c> is a message,
/// <c>(NoireWebsocketConnection, NoireWebsocketClose)</c> a close, <c>(NoireWebsocketConnection)</c> an open,
/// <c>(Exception)</c> an error. Each returns void or an awaited <c>Task</c>.<br/>
/// The handler runs on the endpoint's thread. Delivery on one connection stays ordered.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NoireWebsocketOnAttribute : Attribute
{
    /// <summary>
    /// Gets the character state this handler needs before a message reaches it. Inherit asks the endpoint. Checked
    /// once per drain.
    /// </summary>
    public NoireRemoteReadiness Requires { get; init; } = NoireRemoteReadiness.Inherit;

    /// <summary>
    /// Gets whether a message from a peer on another machine reaches this handler. Inherit asks the endpoint.
    /// </summary>
    public NoireRemoteAccess Access { get; init; } = NoireRemoteAccess.Inherit;

    /// <summary>
    /// Gets a name for the subscription. Attaching again under the same name replaces the previous handler.
    /// </summary>
    public string? Key { get; init; }
}
