using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility;
using NoireLib.Internal.Payloads;
using System;
using System.Numerics;
using NoireLib.Core.Modules;
using NoireLib.Helpers;
using NoireLib.Models;

namespace NoireLib;

/// <summary>
/// A helper class for logging messages and printing to in-game chat.
/// </summary>
public static class NoireLogger
{
    #region LogInfo

    /// <summary>
    /// Writes an info log message including the caller and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogInfo<T>(string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Info, GetLogStringWithCaller<T>(message, prefix));

    /// <summary>
    /// Writes an info log message including the caller instance and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogInfo<T>(T instance, string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Info, GetLogStringWithCaller(instance, message, prefix));

    /// <summary>
    /// Writes an info log message including an optional prefix.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogInfo(string message, string? prefix = null)
        => WriteLog(LogLevel.Info, GetLogString(message, prefix));

    #endregion

    #region LogError

    /// <summary>
    /// Writes an error log message including the caller and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogError<T>(string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Error, GetLogStringWithCaller<T>(message, prefix));

    /// <summary>
    /// Writes an error log message including the caller instance and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogError<T>(T instance, string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Error, GetLogStringWithCaller(instance, message, prefix));

    /// <summary>
    /// Writes an error log message including an optional prefix.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogError(string message, string? prefix = null)
        => WriteLog(LogLevel.Error, GetLogString(message, prefix));

    /// <summary>
    /// Writes an error log message including the caller, an exception, and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="ex">The exception.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogError<T>(Exception ex, string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Error, GetLogStringWithCaller<T>(message, prefix), ex);

    /// <summary>
    /// Writes an error log message including the caller instance, an exception, and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="ex">The exception.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogError<T>(T instance, Exception ex, string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Error, GetLogStringWithCaller(instance, message, prefix), ex);

    /// <summary>
    /// Writes an error log message including an exception and an optional prefix.
    /// </summary>
    /// <param name="ex">The exception.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogError(Exception ex, string message, string? prefix = null)
        => WriteLog(LogLevel.Error, GetLogString(message, prefix), ex);

    #endregion

    #region LogFatal

    /// <summary>
    /// Writes a fatal log message including the caller and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogFatal<T>(string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Fatal, GetLogStringWithCaller<T>(message, prefix));

    /// <summary>
    /// Writes a fatal log message including the caller instance and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogFatal<T>(T instance, string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Fatal, GetLogStringWithCaller(instance, message, prefix));

    /// <summary>
    /// Writes a fatal log message including an optional prefix.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogFatal(string message, string? prefix = null)
        => WriteLog(LogLevel.Fatal, GetLogString(message, prefix));

    /// <summary>
    /// Writes a fatal log message including the caller, an exception, and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="ex">The exception.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogFatal<T>(Exception ex, string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Fatal, GetLogStringWithCaller<T>(message, prefix), ex);

    /// <summary>
    /// Writes a fatal log message including the caller instance, an exception, and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="ex">The exception.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogFatal<T>(T instance, Exception ex, string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Fatal, GetLogStringWithCaller(instance, message, prefix), ex);

    /// <summary>
    /// Writes a fatal log message including an exception and an optional prefix.
    /// </summary>
    /// <param name="ex">The exception.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogFatal(Exception ex, string message, string? prefix = null)
        => WriteLog(LogLevel.Fatal, GetLogString(message, prefix), ex);

    #endregion

    #region LogWarning

    /// <summary>
    /// Writes a warning log message including the caller and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogWarning<T>(string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Warning, GetLogStringWithCaller<T>(message, prefix));

    /// <summary>
    /// Writes a warning log message including the caller instance and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogWarning<T>(T instance, string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Warning, GetLogStringWithCaller(instance, message, prefix));

    /// <summary>
    /// Writes a warning log message including an optional prefix.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogWarning(string message, string? prefix = null)
        => WriteLog(LogLevel.Warning, GetLogString(message, prefix));

    #endregion

    #region LogDebug

    /// <summary>
    /// Writes a debug log message including the caller and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogDebug<T>(string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Debug, GetLogStringWithCaller<T>(message, prefix));

    /// <summary>
    /// Writes a debug log message including the caller instance and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogDebug<T>(T instance, string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Debug, GetLogStringWithCaller(instance, message, prefix));

    /// <summary>
    /// Writes a debug log message including an optional prefix.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogDebug(string message, string? prefix = null)
        => WriteLog(LogLevel.Debug, GetLogString(message, prefix));

    #endregion

    #region LogVerbose

    /// <summary>
    /// Writes a verbose log message including the caller and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogVerbose<T>(string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Verbose, GetLogStringWithCaller<T>(message, prefix));

    /// <summary>
    /// Writes a verbose log message including the caller instance and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogVerbose<T>(T instance, string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Verbose, GetLogStringWithCaller(instance, message, prefix));

    /// <summary>
    /// Writes a verbose log message including an optional prefix.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogVerbose(string message, string? prefix = null)
        => WriteLog(LogLevel.Verbose, GetLogString(message, prefix));

    #endregion

    #region PrintToChat

    /// <summary>
    /// Prints a message to the in-game chat with optional RGB color formatting.
    /// </summary>
    /// <param name="chatType">The type of chat message.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <param name="sender">The sender of the message (optional).</param>
    /// <param name="foregroundColor">The foreground RGB color of the message (Vector3 with values 0-1, optional).</param>
    /// <param name="glowColor">The glow RGB color of the message (Vector3 with values 0-1, optional).</param>
    private static void PrintToChatInternal(XivChatType chatType, string message, string? prefix = null, PlayerModel? sender = null, Vector3? foregroundColor = null, Vector3? glowColor = null)
    {
        var entry = new XivChatEntry
        {
            Type = chatType,
        };

        if (sender != null)
        {
            var builderName = new SeStringBuilder();

            if (foregroundColor.HasValue)
                builderName.Add(new ColorPayload(foregroundColor.Value).AsRaw());

            if (glowColor.HasValue)
                builderName.Add(new GlowPayload(glowColor.Value).AsRaw());

            // DO NOT add a player payload if the sender is the local player
            // Actually, never add a player payload since it could cause issues if sender has "incorrect" data, such as a made up name and world ID
            builderName.AddText(sender.FullName);

            if (glowColor.HasValue)
                builderName.Add(new GlowEndPayload().AsRaw());

            if (foregroundColor.HasValue)
                builderName.Add(new ColorEndPayload().AsRaw());

            entry.Name = builderName.Build();
        }

        var builderText = new SeStringBuilder();
        
        prefix = GetPrefix(prefix);
        var fullMessage = $"{prefix}{message}";

        if (foregroundColor.HasValue)
            builderText.Add(new ColorPayload(foregroundColor.Value).AsRaw());

        if (glowColor.HasValue)
            builderText.Add(new GlowPayload(glowColor.Value).AsRaw());

        builderText.AddText(fullMessage);

        if (glowColor.HasValue)
            builderText.Add(new GlowEndPayload().AsRaw());

        if (foregroundColor.HasValue)
            builderText.Add(new ColorEndPayload().AsRaw());

        entry.Message = builderText.Build();

        NoireService.ChatGui.Print(entry);
    }

    /// <inheritdoc cref="PrintToChatInternal"/>
    [Obsolete("Will get replaced later, use PrintToChat() instead.")]
    public static void PrintToChatAdvanced(XivChatType chatType, string message, string? prefix = null, PlayerModel? sender = null, Vector3? foregroundColor = null, Vector3? glowColor = null)
    {
        PrintToChatInternal(chatType, message, prefix, sender, foregroundColor, glowColor);
    }

    /// <summary>
    /// Prints a message to the in-game chat as an echo message.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void PrintToChat(string message, string? prefix = null)
    {
        PrintToChatInternal(XivChatType.Echo, message, prefix);
    }

    /// <summary>
    /// Prints a message to the in-game chat with specified chat type.
    /// </summary>
    /// <param name="chatType">The type of chat message.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void PrintToChat(XivChatType chatType, string message, string? prefix = null)
    {
        PrintToChatInternal(chatType, message, prefix);
    }

    /// <summary>
    /// Prints a message to the in-game chat with specified chat type and RGB color formatting as Vector3 values.<br/>
    /// For using Vector4 colors, use <see cref="PrintToChat(XivChatType, string, Vector4, Vector4?, string?)"/>.
    /// </summary>
    /// <seealso cref="PrintToChat(XivChatType, string, Vector4, Vector4, string?)"/>
    /// <param name="chatType">The type of chat message.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="foregroundColor">The foreground RGB color of the message.</param>
    /// <param name="glowColor">The glow RGB color of the message.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void PrintToChat(XivChatType chatType, string message, Vector3 foregroundColor, Vector3? glowColor = null, string? prefix = null)
    {
        PrintToChatInternal(chatType, message, prefix, null, foregroundColor, glowColor);
    }

    /// <summary>
    /// Prints a message to the in-game chat with specified chat type and RGB color formatting as Vector4 values.<br/>
    /// This method simply drops the Alpha channel from the Vector4 colors.<br/>
    /// The result will be the same as with <see cref="PrintToChat(XivChatType, string, Vector3, Vector3?, string?)"/>.
    /// </summary>
    /// <param name="chatType">The type of chat message.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="foregroundColor">The foreground RGBA color of the message.</param>
    /// <param name="glowColor"></param>
    /// <param name="prefix"></param>
    public static void PrintToChat(XivChatType chatType, string message, Vector4 foregroundColor, Vector4? glowColor = null, string? prefix = null)
    {
        Vector3 foregroundColorVector3 = ColorHelper.Vector4ToVector3(foregroundColor);
        Vector3? glowColorVector3 = glowColor.HasValue ? ColorHelper.Vector4ToVector3(glowColor.Value) : null;
        PrintToChatInternal(chatType, message, prefix, null, foregroundColorVector3, glowColorVector3);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Enum representing the different log levels.
    /// </summary>
    private enum LogLevel
    {
        Info,
        Error,
        Fatal,
        Warning,
        Debug,
        Verbose
    }

    /// <summary>
    /// Central method to write logs based on the log level.
    /// </summary>
    /// <param name="level">The log level.</param>
    /// <param name="message">The formatted message to log.</param>
    /// <param name="exception">Optional exception to include in the log.</param>
    /// <param name="catchException">Whether to catch exceptions (for unit tests).</param>
    private static void WriteLog(LogLevel level, string message, Exception? exception = null)
    {
        try
        {
            WriteLogInternal(level, message, exception);
        }
        catch (Exception)
        {
            // Catching for unit tests, since PluginLog is not initialized in a test environment
        }
    }

    /// <summary>
    /// Internal method to perform the actual logging.
    /// </summary>
    private static void WriteLogInternal(LogLevel level, string message, Exception? exception)
    {
        if (exception != null)
        {
            switch (level)
            {
                case LogLevel.Error:
                    NoireService.PluginLog.Error(exception, message);
                    break;
                case LogLevel.Fatal:
                    NoireService.PluginLog.Fatal(exception, message);
                    break;
                default:
                    // Other log levels don't support exceptions in the same way
                    break;
            }
        }
        else
        {
            switch (level)
            {
                case LogLevel.Info:
                    NoireService.PluginLog.Info(message);
                    break;
                case LogLevel.Error:
                    NoireService.PluginLog.Error(message);
                    break;
                case LogLevel.Fatal:
                    NoireService.PluginLog.Fatal(message);
                    break;
                case LogLevel.Warning:
                    NoireService.PluginLog.Warning(message);
                    break;
                case LogLevel.Debug:
                    NoireService.PluginLog.Debug(message);
                    break;
                case LogLevel.Verbose:
                    NoireService.PluginLog.Verbose(message);
                    break;
            }
        }
    }

    /// <summary>
    /// Returns the prefix to use in log messages.
    /// </summary>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <returns>The formatted prefix to use in log messages.</returns>
    private static string GetPrefix(string? prefix = null)
    {
        if (prefix.IsNullOrWhitespace())
            return string.Empty;

        return prefix;
    }

    /// <summary>
    /// Gets a log string with caller type name.
    /// </summary>
    /// <returns>The formatted log string.</returns>
    private static string GetLogStringWithCaller<T>(string message, string? prefix = null) where T : class
    {
        prefix = GetPrefix(prefix);
        var caller = typeof(T).Name;
        return $"{prefix}[{caller}] {message}";
    }

    /// <summary>
    /// Gets a log string with caller instance, including module ID if applicable.
    /// </summary>
    /// <returns>The formatted log string.</returns>
    private static string GetLogStringWithCaller<T>(T instance, string message, string? prefix = null) where T : class
    {
        prefix = GetPrefix(prefix);
        var caller = typeof(T).Name;

        if (instance is INoireModule module)
        {
            var moduleId = module.ModuleId;
            if (!moduleId.IsNullOrWhitespace())
                return $"{prefix}[{caller}:{moduleId}] {message}";
        }

        return $"{prefix}[{caller}] {message}";
    }

    /// <summary>
    /// Gets a log string without caller information.
    /// </summary>
    /// <returns>The formatted log string.</returns>
    private static string GetLogString(string message, string? prefix = null)
    {
        prefix = GetPrefix(prefix);
        return $"{prefix}{message}";
    }

    #endregion
}
