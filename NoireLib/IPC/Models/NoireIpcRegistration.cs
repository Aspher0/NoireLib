using System;

namespace NoireLib.IPC;

/// <summary>
/// Represents a tracked IPC provider registration.
/// </summary>
public sealed class NoireIpcRegistration : NoireIpcHandle
{
    internal NoireIpcRegistration(string fullName, NoireIpcRegistrationKind kind, Action disposeAction, Action<NoireIpcHandle>? disposedCallback)
        : base(fullName, disposeAction, disposedCallback)
    {
        Kind = kind;
    }

    /// <summary>
    /// Gets the registration kind used for the provider.
    /// </summary>
    /// <returns>The registration kind used for the provider.</returns>
    public NoireIpcRegistrationKind Kind { get; }
}
