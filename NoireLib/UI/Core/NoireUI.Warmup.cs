using System;
using System.Threading.Tasks;

namespace NoireLib.UI;

public static partial class NoireUI
{
    /// <summary>
    /// Compiles the drawing code ahead of the frame that would otherwise compile it. Off by default.<br/>
    /// Every NoireUI drawing surface is included. Pass the window's own types to cover the rest.
    /// </summary>
    /// <example>
    /// <code>
    /// NoireText.Prewarm(wait: true);
    /// NoireUI.WarmDrawPath(typeof(MyBigWindow));
    /// </code>
    /// </example>
    /// <param name="alsoWarm">Consumer types to compile as well as NoireUI's own.</param>
    /// <returns>The warmup task.</returns>
    /// <seealso cref="NoireText.Prewarm"/>
    public static Task WarmDrawPath(params Type[]? alsoWarm)
        => UiCodeWarmup.Start(alsoWarm);

    /// <summary>
    /// Whether <see cref="WarmDrawPath"/> has run to completion. For diagnostics only.
    /// </summary>
    public static bool DrawPathWarmed => UiCodeWarmup.Finished;
}
