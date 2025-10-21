using System;

namespace NoireLib.EventBus;

internal class SubscriptionEntry
{
    public EventSubscriptionToken Token { get; }
    public Delegate Handler { get; }
    public int Priority { get; }
    public Func<object, bool>? Filter { get; }
    public object? Owner { get; }
    public bool IsAsync { get; }

    public SubscriptionEntry(
        EventSubscriptionToken token,
        Delegate handler,
        int priority,
        Func<object, bool>? filter,
        object? owner,
        bool isAsync)
    {
        Token = token;
        Handler = handler;
        Priority = priority;
        Filter = filter;
        Owner = owner;
        IsAsync = isAsync;
    }
}
