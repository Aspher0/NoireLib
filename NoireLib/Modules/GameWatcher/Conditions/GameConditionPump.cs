using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;

namespace NoireLib.GameWatcher;

/// <summary>
/// The shared tick pump behind waits: while at least one waiter is active, it evaluates them once per
/// framework tick, and detaches entirely when the last waiter completes - waits are demand-activated like
/// everything else. All completions run inline on the framework thread.
/// </summary>
internal static class GameConditionPump
{
    private static readonly object Gate = new();
    private static readonly List<Func<DateTimeOffset, bool>> Waiters = new();
    private static bool attached;

    /// <summary>
    /// Registers a waiter. The callback is invoked once per framework tick with the current UTC time and
    /// returns true when the wait completed (the waiter is then removed).
    /// </summary>
    public static void Register(Func<DateTimeOffset, bool> waiter)
    {
        ArgumentNullException.ThrowIfNull(waiter);

        lock (Gate)
        {
            Waiters.Add(waiter);

            if (!attached && NoireService.IsInitialized())
            {
                NoireService.Framework.Update += OnUpdate;
                attached = true;
            }
        }
    }

    /// <summary>The number of active waiters, for diagnostics.</summary>
    public static int ActiveWaiterCount
    {
        get
        {
            lock (Gate)
                return Waiters.Count;
        }
    }

    private static void OnUpdate(IFramework framework)
    {
        Func<DateTimeOffset, bool>[] snapshot;

        lock (Gate)
        {
            if (Waiters.Count == 0)
            {
                NoireService.Framework.Update -= OnUpdate;
                attached = false;
                return;
            }

            snapshot = Waiters.ToArray();
        }

        var now = DateTimeOffset.UtcNow;
        List<Func<DateTimeOffset, bool>>? completed = null;

        foreach (var waiter in snapshot)
        {
            bool done;

            try
            {
                done = waiter(now);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError<NoireGameWatcher>(ex, "A game-condition waiter threw and was removed.");
                done = true;
            }

            if (done)
                (completed ??= new List<Func<DateTimeOffset, bool>>()).Add(waiter);
        }

        if (completed == null)
            return;

        lock (Gate)
        {
            foreach (var waiter in completed)
                Waiters.Remove(waiter);
        }
    }
}
