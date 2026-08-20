using System;
using System.Collections.Generic;

namespace NoireLib.Hooking;

/// <summary>
/// A live handle over every hook sharing a group name, for enabling, disabling and disposing them together.
/// Hooks created after the handle was taken are included.
/// </summary>
public sealed class HookGroup
{
    /// <summary>Creates a handle over the hooks carrying a group name.</summary>
    /// <param name="name">The group name.</param>
    internal HookGroup(string name) => Name = name;

    /// <summary>The group name.</summary>
    public string Name { get; }

    /// <summary>The hooks currently in the group.</summary>
    public IReadOnlyList<INoireHook> Hooks
    {
        get
        {
            var matches = new List<INoireHook>();

            foreach (var hook in HookRegistry.Snapshot())
            {
                if (string.Equals(hook.Group, Name, StringComparison.Ordinal) && !hook.IsDisposed)
                    matches.Add(hook);
            }

            return matches;
        }
    }

    /// <summary>The number of hooks in the group.</summary>
    public int Count => Hooks.Count;

    /// <summary>Enables every hook in the group.</summary>
    public void Enable() => SetEnabled(true);

    /// <summary>Disables every hook in the group.</summary>
    public void Disable() => SetEnabled(false);

    /// <summary>Sets the enabled state of every hook in the group.</summary>
    /// <param name="enabled">The desired state.</param>
    public void SetEnabled(bool enabled)
    {
        foreach (var hook in Hooks)
            hook.SetEnabled(enabled);
    }

    /// <summary>Disposes every hook in the group.</summary>
    public void Dispose()
    {
        foreach (var hook in Hooks)
            hook.Dispose();
    }

    /// <summary>
    /// Enables every hook in the group until the returned scope is disposed, restoring each hook's previous state.
    /// </summary>
    /// <returns>The scope.</returns>
    public IDisposable EnabledScope() => new HookStateScope(Hooks, true);

    /// <summary>
    /// Disables every hook in the group until the returned scope is disposed, restoring each hook's previous state.
    /// </summary>
    /// <returns>The scope.</returns>
    public IDisposable DisabledScope() => new HookStateScope(Hooks, false);

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({Count} hooks)";
}
