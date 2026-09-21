using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace NoireLib.Remote;

/// <summary>
/// What a listener's published sockets are doing right now: who is connected, in which rooms, and what has crossed.
/// Served at <c>/noire/v1/_sockets</c>. The manifest says which sockets exist. This says who is on them.
/// </summary>
public sealed class NoireRemoteSocketsState
{
    /// <summary>
    /// Gets or sets one entry per published socket. A socket the system publishes for itself is left out.
    /// </summary>
    [JsonProperty("sockets")]
    public List<NoireRemoteSocketState> Sockets { get; set; } = [];
}

/// <summary>
/// One published socket and everything connected to it.
/// </summary>
public sealed class NoireRemoteSocketState
{
    /// <summary>
    /// Gets or sets the socket's name.
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path a client upgrades on.
    /// </summary>
    [JsonProperty("route")]
    public string Route { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets who may connect: <c>local</c> or <c>remote</c>.
    /// </summary>
    [JsonProperty("access")]
    public string Access { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the thread its handlers run on.
    /// </summary>
    [JsonProperty("thread")]
    public string Thread { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets every connection open on it now.
    /// </summary>
    [JsonProperty("clients")]
    public List<NoireRemoteSocketClient> Clients { get; set; } = [];

    /// <summary>
    /// Gets or sets every room that has at least one member.
    /// </summary>
    [JsonProperty("rooms")]
    public List<NoireRemoteSocketRoom> Rooms { get; set; } = [];

    /// <summary>
    /// Gets or sets the messages received since it was published, counting clients that have since left.
    /// </summary>
    [JsonProperty("messagesIn")]
    public long MessagesIn { get; set; }

    /// <summary>
    /// Gets or sets the messages sent since it was published, one per peer a broadcast reached.
    /// </summary>
    [JsonProperty("messagesOut")]
    public long MessagesOut { get; set; }

    /// <summary>
    /// Gets or sets the payload bytes received since it was published.
    /// </summary>
    [JsonProperty("bytesIn")]
    public long BytesIn { get; set; }

    /// <summary>
    /// Gets or sets the bytes written since it was published, frame headers included.
    /// </summary>
    [JsonProperty("bytesOut")]
    public long BytesOut { get; set; }
}

/// <summary>
/// One connection open on a published socket.
/// </summary>
public sealed class NoireRemoteSocketClient
{
    /// <summary>Gets or sets the connection's id. The send and close actions name it.</summary>
    [JsonProperty("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the address it came from.
    /// </summary>
    [JsonProperty("address")]
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether it came over loopback.
    /// </summary>
    [JsonProperty("isLoopback")]
    public bool IsLoopback { get; set; }

    /// <summary>
    /// Gets or sets the sub-protocol it negotiated, when it offered one.
    /// </summary>
    [JsonProperty("subProtocol", NullValueHandling = NullValueHandling.Ignore)]
    public string? SubProtocol { get; set; }

    /// <summary>
    /// Gets or sets when it connected.
    /// </summary>
    [JsonProperty("openedUtc")]
    public DateTime OpenedUtc { get; set; }

    /// <summary>
    /// Gets or sets the rooms it is in.
    /// </summary>
    [JsonProperty("rooms")]
    public IReadOnlyList<string> Rooms { get; set; } = [];

    /// <summary>
    /// Gets or sets the messages it has sent.
    /// </summary>
    [JsonProperty("messagesIn")]
    public long MessagesIn { get; set; }

    /// <summary>
    /// Gets or sets the messages it has been sent.
    /// </summary>
    [JsonProperty("messagesOut")]
    public long MessagesOut { get; set; }

    /// <summary>
    /// Gets or sets the payload bytes it has sent.
    /// </summary>
    [JsonProperty("bytesIn")]
    public long BytesIn { get; set; }

    /// <summary>
    /// Gets or sets the bytes written to it, frame headers included.
    /// </summary>
    [JsonProperty("bytesOut")]
    public long BytesOut { get; set; }

    /// <summary>
    /// Gets or sets how many messages wait in its queue.
    /// </summary>
    [JsonProperty("queued")]
    public int Queued { get; set; }
}

/// <summary>
/// A room on a published socket.
/// </summary>
public sealed class NoireRemoteSocketRoom
{
    /// <summary>
    /// Gets or sets the room's name.
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how many connections are in it.
    /// </summary>
    [JsonProperty("count")]
    public int Count { get; set; }
}

/// <summary>
/// What a socket action carries: a message to send, a room to narrow a broadcast to, or how to close a connection.
/// </summary>
public sealed class NoireRemoteSocketAction
{
    /// <summary>
    /// Gets or sets the text to send.
    /// </summary>
    [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
    public string? Text { get; set; }

    /// <summary>
    /// Gets or sets the room a broadcast is limited to. Null reaches every client.
    /// </summary>
    [JsonProperty("room", NullValueHandling = NullValueHandling.Ignore)]
    public string? Room { get; set; }

    /// <summary>
    /// Gets or sets the close code. Defaults to a normal close.
    /// </summary>
    [JsonProperty("code", NullValueHandling = NullValueHandling.Ignore)]
    public int? Code { get; set; }

    /// <summary>
    /// Gets or sets the close reason.
    /// </summary>
    [JsonProperty("reason", NullValueHandling = NullValueHandling.Ignore)]
    public string? Reason { get; set; }
}
