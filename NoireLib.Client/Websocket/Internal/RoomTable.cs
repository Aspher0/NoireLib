using System;
using System.Collections.Generic;

namespace NoireLib.Websocket.Internal;

// One lock for rooms and membership. Split locks would let a room outlive its last member or vanish mid-join.
internal sealed class RoomTable
{
    private readonly object gate = new();
    private readonly Dictionary<string, NoireWebsocketRoom> rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, HashSet<string>> membership = [];

    public object Gate => gate;

    public int Count
    {
        get
        {
            lock (gate)
                return rooms.Count;
        }
    }

    public IReadOnlyList<NoireWebsocketRoom> Snapshot()
    {
        lock (gate)
            return [.. rooms.Values];
    }

    public NoireWebsocketRoom? Get(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        lock (gate)
            return rooms.TryGetValue(name, out var room) ? room : null;
    }

    public IReadOnlyList<string> RoomsOf(NoireWebsocketConnection connection)
    {
        lock (gate)
            return membership.TryGetValue(connection.Id, out var joined) ? [.. joined] : [];
    }

    public bool IsIn(NoireWebsocketConnection connection, string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        lock (gate)
            return membership.TryGetValue(connection.Id, out var joined) && joined.Contains(name);
    }

    public NoireWebsocketRoom Join(NoireWebsocketEndpoint endpoint, NoireWebsocketConnection connection, string name, NoireWebsocketServerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.Length > Math.Max(1, options.MaxRoomNameLength))
            throw new ArgumentException("A room name is at most " + options.MaxRoomNameLength + " characters and this one is " + name.Length + ".", nameof(name));

        lock (gate)
        {
            membership.TryGetValue(connection.Id, out var joined);

            var already = joined != null && joined.Contains(name);
            var exists = rooms.TryGetValue(name, out var room);

            if (already)
                return room!;

            // The per-connection cap also applies to existing rooms.
            if (joined != null && joined.Count >= Math.Max(1, options.MaxRoomsPerConnection))
                throw new NoireSocketException("This connection is in as many rooms as it may join (" + options.MaxRoomsPerConnection + ").");

            if (!exists && rooms.Count >= Math.Max(1, options.MaxRooms))
                throw new NoireSocketException("This endpoint holds as many rooms as it accepts (" + options.MaxRooms + ").");

            if (!exists)
            {
                room = new NoireWebsocketRoom(endpoint, name, gate);
                rooms[name] = room;
            }

            if (joined == null)
            {
                joined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                membership[connection.Id] = joined;
            }

            joined.Add(name);
            room!.AddUnderGate(connection);

            return room;
        }
    }

    public bool Leave(NoireWebsocketConnection connection, string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        lock (gate)
        {
            if (!membership.TryGetValue(connection.Id, out var joined) || !joined.Remove(name))
                return false;

            if (joined.Count == 0)
                membership.Remove(connection.Id);

            if (rooms.TryGetValue(name, out var room))
            {
                room.RemoveUnderGate(connection);

                if (room.IsEmptyUnderGate)
                    rooms.Remove(name);
            }

            return true;
        }
    }

    public void Drop(NoireWebsocketConnection connection)
    {
        lock (gate)
        {
            if (!membership.Remove(connection.Id, out var joined))
                return;

            foreach (var name in joined)
            {
                if (!rooms.TryGetValue(name, out var room))
                    continue;

                room.RemoveUnderGate(connection);

                if (room.IsEmptyUnderGate)
                    rooms.Remove(name);
            }
        }
    }
}
