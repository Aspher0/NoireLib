using System;

namespace NoireLib.UI;

/// <summary>
/// One open measurement, closed when it is disposed. Created by
/// <see cref="UiProfilerExtensions.Measure(UiProfiler, string)"/>.<br/>
/// Closing twice is a no-op.
/// </summary>
public ref struct UiProfileScope
{
    private readonly UiProfiler? profiler;
    private readonly UiScopeName? name;
    private readonly long started;

    private bool closed;

    internal UiProfileScope(UiProfiler profiler, UiScopeName? name)
    {
        this.name = name;
        started = profiler.Open(name);
        closed = false;

        // Held only when the clock actually started, so a disabled profiler leaves nothing to do on the way out.
        this.profiler = started == 0L ? null : profiler;
    }

    /// <summary>
    /// Closes the measurement. Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (closed)
            return;

        closed = true;
        profiler?.Close(name, started);
    }
}

/// <summary>
/// Opens the measurement an instance widget records itself under.
/// </summary>
internal static class UiProfile
{
    /// <summary>
    /// Times a widget's draw under <c>{kind}:{id}</c>.
    /// </summary>
    /// <param name="kind">The widget's type name.</param>
    /// <param name="id">The widget's own id.</param>
    /// <returns>The open scope. Dispose it to close the measurement.</returns>
    internal static UiProfileScope Widget(string kind, string id)
    {
        // Composed only while the profiler is on, since composing it costs a dictionary lookup of its own. UiIds
        // returns the same string instance for the same widget every frame, so the handle is found by reference
        // rather than by hashing the composed id again. Built directly rather than through the Measure extension,
        // sparing a call on a path every widget takes every frame.
        var profiler = NoireUI.Profiler;
        return new UiProfileScope(profiler, profiler.Enabled ? UiScopeName.ForInstance(UiIds.Join(kind, ":", id)) : null);
    }
}

/// <summary>
/// Opens a measurement on a <see cref="UiProfiler"/>.
/// </summary>
public static class UiProfilerExtensions
{
    /// <summary>
    /// Times everything up to the returned scope's disposal, under <paramref name="name"/>.
    /// </summary>
    /// <param name="profiler">The profiler to measure on.</param>
    /// <param name="name">The scope's name.</param>
    /// <returns>The open scope. Dispose it to close the measurement.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profiler"/> is <see langword="null"/>.</exception>
    public static UiProfileScope Measure(this UiProfiler profiler, string name)
    {
        ArgumentNullException.ThrowIfNull(profiler);

        // Resolved only while the profiler is on, so a disabled one still costs a boolean read rather than the hash
        // that resolving a name takes.
        return new UiProfileScope(
            profiler,
            profiler.Enabled && !string.IsNullOrEmpty(name) ? UiScopeName.For(name) : null);
    }

    /// <summary>
    /// Times everything up to the returned scope's disposal, under a name already resolved to a handle.
    /// </summary>
    /// <param name="profiler">The profiler to measure on.</param>
    /// <param name="name">The scope's name, or <see langword="null"/> for nothing to measure.</param>
    /// <returns>The open scope. Dispose it to close the measurement.</returns>
    internal static UiProfileScope Measure(this UiProfiler profiler, UiScopeName? name)
        => new(profiler, name);
}
