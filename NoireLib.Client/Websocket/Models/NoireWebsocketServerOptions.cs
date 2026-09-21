using NoireLib.Remote;
using System;

namespace NoireLib.Websocket;

/// <summary>The settings of every socket accepted on one listener. Every default works. An open connection keeps the values it was accepted with.</summary>
public sealed class NoireWebsocketServerOptions
{
    /// <summary>
    /// Gets or sets how many sockets the listener holds open at once. A further upgrade is refused with 503.
    /// </summary>
    public int MaxSockets { get; set; } = 64;

    /// <summary>
    /// Gets or sets how many sockets one remote address holds open at once. Every loopback connection shares
    /// 127.0.0.1. Two plugins and a console tool on one machine already count three against it. The default is set
    /// high enough that a machine talking to itself never reaches it.
    /// </summary>
    public int MaxSocketsPerAddress { get; set; } = 32;

    /// <summary>
    /// Gets or sets how many rooms one endpoint holds at once. A room stops existing when its last member leaves.
    /// </summary>
    public int MaxRooms { get; set; } = 256;

    /// <summary>
    /// Gets or sets how many rooms one connection joins at once.
    /// </summary>
    public int MaxRoomsPerConnection { get; set; } = 16;

    /// <summary>
    /// Gets or sets the longest room name accepted, in characters.
    /// </summary>
    public int MaxRoomNameLength { get; set; } = 64;

    /// <summary>
    /// Gets or sets the largest single frame accepted, in bytes. It is refused off the length field, before anything
    /// is reserved for it.
    /// </summary>
    public int MaxFrameSize { get; set; } = 1024 * 1024;

    /// <summary>
    /// Gets or sets the largest reassembled message accepted, in bytes.
    /// </summary>
    public int MaxMessageSize { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// Gets or sets how many fragments one message arrives in before the connection is closed.
    /// </summary>
    public int MaxMessageFragments { get; set; } = 256;

    /// <summary>
    /// Gets or sets how many messages wait to be written to one peer before <see cref="Overflow"/> decides what
    /// happens.
    /// </summary>
    public int PeerQueueCapacity { get; set; } = 256;

    /// <summary>
    /// Gets or sets what a full per-peer queue does. A stopped-reading peer looks like this.
    /// </summary>
    public NoireWebsocketPeerOverflow Overflow { get; set; } = NoireWebsocketPeerOverflow.CloseConnection;

    /// <summary>
    /// Gets or sets how long nothing may arrive from a peer before its connection is dropped. Zero never times out.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Gets or sets how long an idle connection waits before a ping is sent to it. Zero sends none.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long a close waits for the peer to echo it before the socket is dropped.
    /// </summary>
    public TimeSpan CloseTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the thread handlers run on when neither the endpoint nor its attribute names one. Inherit is the framework thread inside a plugin.</summary>
    public NoireRemoteThread Thread { get; set; } = NoireRemoteThread.Inherit;

    /// <summary>
    /// Copies these options.
    /// </summary>
    /// <returns>A copy sharing no state with this one.</returns>
    public NoireWebsocketServerOptions Clone()
        => new()
        {
            MaxSockets = MaxSockets,
            MaxSocketsPerAddress = MaxSocketsPerAddress,
            MaxRooms = MaxRooms,
            MaxRoomsPerConnection = MaxRoomsPerConnection,
            MaxRoomNameLength = MaxRoomNameLength,
            MaxFrameSize = MaxFrameSize,
            MaxMessageSize = MaxMessageSize,
            MaxMessageFragments = MaxMessageFragments,
            PeerQueueCapacity = PeerQueueCapacity,
            Overflow = Overflow,
            IdleTimeout = IdleTimeout,
            HeartbeatInterval = HeartbeatInterval,
            CloseTimeout = CloseTimeout,
            Thread = Thread,
        };
}
