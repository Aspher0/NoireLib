using System;
using System.Runtime.CompilerServices;

namespace NoireLib.Core.Modules;

/// <summary>
/// Module-facing logging helpers.<br/>
/// Forward to <see cref="NoireLogger"/> with the module as the caller, gated by <see cref="EnableLogging"/> so
/// call sites do not repeat the check themselves.<br/>
/// The informational levels (<see cref="LogInfo(ref NoireLogHandler)"/>, <see cref="LogDebug(ref NoireLogHandler)"/>,
/// <see cref="LogVerbose(ref NoireLogHandler)"/>) take a <see cref="NoireLogHandler"/> so an interpolated message
/// is never built while logging is off. Warnings, errors and fatal messages always report and take a plain string.
/// </summary>
/// <typeparam name="TModule">The type of the module.</typeparam>
public abstract partial class NoireModuleBase<TModule>
{
    /// <summary>
    /// Writes an interpolated info message when <see cref="EnableLogging"/> is set. The message is not built while
    /// logging is off.
    /// </summary>
    /// <param name="message">The interpolated message.</param>
    protected void LogInfo([InterpolatedStringHandlerArgument("")] ref NoireLogHandler message)
    {
        if (message.IsEnabled)
            NoireLogger.LogInfo((TModule)this, message.ToStringAndClear());
    }

    /// <summary>
    /// Writes an info message when <see cref="EnableLogging"/> is set.
    /// </summary>
    /// <param name="message">The message.</param>
    protected void LogInfo(string message)
    {
        if (EnableLogging)
            NoireLogger.LogInfo((TModule)this, message);
    }

    /// <inheritdoc cref="LogInfo(ref NoireLogHandler)"/>
    protected void LogInformation([InterpolatedStringHandlerArgument("")] ref NoireLogHandler message)
    {
        if (message.IsEnabled)
            NoireLogger.LogInfo((TModule)this, message.ToStringAndClear());
    }

    /// <inheritdoc cref="LogInfo(string)"/>
    protected void LogInformation(string message)
    {
        if (EnableLogging)
            NoireLogger.LogInfo((TModule)this, message);
    }

    /// <summary>
    /// Writes an interpolated debug message when <see cref="EnableLogging"/> is set. The message is not built while
    /// logging is off.
    /// </summary>
    /// <param name="message">The interpolated message.</param>
    protected void LogDebug([InterpolatedStringHandlerArgument("")] ref NoireLogHandler message)
    {
        if (message.IsEnabled)
            NoireLogger.LogDebug((TModule)this, message.ToStringAndClear());
    }

    /// <summary>
    /// Writes a debug message when <see cref="EnableLogging"/> is set.
    /// </summary>
    /// <param name="message">The message.</param>
    protected void LogDebug(string message)
    {
        if (EnableLogging)
            NoireLogger.LogDebug((TModule)this, message);
    }

    /// <summary>
    /// Writes an interpolated verbose message when <see cref="EnableLogging"/> is set. The message is not built while
    /// logging is off.
    /// </summary>
    /// <param name="message">The interpolated message.</param>
    protected void LogVerbose([InterpolatedStringHandlerArgument("")] ref NoireLogHandler message)
    {
        if (message.IsEnabled)
            NoireLogger.LogVerbose((TModule)this, message.ToStringAndClear());
    }

    /// <summary>
    /// Writes a verbose message when <see cref="EnableLogging"/> is set.
    /// </summary>
    /// <param name="message">The message.</param>
    protected void LogVerbose(string message)
    {
        if (EnableLogging)
            NoireLogger.LogVerbose((TModule)this, message);
    }

    /// <summary>
    /// Writes a warning message. Reported regardless of <see cref="EnableLogging"/>.
    /// </summary>
    /// <param name="message">The message.</param>
    protected void LogWarning(string message)
        => NoireLogger.LogWarning((TModule)this, message);

    /// <summary>
    /// Writes an error message. Reported regardless of <see cref="EnableLogging"/>.
    /// </summary>
    /// <param name="message">The message.</param>
    protected void LogError(string message)
        => NoireLogger.LogError((TModule)this, message);

    /// <summary>
    /// Writes an error message with an exception. Reported regardless of <see cref="EnableLogging"/>.
    /// </summary>
    /// <param name="ex">The exception.</param>
    /// <param name="message">The message.</param>
    protected void LogError(Exception? ex, string message)
        => NoireLogger.LogError((TModule)this, ex, message);

    /// <summary>
    /// Writes a fatal message. Reported regardless of <see cref="EnableLogging"/>.
    /// </summary>
    /// <param name="message">The message.</param>
    protected void LogFatal(string message)
        => NoireLogger.LogFatal((TModule)this, message);

    /// <summary>
    /// Writes a fatal message with an exception. Reported regardless of <see cref="EnableLogging"/>.
    /// </summary>
    /// <param name="ex">The exception.</param>
    /// <param name="message">The message.</param>
    protected void LogFatal(Exception ex, string message)
        => NoireLogger.LogFatal((TModule)this, ex, message);
}
