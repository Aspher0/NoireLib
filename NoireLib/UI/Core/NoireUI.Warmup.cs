using System;
using System.Reflection;
using System.Threading.Tasks;

namespace NoireLib.UI;

public static partial class NoireUI
{
    /// <summary>
    /// Compiles the drawing code ahead of the frame that would otherwise compile it, off by default.
    /// </summary>
    /// <param name="alsoWarm">Consumer types to compile as well as NoireUI's own.</param>
    /// <returns>The warmup task.</returns>
    /// <seealso cref="NoireText.Prewarm"/>
    public static Task WarmDrawPath(params Type[]? alsoWarm)
        => UiCodeWarmup.Start(alsoWarm);

    public static Task WarmDrawPath(Assembly assembly, string? namespacePrefix = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return Task.Run(() => UiCodeWarmup.Start(Array.FindAll(assembly.GetTypes(),
            type => namespacePrefix == null || type.Namespace?.StartsWith(namespacePrefix, StringComparison.Ordinal) == true)));
    }

    /// <summary>
    /// Whether <see cref="WarmDrawPath(Type[])"/> has run to completion.
    /// </summary>
    public static bool DrawPathWarmed => UiCodeWarmup.Finished;
}
