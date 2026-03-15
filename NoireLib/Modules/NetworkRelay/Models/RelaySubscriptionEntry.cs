using System;

namespace NoireLib.NetworkRelay;

internal sealed class RelaySubscriptionEntry(
    NetworkRelaySubscriptionToken token,
    Delegate handler,
    int priority,
    Func<NetworkRelayMessage, bool>? filter,
    object? owner,
    bool isAsync,
    string? key,
    string channel)
{
    public NetworkRelaySubscriptionToken Token { get; } = token;
    public Delegate Handler { get; } = handler;
    public int Priority { get; } = priority;
    public Func<NetworkRelayMessage, bool>? Filter { get; } = filter;
    public object? Owner { get; } = owner;
    public bool IsAsync { get; } = isAsync;
    public string? Key { get; } = key;
    public string Channel { get; } = channel;
}
