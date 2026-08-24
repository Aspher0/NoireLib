using System;

namespace NoireLib.UI;

/// <summary>
/// The one place a container body is invoked, so every container gets the same guarantees: the ImGui style stacks are
/// left exactly as they were found, and an exception on the way out does not change that.<br/>
/// A body exception is not swallowed: the scope closes and the exception keeps travelling.
/// </summary>
internal static class UiScope
{
    /// <summary>
    /// Invokes a container body, restoring the ImGui style stacks afterwards.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="containerName">The container, named in the log if the body leaks a push.</param>
    /// <param name="state">Passed to the body.</param>
    /// <param name="body">The body to invoke.</param>
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
