using NoireLib.Remote;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;

namespace NoireLib.Websocket;

/// <summary>The settings of one outbound WebSocket connection. Every default works. A change applies at the next connect.</summary>
public sealed class NoireWebsocketClientOptions
{
    /// <summary>
    /// The smallest receive buffer accepted. A value below it is raised to it when the connection opens.
    /// </summary>
    public const int MinimumReceiveBufferSize = 256;

    /// <summary>
    /// Gets the sub-protocols offered in the handshake, in order of preference.
    /// </summary>
    public IList<string> SubProtocols { get; } = new List<string>();

    /// <summary>
    /// Gets or sets the HTTP-level settings the handshake is sent with.
    /// </summary>
    public NoireSocketHttpOptions Http { get; set; } = new();

    /// <summary>
    /// Gets or sets how often the transport sends a keep-alive frame. Zero sends none.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long nothing may arrive before the connection is dropped and reconnected. Zero never times
    /// out. This is what detects a dead peer: the keep-alive this runtime sends is not answered in any way the
    /// client can observe.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Gets or sets the largest reassembled message accepted, in bytes. A larger one is read to its end and
    /// discarded, <see cref="NoireSocketMessageTooLargeException"/> reaches the error handlers, and the connection
    /// stays open.
    /// </summary>
    public int MaxMessageSize { get; set; } = 1024 * 1024;

    /// <summary>
    /// Gets or sets the receive buffer size in bytes, floored at <see cref="MinimumReceiveBufferSize"/>.
    /// </summary>
    public int ReceiveBufferSize { get; set; } = 16 * 1024;

    /// <summary>
    /// Gets or sets the reconnection schedule.
    /// </summary>
    public NoireRetryPolicy Retry { get; set; } = NoireRetryPolicy.Default;

    /// <summary>
    /// Gets or sets how many messages may wait to be written before <see cref="Overflow"/> decides what happens.
    /// </summary>
    public int SendQueueCapacity { get; set; } = 1024;

    /// <summary>
    /// Gets or sets what a full send queue does.
    /// </summary>
    public NoireSocketSendOverflow Overflow { get; set; } = NoireSocketSendOverflow.Fail;

    /// <summary>
    /// Gets or sets the thread the callbacks run on. Inherit takes the host's own default.
    /// </summary>
    public NoireRemoteThread Thread { get; set; } = NoireRemoteThread.Inherit;

    /// <summary>
    /// Gets or sets the host the callbacks are delivered through. Null takes the host installed when the connection
    /// opens.
    /// </summary>
    public INoireRemoteHost? Host { get; set; } = null;

    /// <summary>
    /// Gets or sets a hook over the socket options of every attempt, run last so it overrides everything above it.
    /// <c>KeepAliveTimeout</c> is only reachable here, being absent from the reference assemblies this library
    /// compiles against and present on the runtime it executes on.
    /// </summary>
    public Action<ClientWebSocketOptions>? ConfigureSocket { get; set; } = null;

    /// <summary>
    /// Gets or sets a hook over the HTTP handler the client owns, run once when the handler is built at the first
    /// connect.
    /// </summary>
    public Action<SocketsHttpHandler>? ConfigureHandler { get; set; } = null;

    /// <summary>
    /// Copies these options.
    /// </summary>
    /// <returns>A copy sharing no mutable state with this one.</returns>
    public NoireWebsocketClientOptions Clone()
    {
        var copy = new NoireWebsocketClientOptions
        {
            Http = Http.Clone(),
            KeepAliveInterval = KeepAliveInterval,
            IdleTimeout = IdleTimeout,
            MaxMessageSize = MaxMessageSize,
            ReceiveBufferSize = ReceiveBufferSize,
            Retry = Retry,
            SendQueueCapacity = SendQueueCapacity,
            Overflow = Overflow,
            Thread = Thread,
            Host = Host,
            ConfigureSocket = ConfigureSocket,
            ConfigureHandler = ConfigureHandler,
        };

        foreach (var protocol in SubProtocols)
            copy.SubProtocols.Add(protocol);

        return copy;
    }
}
