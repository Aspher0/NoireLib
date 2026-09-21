using System;
using System.Collections.Generic;

namespace NoireLib.Remote;

/// <summary>
/// One published endpoint and the members under it.
/// </summary>
public sealed class NoireRemoteApiInfo
{
    internal NoireRemoteApiInfo(
        string name,
        Type declaringType,
        NoireRemoteAccess access,
        IReadOnlyList<NoireRemoteMemberInfo> members,
        IReadOnlyList<NoireRemoteEventInfo>? events = null,
        string? summary = null)
    {
        Name = name;
        DeclaringType = declaringType;
        Access = access;
        Members = members;
        Events = events;
        Summary = summary;
    }

    /// <summary>
    /// Gets the endpoint name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the type the endpoint was published from.
    /// </summary>
    public Type DeclaringType { get; }

    /// <summary>
    /// Gets whether the endpoint accepts a call from another machine.
    /// </summary>
    public NoireRemoteAccess Access { get; }

    /// <summary>
    /// Gets the published members.
    /// </summary>
    public IReadOnlyList<NoireRemoteMemberInfo> Members { get; }

    /// <summary>
    /// Gets the events the endpoint publishes, or null when it declares none.
    /// </summary>
    public IReadOnlyList<NoireRemoteEventInfo>? Events { get; }

    /// <summary>
    /// Gets what the endpoint is for, taken from its attribute.
    /// </summary>
    public string? Summary { get; }
}

/// <summary>
/// One event a published type declares with <see cref="NoireRemoteAttribute"/>.
/// </summary>
public sealed class NoireRemoteEventInfo
{
    internal NoireRemoteEventInfo(string name, string topic, string channel, Type? payloadClrType, Action detach)
    {
        Name = name;
        Topic = topic;
        Channel = channel;
        PayloadClrType = payloadClrType;
        Detach = detach;
    }

    /// <summary>
    /// Gets the event name as the type declares it.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the topic the event publishes under.
    /// </summary>
    public string Topic { get; }

    /// <summary>
    /// Gets the channel the event publishes on.
    /// </summary>
    public string Channel { get; }

    /// <summary>
    /// Gets the payload type, or null when the event carries nothing.
    /// </summary>
    public Type? PayloadClrType { get; }

    // A static event would otherwise outlive a plugin reload.
    internal Action Detach { get; }
}
