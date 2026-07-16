using System;

namespace NoireLib.IPC;

/// <summary>
/// Represents a consumer binding created by attaching an IPC invocation delegate to a target method.<br/>
/// Disposing the binding detaches the delegate from the target so the property, field or event is restored to its prior value.
/// </summary>
public sealed class NoireIpcConsumerBinding : NoireIpcHandle
{
    internal NoireIpcConsumerBinding(string fullName, Action unbindAction, Action<NoireIpcHandle>? disposedCallback)
        : base(fullName, unbindAction, disposedCallback)
    {
    }
}
