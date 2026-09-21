using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Websocket;

/// <summary>
/// A named group of connections on one endpoint. A room exists for as long as it has a member. The last connection
/// to leave, or to close, takes it with it. This is what keeps <see cref="NoireWebsocketServerOptions.MaxRooms"/>
/// counting live rooms.
/// </summary>
public sealed class NoireWebsocketRoom
{
    private readonly object gate;
    private readonly Dictionary<Guid, NoireWebsocketConnection> members = [];

    internal NoireWebsocketRoom(NoireWebsocketEndpoint endpoint, string name, object gate)
    {
        Endpoint = endpoint;
        Name = name;
        this.gate = gate;
    }

    /// <summary>
    /// Gets the room name, as the first connection to join it spelled the name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the endpoint the room belongs to.
    /// </summary>
    public NoireWebsocketEndpoint Endpoint { get; }

    /// <summary>
    /// Gets how many connections are in the room.
    /// </summary>
    public int Count
    {
        get
        {
            lock (gate)
                return members.Count;
        }
    }

    /// <summary>
    /// Gets the connections in the room, as a snapshot taken now. Enumerating it is safe while other connections
    /// join, leave or close.
    /// </summary>
    public IReadOnlyList<NoireWebsocketConnection> Clients
    {
        get
        {
            lock (gate)
                return [.. members.Values];
        }
    }

    /// <summary>
    /// Checks whether a connection is in the room.
    /// </summary>
    /// <param name="connection">The connection to look for.</param>
    /// <returns>True when it is a member.</returns>
    public bool Contains(NoireWebsocketConnection connection)
    {
        if (connection == null)
            return false;

        lock (gate)
            return members.ContainsKey(connection.Id);
    }

    /// <summary>
    /// Queues a text message to every member and returns without waiting for it to be written.
    /// </summary>
    /// <param name="text">The message.</param>
    /// <param name="except">A connection to leave out, such as the sender.</param>
    /// <param name="where">A further test a member has to pass, or null for all of them.</param>
    public void Broadcast(string text, NoireWebsocketConnection? except = null, Func<NoireWebsocketConnection, bool>? where = null)
        => Endpoint.Deliver(Clients, NoireWebsocketMessageKind.Text, System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty), except, where);

    /// <summary>
    /// Queues a binary message to every member and returns without waiting for it to be written.
    /// </summary>
    /// <param name="bytes">The payload.</param>
    /// <param name="except">A connection to leave out.</param>
    /// <param name="where">A further test a member has to pass, or null for all of them.</param>
    public void Broadcast(byte[] bytes, NoireWebsocketConnection? except = null, Func<NoireWebsocketConnection, bool>? where = null)
        => Endpoint.Deliver(Clients, NoireWebsocketMessageKind.Binary, bytes ?? [], except, where);

    /// <summary>
    /// Sends a text message to every member and waits until each has been written or has failed.
    /// </summary>
    /// <param name="text">The message.</param>
    /// <param name="except">A connection to leave out.</param>
    /// <param name="where">A further test a member has to pass, or null for all of them.</param>
    /// <param name="cancellationToken">A token abandoning the wait only.</param>
    /// <returns>A task completing when every member has been written to.</returns>
    public Task BroadcastAsync(string text, NoireWebsocketConnection? except = null,
        Func<NoireWebsocketConnection, bool>? where = null, CancellationToken cancellationToken = default)
        => Endpoint.DeliverAsync(Clients, NoireWebsocketMessageKind.Text, System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty), except, where, cancellationToken);

    /// <summary>
    /// Sends a binary message to every member and waits until each has been written or has failed.
    /// </summary>
    /// <param name="bytes">The payload.</param>
    /// <param name="except">A connection to leave out.</param>
    /// <param name="where">A further test a member has to pass, or null for all of them.</param>
    /// <param name="cancellationToken">A token abandoning the wait only.</param>
    /// <returns>A task completing when every member has been written to.</returns>
    public Task BroadcastAsync(byte[] bytes, NoireWebsocketConnection? except = null,
        Func<NoireWebsocketConnection, bool>? where = null, CancellationToken cancellationToken = default)
        => Endpoint.DeliverAsync(Clients, NoireWebsocketMessageKind.Binary, bytes ?? [], except, where, cancellationToken);

    // Callers hold the table's gate.
    internal bool AddUnderGate(NoireWebsocketConnection connection)
        => members.TryAdd(connection.Id, connection);

    internal bool RemoveUnderGate(NoireWebsocketConnection connection)
        => members.Remove(connection.Id);

    internal bool IsEmptyUnderGate
        => members.Count == 0;
}
