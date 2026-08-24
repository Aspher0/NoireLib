using System;

namespace NoireLib.UI;

// The ImGui style stacks are left exactly as they were found, and an exception on the way out does not change that.
// A body exception is not swallowed: the scope closes and the exception keeps travelling.
internal static class UiScope
{
    public static void Run<TState>(string containerName, TState state, Action<TState> body)
    {
        var snapshot = UiStackSnapshot.Capture();

        try
        {
            body(state);
        }
        finally
        {
            snapshot.Restore(containerName);
        }
    }
}
