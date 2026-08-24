using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System;

namespace NoireLib.IPC;

/// <summary>
/// Represents a tracked IPC provider registration.
/// </summary>
public sealed class NoireIpcRegistration : NoireIpcHandle
{
    private readonly object provider;

    internal NoireIpcRegistration(string fullName, NoireIpcRegistrationKind kind, object provider, Action disposeAction, Action<NoireIpcHandle>? disposedCallback)
        : base(fullName, disposeAction, disposedCallback)
    {
        Kind = kind;
        this.provider = provider;
    }

    /// <summary>
    /// Gets the registration kind used for the provider.
    /// </summary>
    /// <returns>The registration kind used for the provider.</returns>
    public NoireIpcRegistrationKind Kind { get; }

    /// <summary>
    /// Gets how many subscribers are attached to this channel, or 0 when the call gate does not expose it.
    /// </summary>
    public int SubscriptionCount => provider is ICallGateProvider typedProvider ? typedProvider.SubscriptionCount : 0;

    /// <summary>
    /// Gets the plugin invoking this provider, read from inside the handler, or null outside a call.
    /// </summary>
    /// <seealso cref="NoireIPC.GetRegistration(string, string?, bool)"/>
    public IExposedPlugin? CurrentCaller
    {
        get
        {
            try
            {
                return provider is ICallGateProvider typedProvider ? typedProvider.GetContext()?.SourcePlugin : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
