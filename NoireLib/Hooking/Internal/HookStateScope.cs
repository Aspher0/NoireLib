using System;
using System.Collections.Generic;

namespace NoireLib.Hooking;

// Forces a set of hooks to one state and restores each hook's own previous state on disposal, rather than restoring
// them all to the same state.
internal sealed class HookStateScope : IDisposable
{
    private readonly List<(INoireHook Hook, bool WasEnabled)> restore = [];

    private bool disposed;

    // Applies the state and records what each hook was doing before.
    public HookStateScope(IEnumerable<INoireHook> hooks, bool enabled)
    {
        foreach (var hook in hooks)
        {
            restore.Add((hook, hook.IsEnabled));
            hook.SetEnabled(enabled);
        }
    }

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
