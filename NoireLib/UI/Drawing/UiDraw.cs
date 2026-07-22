using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;

namespace NoireLib.UI;

/// <summary>
/// Which of ImGui's draw lists a scope paints into.
/// </summary>
internal enum UiDrawTarget
{
    /// <summary>The current window's list, or the one a redirect is in force for.</summary>
    Window,

    /// <summary>The current window's own list, never a redirect. See <see cref="UiDraw.BeginWindow"/>.</summary>
    OwnWindow,

    /// <summary>The list drawn on top of every window.</summary>
    Foreground,

    /// <summary>The list drawn behind every window.</summary>
    Background,
}

/// <summary>
/// The one way a surface inside NoireUI obtains a draw list, which hands it a profiler scope at the same time.
/// </summary>
/// <remarks>
/// The scope and the list arrive together so that neither can be had without the other: a surface cannot paint
/// without a list, and cannot obtain a list without being measured. The scope's name is derived from where it was
/// opened rather than from a literal, so there is nothing to mistype.<br/>
/// <see cref="Begin"/> names the scope for the calling type, aggregating a surface's whole frame into one row.
/// <see cref="BeginMethod"/> names it for the calling method, which is the shape the shape helpers want: a reader
/// wants to know what all the glows cost, not what the eleventh one did.
/// </remarks>
/// <example>
/// <code>
/// using var draw = UiDraw.Begin();
/// draw.List.AddRectFilled(min, max, color);
/// </code>
/// </example>
internal static class UiDraw
{
    /// <summary>
    /// The scope name for each calling file, so a name is derived once rather than on every draw.
    /// </summary>
    /// <remarks>
    /// Keyed on the path the compiler baked in, which is the same string instance at a given call site every time.
    /// <br/>
    /// Holds the resolved <see cref="UiScopeName"/> rather than the string, so a draw hands the profiler a handle it
    /// can key on directly. Resolving one costs a string hash, and doing that per draw is what this cache exists to
    /// avoid.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, UiScopeName> typeNames = new();

    /// <summary>
    /// The composed <c>Type.Member</c> name for each calling method, by file and then by member.
    /// </summary>
    /// <remarks>
    /// Cached for the same reason as <see cref="typeNames"/>: composing it per draw would put a string per surface per
    /// frame on the draw thread.<br/>
    /// Nested rather than keyed on a <c>(path, member)</c> pair, which is not free: a concurrent dictionary boxes a
    /// value-type key, so the pair cost 88 bytes on every gated draw and the gate has to cost nothing.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, UiScopeName>> methodNames = new();

    /// <summary>
    /// Stands in for the plugin check when deciding whether ImGui may be called, for the headless harness.
    /// </summary>
    /// <remarks>
    /// The guard the library ships asks whether a plugin is behind it, which is the right question in a plugin and the
    /// wrong one in a test: the harness owns a real ImGui context with no plugin anywhere, so every gated surface would
    /// hand back a null list and draw nothing. The surfaces that follow a redirect can be tested through
    /// <see cref="NoireShapes.On(ImDrawListPtr, System.Action)"/>, but the ones that deliberately do not, the window
    /// channel plumbing and the two viewport lists, have no such way in.<br/>
    /// Null everywhere except in the harness, so a plugin takes the plugin check exactly as before. Matches
    /// <see cref="NoireUI.FrameOverride"/> and <see cref="NoireUI.ScaleOverride"/>, which exist for the same reason.
    /// </remarks>
    internal static Func<bool>? AvailableOverride { get; set; }

    /// <summary>
    /// Whether ImGui may be called at all.
    /// </summary>
    internal static bool Available => AvailableOverride?.Invoke() ?? NoireService.IsInitialized();

    /// <summary>
    /// Opens a measurement named for the calling type, and hands back the list it paints into.
    /// </summary>
    /// <remarks>
    /// Every call from the same type lands in one row. For a surface whose cost is worth reading as a whole rather
    /// than broken down by which of its methods spent it.
    /// </remarks>
    /// <param name="path">Supplied by the compiler. Do not pass this.</param>
    /// <returns>The open scope. Dispose it to close the measurement.</returns>
    internal static UiDrawScope Begin([CallerFilePath] string path = "")
        => new(TypeName(path), UiDrawTarget.Window);

    /// <summary>
    /// Opens a measurement named <c>Type.Member</c> for the calling method, and hands back the list it paints into.
    /// </summary>
    /// <remarks>
    /// One row per method, aggregating every call to it in the frame. This is the granularity the shape helpers
    /// report at, and it is what keeps <c>NoireShapes.Sunburst</c> a row of its own rather than a share of
    /// <c>NoireShapes</c>.
    /// </remarks>
    /// <param name="member">Supplied by the compiler. Do not pass this.</param>
    /// <param name="path">Supplied by the compiler. Do not pass this.</param>
    /// <returns>The open scope. Dispose it to close the measurement.</returns>
    internal static UiDrawScope BeginMethod([CallerMemberName] string member = "", [CallerFilePath] string path = "")
        => new(MethodName(path, member), UiDrawTarget.Window);

    /// <summary>
    /// Opens a measurement named for the calling type, and hands back the current window's own list, ignoring any
    /// redirect in force.
    /// </summary>
    /// <remarks>
    /// For window plumbing rather than for shapes: splitting a draw list into channels, setting the current one, and
    /// merging them back have to happen on the list the window's own items land on, which is not the list a
    /// <see cref="NoireShapes.On(ImDrawListPtr, System.Action)"/> redirect points at. Anything that paints a shape
    /// wants <see cref="Begin"/> instead, so that it follows the redirect the way every other shape does.
    /// </remarks>
    /// <inheritdoc cref="Begin"/>
    internal static UiDrawScope BeginWindow([CallerFilePath] string path = "")
        => new(TypeName(path), UiDrawTarget.OwnWindow);

    /// <summary>
    /// Opens a measurement named for the calling type, and hands back the list drawn on top of every window.
    /// </summary>
    /// <inheritdoc cref="Begin"/>
    internal static UiDrawScope BeginForeground([CallerFilePath] string path = "")
        => new(TypeName(path), UiDrawTarget.Foreground);

    /// <summary>
    /// Opens a measurement named for the calling type, and hands back the list drawn behind every window.
    /// </summary>
    /// <inheritdoc cref="Begin"/>
    internal static UiDrawScope BeginBackground([CallerFilePath] string path = "")
        => new(TypeName(path), UiDrawTarget.Background);

    /// <summary>
    /// The type name for a calling file, or none while the profiler is off.
    /// </summary>
    /// <remarks>
    /// Skipped entirely when nothing is being measured, so a disabled profiler costs a boolean read here rather than a
    /// dictionary lookup.<br/>
    /// Taken from the file name up to its first dot, which is the type a partial class is split from: the parts of
    /// <c>NoireShapes</c> live in <c>NoireShapes.Arcs.cs</c> and <c>NoireShapes.Rects.cs</c> and all report as
    /// <c>NoireShapes</c>. Naming them for the whole file name instead would split one surface across as many rows as
    /// it has files, and would rename every scope that already exists.
    /// </remarks>
    private static UiScopeName? TypeName(string path)
        => NoireUI.Profiler.Enabled ? NotAlreadyOpen(Resolve(path)) : null;

    /// <summary>
    /// The <c>Type.Member</c> name for a calling method, or none while the profiler is off.
    /// </summary>
    private static UiScopeName? MethodName(string path, string member)
    {
        if (!NoireUI.Profiler.Enabled)
            return null;

        var inFile = methodNames.GetOrAdd(path, static _ => new ConcurrentDictionary<string, UiScopeName>());

        return NotAlreadyOpen(inFile.GetOrAdd(
            member,
            static (name, file) => UiScopeName.For($"{Resolve(file).Name}.{name}"),
            path));
    }

    /// <summary>
    /// A name, or none when a scope of that name is already open around this call.
    /// </summary>
    /// <remarks>
    /// A surface reached through its own public entry point holds a scope by the time its private painting helpers
    /// run, and those helpers hold the gate too because that is the only way to obtain a list. Opening the name again
    /// would not add to the row that is already open: the profiler keys a node on its name and its parent, so the
    /// second one becomes a child row of the same name, and the outer row's self time drains into it. Every figure
    /// recorded against the single row before then stops being comparable.<br/>
    /// No name is what <see cref="UiProfiler.Open"/> already treats as nothing to measure, so the inner call costs
    /// nothing and its work is charged to the scope that is genuinely around it.<br/>
    /// Compared by reference: names are interned, so two handles are the same handle exactly when the names match.
    /// </remarks>
    private static UiScopeName? NotAlreadyOpen(UiScopeName name)
        => ReferenceEquals(UiProfiler.InnermostScope, name) ? null : name;

    /// <summary>
    /// The cached scope name for a file path.
    /// </summary>
    private static UiScopeName Resolve(string path)
        => typeNames.GetOrAdd(path, static key =>
        {
            var file = Path.GetFileNameWithoutExtension(key);
            var dot = file.IndexOf('.');

            return UiScopeName.For(dot < 0 ? file : file[..dot]);
        });
}

/// <summary>
/// An open measurement and the list it measures the drawing into, closed when it is disposed.
/// </summary>
/// <remarks>
/// A <see langword="ref struct"/> so that obtaining a draw list allocates nothing, which matters for something every
/// surface in the library will hold once per draw.
/// </remarks>
internal ref struct UiDrawScope
{
    private UiProfileScope profile;

    private readonly UiDrawTarget target;

    /// <summary>
    /// The list to paint into. Null when there is no plugin behind the library, in which case ImGui is not called at
    /// all and the drawing is silently skipped.
    /// </summary>
    /// <remarks>
    /// Resolved on each read rather than captured when the scope opened, which is what lets a surface that measures
    /// without painting hold a scope safely: reaching for the current window's list outside a window is not a
    /// question ImGui answers. Read it into a local when a method paints more than once.<br/>
    /// The window list is resolved the way <see cref="NoireShapes.DrawList"/> resolves it, so a gated surface drawn
    /// inside <see cref="NoireShapes.On(ImDrawListPtr, System.Action)"/> paints into the redirected list rather than
    /// the window's. The viewport lists are asked for directly, since that call answers for the whole viewport rather
    /// than for the current window; both carry the same uninitialized guard, and both must.
    /// </remarks>
    public readonly ImDrawListPtr List => target switch
    {
        UiDrawTarget.OwnWindow => UiDraw.Available ? ImGui.GetWindowDrawList() : ImDrawListPtr.Null,
        UiDrawTarget.Foreground => UiDraw.Available ? ImGui.GetForegroundDrawList() : ImDrawListPtr.Null,
        UiDrawTarget.Background => UiDraw.Available ? ImGui.GetBackgroundDrawList() : ImDrawListPtr.Null,
        _ => NoireShapes.DrawList,
    };

    internal UiDrawScope(UiScopeName? name, UiDrawTarget target)
    {
        profile = NoireUI.Profiler.Measure(name);
        this.target = target;
    }

    /// <summary>
    /// Closes the measurement. Safe to call more than once.
    /// </summary>
    public void Dispose() => profile.Dispose();
}
