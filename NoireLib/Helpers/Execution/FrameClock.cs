using Dalamud.Plugin.Services;
using System.Threading;

namespace NoireLib.Helpers;

/// <summary>
/// Counts game frames, so a helper can measure an interval in frames rather than in milliseconds. Attaches to the
/// game's update on first read and stops with the library. Before NoireLib is initialized the count stands still,
/// which is what lets the frame-based helpers run in a test without a game behind them.
/// </summary>
public static class FrameClock
{
    private static readonly Lock AttachLock = new();

    private static long current;
    private static bool attached;

    /// <summary>The number of game frames counted so far.</summary>
    public static long Current
    {
        get
        {
            EnsureAttached();
            return Volatile.Read(ref current);
        }
    }

    /// <summary>Whether the clock is attached to the game's update, and so actually advancing.</summary>
    public static bool IsRunning => Volatile.Read(ref attached);

    /// <summary>Advances the count by hand, for a test with no game update to drive it.</summary>
    /// <param name="frames">How many frames to advance.</param>
    internal static void Advance(long frames = 1)
    {
        Interlocked.Add(ref current, frames);
    }

    private static void EnsureAttached()
    {
        if (Volatile.Read(ref attached) || !NoireService.IsInitialized())
            return;

        lock (AttachLock)
        {
            if (attached)
                return;

            NoireService.Framework.Update += OnFrameworkUpdate;
            attached = true;

            NoireLibMain.RegisterOnDispose("NoireLib_Internal_FrameClock", Detach);
        }
    }

    private static void OnFrameworkUpdate(IFramework framework)
    {
        Interlocked.Increment(ref current);
    }

    private static void Detach()
    {
        lock (AttachLock)
        {
            if (!attached)
                return;

            NoireService.Framework.Update -= OnFrameworkUpdate;
            attached = false;
        }
    }
}
