using System;

namespace NoireLib.UI;

/// <summary>
/// The hub's diagnostics facade.
/// </summary>
public static partial class NoireUI
{
    /// <summary>
    /// What NoireUI knows about itself: live counts, recent faults, the fault ladder and the stack-leak net.
    /// </summary>
    public static UiDiagnostics Diagnostics { get; } = new();

    /// <summary>
    /// What each part of the interface costs to build, per frame, by name, off by default.
    /// </summary>
    public static UiProfiler Profiler { get; } = new();

    /// <summary>
    /// Runs a block of drawing with its cost recorded against <paramref name="name"/>, alongside the library's own
    /// widgets in <see cref="UiProfiler.Snapshot()"/>.
    /// </summary>
    /// <param name="name">The name to record the cost under.</param>
    /// <param name="body">The drawing to measure.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Profile(string name, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Profile(name, body, static b => b());
    }

    /// <summary>
    /// Runs a block of drawing with its cost recorded against <paramref name="name"/>, alongside the library's own
    /// widgets in <see cref="UiProfiler.Snapshot()"/>.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="name">The name to record the cost under.</param>
    /// <param name="state">The value passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to measure.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Profile<TState>(string name, TState state, Action<TState> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        using var scope = Profiler.Measure(name);
        body(state);
    }

    /// <summary>
    /// How many <see cref="RunOnDraw"/> actions have been dropped because the queue was full, since startup.
    /// </summary>
    public static int DroppedDrawActions => DrawPump.DroppedCount;
}
