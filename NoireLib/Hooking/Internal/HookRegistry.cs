using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;

namespace NoireLib.Hooking;

/// <summary>
/// Holds every live hook, detects two hooks landing on one address, and drives the retry pump that
/// installs deferred hooks once their address exists.
/// </summary>
internal static class HookRegistry
{
    private static readonly object Gate = new();
    private static readonly List<INoireHook> Hooks = [];
    private static readonly List<PendingHook> Pending = [];

    /// <summary>
    /// Filled and walked only by the framework thread inside the pump, so it needs no lock of its own, and kept
    /// between frames so a waiting hook costs no allocation per frame.
    /// </summary>
    private static readonly List<PendingHook> PendingScratch = [];

    private static bool pumpAttached;

    /// <summary>
    /// Gets a counter incremented whenever the set of hooks or their states change, so a view can cache against it.
    /// </summary>
    public static int Version { get; private set; }

    /// <summary>
    /// Gets the number of live hooks.
    /// </summary>
    public static int Count
    {
        get
        {
            lock (Gate)
                return Hooks.Count;
        }
    }

    /// <summary>
    /// Returns a snapshot of every live hook.
    /// </summary>
    /// <returns>The snapshot.</returns>
    public static IReadOnlyList<INoireHook> Snapshot()
    {
        lock (Gate)
            return Hooks.ToArray();
    }

    /// <summary>
    /// Adds a hook and warns when another live hook already occupies its address.
    /// </summary>
    /// <param name="hook">The hook to add.</param>
    public static void Register(INoireHook hook)
    {
        INoireHook? conflict = null;

        lock (Gate)
        {
            if (hook.Address != 0)
            {
                foreach (var existing in Hooks)
                {
                    if (existing.Address == hook.Address && !existing.IsDisposed)
                    {
                        conflict = existing;
                        break;
                    }
                }
            }

            Hooks.Add(hook);
            Version++;
        }

        if (conflict != null)
        {
            NoireLogger.LogWarning(
                $"Hook '{hook.Name}' targets an address already hooked by '{conflict.Name}' ({HookSignatureFormatter.FormatAddress(hook.Address)}). One detour will run inside the other, and disabling either changes what the other sees.",
                HookLog.Prefix);
        }
    }

    /// <summary>
    /// Removes a hook.
    /// </summary>
    /// <param name="hook">The hook to remove.</param>
    public static void Unregister(INoireHook hook)
    {
        lock (Gate)
        {
            Hooks.Remove(hook);
            Version++;
        }
    }

    /// <summary>
    /// Records that a hook changed state, so cached views rebuild.
    /// </summary>
    public static void NotifyChanged()
    {
        lock (Gate)
            Version++;
    }

    /// <summary>
    /// Gets the number of hooks still waiting for their address.
    /// </summary>
    public static int PendingCount
    {
        get
        {
            lock (Gate)
                return Pending.Count;
        }
    }

    /// <summary>
    /// Adds a retry callback that runs on every framework update until it removes itself.
    /// </summary>
    /// <param name="hook">The hook the callback belongs to, so a failure can name it.</param>
    /// <param name="retry">The callback.</param>
    public static void AddPending(INoireHook hook, Action retry)
    {
        lock (Gate)
        {
            Pending.Add(new PendingHook(hook, retry));
            AttachPump();
        }
    }

    /// <summary>
    /// Removes a retry callback.
    /// </summary>
    /// <param name="retry">The callback.</param>
    public static void RemovePending(Action retry)
    {
        lock (Gate)
        {
            Pending.RemoveAll(pending => pending.Retry == retry);
            DetachPumpIfIdle();
        }
    }

    private static void AttachPump()
    {
        if (pumpAttached || Pending.Count == 0 || !NoireService.IsInitialized())
            return;

        NoireService.Framework.Update += OnFrameworkUpdate;
        pumpAttached = true;
    }

    private static void DetachPumpIfIdle()
    {
        if (!pumpAttached || Pending.Count > 0)
            return;

        NoireService.Framework.Update -= OnFrameworkUpdate;
        pumpAttached = false;
    }

    private static void OnFrameworkUpdate(IFramework framework)
    {
        // Copied out first: a retry that succeeds removes itself and would mutate the list being walked.
        lock (Gate)
        {
            PendingScratch.Clear();
            PendingScratch.AddRange(Pending);
        }

        foreach (var pending in PendingScratch)
        {
            try
            {
                pending.Retry();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"The deferred retry for hook '{pending.Hook.Name}' threw.", HookLog.Prefix);
            }
        }

        PendingScratch.Clear();
    }

    private readonly record struct PendingHook(INoireHook Hook, Action Retry);
}
