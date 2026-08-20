using System;
using System.Collections.Generic;

namespace NoireLib.Hooking;

/// <summary>
/// Forces a set of hooks to one state and restores each hook's own previous state on disposal,
/// rather than restoring them all to the same state.
/// </summary>
internal sealed class HookStateScope : IDisposable
{
    private readonly List<(INoireHook Hook, bool WasEnabled)> restore = [];

    private bool disposed;

    /// <summary>
    /// Applies the state and records what each hook was doing before.
    /// </summary>
    /// <param name="hooks">The hooks to change.</param>
    /// <param name="enabled">The state to apply for the lifetime of the scope.</param>
    public HookStateScope(IEnumerable<INoireHook> hooks, bool enabled)
    {
        foreach (var hook in hooks)
        {
            restore.Add((hook, hook.IsEnabled));
            hook.SetEnabled(enabled);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        foreach (var (hook, wasEnabled) in restore)
        {
            if (!hook.IsDisposed)
                hook.SetEnabled(wasEnabled);
        }

        restore.Clear();
    }
}
