using System;

namespace NoireLib.UI;

// Runs consumer draw hooks, reporting anything they throw. The argument is passed explicitly rather than captured,
// since a lambda capturing a parameter allocates a display class at method entry even when its branch never runs.
internal static class UiHook
{
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
