using System;

namespace NoireLib.Helpers;

/// <summary>
/// Helper class with common utility methods.
/// </summary>
public static class CommonHelper
{
    /// <summary>
    /// Executes the provided action safely, catching and logging any exceptions that occur.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    public static void ExecuteSafely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "An error occurred while executing a safe action.", "[CommonHelper] ");
        }
    }
}
