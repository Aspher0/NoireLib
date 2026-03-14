using System;

namespace NoireLib.IPC;

/// <summary>
/// Represents a tracked IPC subscription.
/// </summary>
public sealed class NoireIpcSubscription : NoireIpcHandle
{
    internal NoireIpcSubscription(string fullName, Action disposeAction, Action<NoireIpcHandle>? disposedCallback)
        : base(fullName, disposeAction, disposedCallback)
    {
    }
}
