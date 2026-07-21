using System;

namespace NoireLib.UI;

/// <summary>
/// One open measurement, closed when it is disposed. Created by <see cref="UiProfilerExtensions.Measure"/>.
/// </summary>
/// <remarks>
/// A <see langword="ref struct"/> so it lives on the stack and a measurement costs no allocation, which matters for a
/// thing wrapped around every widget in the library.<br/>
/// Closing twice is a no-op. That is not defensive tidiness: <c>using var scope = ...</c> followed by an explicit
/// <c>scope.Dispose()</c> disposes once explicitly and once at the end of the block, and without this the scope would
/// be counted twice, the second reading running from its original start to the end of the enclosing block.
/// </remarks>
public ref struct UiProfileScope
{
    private readonly UiProfiler? profiler;
    private readonly string name;
    private readonly long started;

    private bool closed;

    internal UiProfileScope(UiProfiler profiler, string name)
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
/// Opens the measurement a widget records itself under.
/// </summary>
/// <remarks>
/// The widgets are not <see cref="NoireDrawable"/>s and are drawn by their owner rather than by the hub, so measuring
/// the hub's pass alone would report nothing for most of an interface. Each widget opens one of these instead.
/// </remarks>
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
        // The name is composed only while the profiler is on, since composing it costs the same dictionary lookup the
        // measurement itself would.
        var profiler = NoireUI.Profiler;
        return profiler.Measure(profiler.Enabled ? UiIds.Join(kind, ":", id) : string.Empty);
    }

    /// <summary>
    /// Times a static helper, aggregating every call in the frame under one name.
    /// </summary>
    /// <remarks>
    /// Helpers have no id to be told apart by and are called many times a frame, so one row carrying the whole frame's
    /// worth of them is the useful shape: what a reader wants to know is what all the text, or all the buttons, cost,
    /// not what the eleventh one did.<br/>
    /// The name is a constant at every call site, so there is nothing to compose and nothing to guard.
    /// </remarks>
    /// <param name="name">The helper's name.</param>
    /// <returns>The open scope. Dispose it to close the measurement.</returns>
    internal static UiProfileScope Helper(string name) => NoireUI.Profiler.Measure(name);
}

/// <summary>
/// Opens a measurement on a <see cref="UiProfiler"/>.
/// </summary>
public static class UiProfilerExtensions
{
    /// <summary>
    /// Times everything up to the returned scope's disposal, under <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    /// This is the <see langword="using"/>-shaped form, used inside the library where a widget's body is not already a
    /// callback. Prefer <see cref="NoireUI.Profile(string, Action)"/> from a plugin, which needs nothing disposed.
    /// </remarks>
    /// <param name="profiler">The profiler to measure on.</param>
    /// <param name="name">The scope's name.</param>
    /// <returns>The open scope. Dispose it to close the measurement.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profiler"/> is <see langword="null"/>.</exception>
    public static UiProfileScope Measure(this UiProfiler profiler, string name)
    {
        ArgumentNullException.ThrowIfNull(profiler);
        return new UiProfileScope(profiler, name);
    }
}
