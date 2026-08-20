using System;

namespace NoireLib.UI;

/// <summary>
/// Runs consumer-supplied draw hooks and callbacks, reporting anything they throw rather than letting it escape into
/// the frame. The argument is passed explicitly rather than captured, since a lambda capturing a parameter allocates
/// a display class at method entry even on frames where its branch never runs.
/// </summary>
internal static class UiHook
{
    /// <summary>Runs a consumer callback, reporting anything it throws.</summary>
    /// <typeparam name="TArg">The argument type.</typeparam>
    /// <param name="callback">The callback to run.</param>
    /// <param name="argument">The argument passed to <paramref name="callback"/>.</param>
    /// <param name="source">The source name recorded in the fault report.</param>
    /// <param name="fault">The fault message to report.</param>
    internal static void Invoke<TArg>(Action<TArg> callback, TArg argument, string source, string fault)
        where TArg : allows ref struct
    {
        try
        {
            callback(argument);
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(source, fault, ex);
        }
    }
}
