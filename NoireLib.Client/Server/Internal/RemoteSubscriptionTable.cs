using System;
using System.Collections.Generic;

namespace NoireLib.Remote.Internal;

// Topics match by prefix like the HTTP event filter: "nav." reaches "nav.route.done".
internal sealed class RemoteSubscriptionTable
{
    internal const string ReservedPrefix = "_instance.";

    private readonly Dictionary<Guid, HashSet<string>> prefixes = [];
    private readonly object gate = new();

    internal int Count
    {
        get
        {
            lock (gate)
                return prefixes.Count;
        }
    }

    internal void Add(Guid connection, string? prefix)
    {
        lock (gate)
        {
            if (!prefixes.TryGetValue(connection, out var held))
            {
                held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                prefixes[connection] = held;
            }

            // An empty prefix asks for everything the connection may see.
            held.Add(prefix ?? string.Empty);
        }
    }

    internal void Remove(Guid connection, string? prefix)
    {
        lock (gate)
        {
            if (!prefixes.TryGetValue(connection, out var held))
                return;

            held.Remove(prefix ?? string.Empty);

            if (held.Count == 0)
                prefixes.Remove(connection);
        }
    }

    internal void RemoveAll(Guid connection)
    {
        lock (gate)
            prefixes.Remove(connection);
    }

    internal bool Matches(Guid connection, string topic)
    {
        // What the instance says about itself reaches every connected caller, subscribed or not.
        if (topic.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase))
            return true;

        lock (gate)
        {
            if (!prefixes.TryGetValue(connection, out var held))
                return false;

            foreach (var prefix in held)
            {
                if (prefix.Length == 0 || topic.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
