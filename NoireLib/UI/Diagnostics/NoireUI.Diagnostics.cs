using System;

namespace NoireLib.UI;

/// <summary>
/// The hub's diagnostics facade.
/// </summary>
public static partial class NoireUI
{
    /// <summary>
    /// What NoireUI knows about itself: live counts, recent faults, the fault ladder and the stack-leak net.
    /// See <see cref="UiDiagnostics"/>.
    /// </summary>
    public static UiDiagnostics Diagnostics { get; } = new();

    /// <summary>
    /// What each part of the interface costs to build, per frame, by name. Off by default.
    /// See <see cref="UiProfiler"/>.
    /// </summary>
    public static UiProfiler Profiler { get; } = new();

    /// <summary>
    /// Runs a block of drawing with its cost recorded against <paramref name="name"/>, so your own code appears on
    /// <see cref="UiProfiler.Snapshot()"/> beside the widgets the library ships.
    /// </summary>
    /// <remarks>
    /// Free while <see cref="UiProfiler.Enabled"/> is off, so this can be left in place rather than added when you go
    /// looking and removed afterwards.
    /// </remarks>
    /// <example>
    /// <code>
    /// NoireUI.Profile("inventory grid", () => DrawInventoryGrid());
    /// </code>
    /// </example>
    /// <param name="name">The name to record the cost under.</param>
    /// <param name="body">The drawing to measure.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Profile(string name, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Profile(name, body, static b => b());
    }

    /// <summary>
    /// Runs a block of drawing with its cost recorded against <paramref name="name"/>, so your own code appears on
    /// <see cref="UiProfiler.Snapshot()"/> beside the widgets the library ships.
    /// </summary>
    /// <remarks>
    /// Free while <see cref="UiProfiler.Enabled"/> is off, so this can be left in place rather than added when you go
    /// looking and removed afterwards.
    /// </remarks>
    /// <example>
    /// <code>
    /// NoireUI.Profile("inventory grid", inventory, static i => DrawInventoryGrid(i));
    /// </code>
    /// </example>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="name">The name to record the cost under.</param>
    /// <param name="state">Passed to <paramref name="body"/>, so the body can stay a static lambda.</param>
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
