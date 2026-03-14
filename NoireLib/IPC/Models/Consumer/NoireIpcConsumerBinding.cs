using System;

namespace NoireLib.IPC;

/// <summary>
/// Represents a consumer binding created by attaching an IPC invocation delegate to a target method.
/// </summary>
public sealed class NoireIpcConsumerBinding : NoireIpcHandle
{
    private readonly Action _unbindAction;

    internal NoireIpcConsumerBinding(string fullName, Action unbindAction, Action<NoireIpcHandle>? disposedCallback)
        : base(fullName, () => { }, disposedCallback)
    {
        _unbindAction = unbindAction;
    }

    internal void Unbind()
    {
        _unbindAction();
    }
}
