using System;
using System.Collections.Generic;
using System.Threading;

namespace NoireLib.Remote.Internal;

// Names match ignoring case, like the documented routes.
internal sealed class HttpRouteTable
{
    private readonly object gate = new();
    private readonly Dictionary<string, EndpointEntry> endpoints = new(StringComparer.OrdinalIgnoreCase);
    private int version;

    internal sealed class EndpointEntry
    {
        public string Name = string.Empty;
        public Type DeclaringType = typeof(object);
        public NoireRemoteAccess Access = NoireRemoteAccess.Local;
        public string? Summary;
        public readonly Dictionary<string, NoireRemoteMemberInfo> Members = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<NoireRemoteEventInfo> Events = [];
    }

    public int Version => Volatile.Read(ref version);

    public void Add(
        string endpoint,
        Type declaringType,
        NoireRemoteAccess access,
        IReadOnlyList<NoireRemoteMemberInfo> members,
        bool fromType,
        IReadOnlyList<NoireRemoteEventInfo>? events = null,
        string? summary = null)
    {
        if (!NoireRemotePaths.IsValidName(endpoint))
            throw new InvalidOperationException("'" + endpoint + "' is not a usable endpoint name. It may not be blank, start with an underscore or carry a slash.");

        lock (gate)
        {
            if (!endpoints.TryGetValue(endpoint, out var entry))
            {
                entry = new EndpointEntry { Name = endpoint, DeclaringType = declaringType, Access = access };
                endpoints[endpoint] = entry;
            }
            else if (fromType && entry.DeclaringType != declaringType)
            {
                throw new InvalidOperationException(
                    "Two types claim the HTTP endpoint '" + endpoint + "': " + entry.DeclaringType.FullName + " and " + declaringType.FullName + ".");
            }

            foreach (var member in members)
            {
                if (entry.Members.ContainsKey(member.Name))
                    throw new InvalidOperationException("The HTTP endpoint '" + endpoint + "' already publishes a member named '" + member.Name + "'.");
            }

            foreach (var member in members)
                entry.Members[member.Name] = member;

            if (summary != null)
                entry.Summary = summary;

            if (events != null)
                entry.Events.AddRange(events);

            Interlocked.Increment(ref version);
        }
    }

    public void Remove(string endpoint, IReadOnlyList<NoireRemoteMemberInfo> members, IReadOnlyList<NoireRemoteEventInfo>? events = null)
    {
        lock (gate)
        {
            if (!endpoints.TryGetValue(endpoint, out var entry))
                return;

            foreach (var member in members)
            {
                if (entry.Members.TryGetValue(member.Name, out var published) && ReferenceEquals(published, member))
                    entry.Members.Remove(member.Name);
            }

            if (events != null)
            {
                foreach (var declared in events)
                    entry.Events.Remove(declared);
            }

            if (entry.Members.Count == 0 && entry.Events.Count == 0)
                endpoints.Remove(endpoint);

            Interlocked.Increment(ref version);
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            endpoints.Clear();
            Interlocked.Increment(ref version);
        }
    }

    public bool TryGetEndpoint(string endpoint, out EndpointEntry? entry)
    {
        lock (gate)
            return endpoints.TryGetValue(endpoint, out entry);
    }

    public bool TryGetMember(string endpoint, string member, out NoireRemoteMemberInfo? found, out EndpointEntry? entry)
    {
        found = null;

        lock (gate)
        {
            if (!endpoints.TryGetValue(endpoint, out entry))
                return false;

            return entry.Members.TryGetValue(member, out found);
        }
    }

    public IReadOnlyList<string> EndpointNames()
    {
        lock (gate)
            return [.. endpoints.Keys];
    }

    public IReadOnlyList<string> MemberNames(string endpoint)
    {
        lock (gate)
            return endpoints.TryGetValue(endpoint, out var entry) ? [.. entry.Members.Keys] : [];
    }

    public IReadOnlyList<NoireRemoteApiInfo> Snapshot()
    {
        lock (gate)
        {
            var list = new List<NoireRemoteApiInfo>(endpoints.Count);

            foreach (var entry in endpoints.Values)
            {
                var members = new List<NoireRemoteMemberInfo>(entry.Members.Values);
                members.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

                list.Add(new NoireRemoteApiInfo(
                    entry.Name,
                    entry.DeclaringType,
                    entry.Access,
                    members,
                    entry.Events.Count == 0 ? null : [.. entry.Events],
                    entry.Summary));
            }

            list.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
            return list;
        }
    }
}
