using System;

namespace NoireLib.Helpers;

/// <summary>
/// What one operation on Dalamud's plugin installation did. Nothing is swallowed: an operation that could not run
/// says so in <see cref="Message"/>. It never fails quietly or throws.
/// </summary>
/// <param name="Succeeded">Whether the operation did what it was asked.</param>
/// <param name="Subject">What it acted on, being a plugin's internal name or a repository's URL.</param>
/// <param name="Message">What happened, in one line.</param>
/// <param name="Error">The exception when one was thrown inside Dalamud, null otherwise.</param>
public readonly record struct PluginOperationResult(
    bool Succeeded,
    string Subject,
    string Message,
    Exception? Error = null)
{
    /// <summary>Builds a result for an operation that did what it was asked.</summary>
    /// <param name="subject">What it acted on.</param>
    /// <param name="message">What happened.</param>
    /// <returns>The result.</returns>
    public static PluginOperationResult Ok(string subject, string message) => new(true, subject, message);

    /// <summary>Builds a result for an operation that did not run or did not finish.</summary>
    /// <param name="subject">What it was asked to act on.</param>
    /// <param name="message">Why it did not.</param>
    /// <param name="error">The exception, when there was one.</param>
    /// <returns>The result.</returns>
    public static PluginOperationResult Failed(string subject, string message, Exception? error = null)
        => new(false, subject, message, error);
}
