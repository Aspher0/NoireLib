using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;

namespace NoireLib.UI;

internal enum UiDrawTarget
{
    // Window follows a redirect in force; OwnWindow never does.
    Window,

    OwnWindow,

    Foreground,

    Background,
}

// The one way a surface inside NoireUI obtains a draw list, which hands it a profiler scope at the same time.
internal static class UiDraw
{
    // The scope name for each calling file, derived once rather than on every draw.
    private static readonly ConcurrentDictionary<string, UiScopeName> typeNames = new(StringInstanceComparer.Instance);

    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, UiScopeName>> methodNames = new(StringInstanceComparer.Instance);

    // Stands in for the plugin check when deciding whether ImGui may be called. Null everywhere except in the harness.
    internal static Func<bool>? AvailableOverride { get; set; }

    internal static bool Available => AvailableOverride?.Invoke() ?? NoireService.IsInitialized();

    internal static UiDrawScope Begin([CallerFilePath] string path = "")
        => new(TypeName(path), UiDrawTarget.Window);

    // Measured only while UiProfiler.Detailed is on; otherwise the time folds into the enclosing scope.
    internal static UiDrawScope BeginMethod([CallerMemberName] string member = "", [CallerFilePath] string path = "")
        => new(MethodName(path, member), UiDrawTarget.Window);

    // Window plumbing only. Anything painting a shape uses Begin, which follows the redirect.
    internal static UiDrawScope BeginWindow([CallerFilePath] string path = "")
        => new(TypeName(path), UiDrawTarget.OwnWindow);

    internal static UiDrawScope BeginForeground([CallerFilePath] string path = "")
        => new(TypeName(path), UiDrawTarget.Foreground);

    internal static UiDrawScope BeginBackground([CallerFilePath] string path = "")
        => new(TypeName(path), UiDrawTarget.Background);

    // Up to the first dot: every part of a partial class reports as one type.
    private static UiScopeName? TypeName(string path)
        => NoireUI.Profiler.Enabled ? Resolve(path) : null;

    private static UiScopeName? MethodName(string path, string member)
    {
        if (!NoireUI.Profiler.MeasuringMethods)
            return null;

        var inFile = methodNames.GetOrAdd(path, static _ => new ConcurrentDictionary<string, UiScopeName>(StringInstanceComparer.Instance));

        return inFile.GetOrAdd(
            member,
            static (name, file) => UiScopeName.For($"{Resolve(file).Name}.{name}"),
            path);
    }

    private static UiScopeName Resolve(string path)
        => typeNames.GetOrAdd(path, static key =>
        {
            var file = Path.GetFileNameWithoutExtension(key);
            var dot = file.IndexOf('.');

            return UiScopeName.For(dot < 0 ? file : file[..dot]);
        });
}

internal ref struct UiDrawScope
{
    private readonly UiProfiler profiler;
    private readonly UiScopeName? name;
    private long started;

    private readonly UiDrawTarget target;

    // Null when there is no plugin behind the library, in which case the drawing is silently skipped.
    // Resolved on each read: read it into a local when a method paints more than once.
    public readonly ImDrawListPtr List => target switch
    {
        UiDrawTarget.OwnWindow => UiDraw.Available ? UiContext.WindowDrawList : ImDrawListPtr.Null,
        UiDrawTarget.Foreground => UiDraw.Available ? ImGui.GetForegroundDrawList() : ImDrawListPtr.Null,
        UiDrawTarget.Background => UiDraw.Available ? ImGui.GetBackgroundDrawList() : ImDrawListPtr.Null,
        _ => NoireShapes.DrawList,
    };

    internal UiDrawScope(UiScopeName? name, UiDrawTarget target)
    {
        profiler = NoireUI.Profiler;
        this.name = name;
        this.target = target;
        started = profiler.Open(name);
    }

    // Safe to call more than once.
    public void Dispose()
    {
        if (started == 0L)
            return;

        profiler.Close(name, started);
        started = 0L;
    }
}

