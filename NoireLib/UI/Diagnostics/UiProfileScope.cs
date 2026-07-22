using System;

namespace NoireLib.UI;

/// <summary>
/// One open measurement, closed when it is disposed. Created by
/// <see cref="UiProfilerExtensions.Measure(UiProfiler, string)"/>.
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
/// <remarks>
/// The widgets are not <see cref="NoireDrawable"/>s and are drawn by their owner rather than by the hub, so measuring
/// the hub's pass alone would report nothing for most of an interface. Each widget opens one of these instead.<br/>
/// Only for the <c>{kind}:{id}</c> shape, which carries a runtime id and so cannot be derived from a call site. A
/// static surface takes its name from <see cref="UiDraw"/> instead, which hands over the draw list at the same time.
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
        return profiler.Measure(profiler.Enabled ? UiScopeName.For(UiIds.Join(kind, ":", id)) : null);
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

        // Resolved only while the profiler is on, so a disabled one still costs a boolean read rather than the hash
        // that resolving a name takes.
        return new UiProfileScope(
            profiler,
            profiler.Enabled && !string.IsNullOrEmpty(name) ? UiScopeName.For(name) : null);
    }

    /// <summary>
    /// Times everything up to the returned scope's disposal, under a name already resolved to a handle.
    /// </summary>
    /// <remarks>
    /// The form the library's own hot paths use. A caller entering the same scope every frame resolves its handle once
    /// and holds it, which is what keeps a measurement from hashing its own name. See <see cref="UiScopeName"/>.
    /// </remarks>
    /// <param name="profiler">The profiler to measure on.</param>
    /// <param name="name">The scope's name, or <see langword="null"/> for nothing to measure.</param>
    /// <returns>The open scope. Dispose it to close the measurement.</returns>
    internal static UiProfileScope Measure(this UiProfiler profiler, UiScopeName? name)
        => new(profiler, name);
}
