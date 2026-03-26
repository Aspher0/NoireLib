using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility;
using NoireLib.Core.Modules;
using NoireLib.Internal.Payloads;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

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

    /// <inheritdoc cref="LogInfo{T}(string, string?)"/>
    public static void LogInformation<T>(string message, string? prefix = null) where T : class
        => LogInfo<T>(message, prefix);

    /// <summary>
    /// Writes an info log message including the caller instance and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogInfo<T>(T instance, string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Info, GetLogStringWithCaller(instance, message, prefix));

    /// <inheritdoc cref="LogInfo{T}(T, string, string?)"/>
    public static void LogInformation<T>(T instance, string message, string? prefix = null) where T : class
        => LogInfo<T>(instance, message, prefix);

    /// <summary>
    /// Writes an info log message including an optional prefix.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogInfo(string message, string? prefix = null)
        => WriteLog(LogLevel.Info, GetLogString(message, prefix));

    /// <inheritdoc cref="LogInfo(string, string?)"/>
    public static void LogInformation(string message, string? prefix = null)
        => LogInfo(message, prefix);

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
    public static void LogError<T>(Exception? ex, string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Error, GetLogStringWithCaller<T>(message, prefix), ex);

    /// <summary>
    /// Writes an error log message including the caller instance, an exception, and an optional prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="ex">The exception.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogError<T>(T instance, Exception? ex, string message, string? prefix = null) where T : class
        => WriteLog(LogLevel.Error, GetLogStringWithCaller(instance, message, prefix), ex);

    /// <summary>
    /// Writes an error log message including an exception and an optional prefix.
    /// </summary>
    /// <param name="ex">The exception.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    public static void LogError(Exception? ex, string message, string? prefix = null)
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
    /// A builder for composing chat messages with per-segment colors.
    /// </summary>
    public sealed class ChatMessageBuilder
    {
        private readonly List<ChatMessageSegment> segments = [];

        /// <summary>
        /// Adds plain text to the chat message.
        /// </summary>
        /// <param name="text">The text to add.</param>
        /// <returns>The current chat message builder.</returns>
        public ChatMessageBuilder AddText(string text)
        {
            AddChatMessageSegment(segments, text, default);
            return this;
        }

        /// <summary>
        /// Adds colored text to the chat message using RGB colors.
        /// </summary>
        /// <param name="text">The text to add.</param>
        /// <param name="foregroundColor">The foreground RGB color to apply.</param>
        /// <param name="glowColor">The optional glow RGB color to apply.</param>
        /// <returns>The current chat message builder.</returns>
        public ChatMessageBuilder AddText(string text, Vector3 foregroundColor, Vector3? glowColor = null)
        {
            AddChatMessageSegment(segments, text, new ChatStyle(foregroundColor, glowColor));
            return this;
        }

        /// <summary>
        /// Adds tagged text to the chat message.
        /// </summary>
        /// <param name="taggedText">The tagged text to add.</param>
        /// <returns>The current chat message builder.</returns>
        /// <remarks>
        /// Supported tags are <c>&lt;color=#RRGGBB&gt;</c>, <c>&lt;glow=#RRGGBB&gt;</c>, and
        /// <c>&lt;style color=#RRGGBB glow=#RRGGBB&gt;</c>, with matching closing tags.
        /// </remarks>
        public ChatMessageBuilder AddTaggedText(string taggedText)
        {
            AppendTaggedTextSegments(segments, taggedText);
            return this;
        }

        internal SeString Build(string? leadingText = null)
        {
            var builder = new SeStringBuilder();

            if (!string.IsNullOrEmpty(leadingText))
                builder.AddText(leadingText);

            foreach (var segment in segments)
                AppendStyledText(builder, segment.Text, segment.ForegroundColor, segment.GlowColor);

            return builder.Build();
        }
    }

    /// <summary>
    /// Creates a new chat message builder for composing styled messages.
    /// </summary>
    /// <returns>A new chat message builder.</returns>
    public static ChatMessageBuilder CreateChatMessageBuilder()
        => new();

    /// <summary>
    /// Parses tagged chat text into a chat message builder.
    /// </summary>
    /// <param name="taggedMessage">The tagged message to parse.</param>
    /// <returns>A chat message builder containing the parsed message segments.</returns>
    /// <remarks>
    /// Supported tags are <c>&lt;color=#RRGGBB&gt;</c>, <c>&lt;glow=#RRGGBB&gt;</c>, and
    /// <c>&lt;style color=#RRGGBB glow=#RRGGBB&gt;</c>, with matching closing tags.
    /// </remarks>
    public static ChatMessageBuilder ParseTaggedChatMessage(string taggedMessage)
        => new ChatMessageBuilder().AddTaggedText(taggedMessage);

    /// <summary>
    /// Prints a built chat message to the in-game chat as an echo message.
    /// </summary>
    /// <param name="messageBuilder">The message builder containing the message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <param name="senderName">The optional sender name to display for the chat entry.</param>
    /// <param name="senderForegroundColor">The optional foreground RGB color of the sender name.</param>
    /// <param name="senderGlowColor">The optional glow RGB color of the sender name.</param>
    public static void PrintToChat(ChatMessageBuilder messageBuilder, string? prefix = null, string? senderName = null, Vector3? senderForegroundColor = null, Vector3? senderGlowColor = null)
        => PrintToChat(XivChatType.Echo, messageBuilder, prefix, senderName, senderForegroundColor, senderGlowColor);

    /// <summary>
    /// Prints a built chat message to the in-game chat with the caller instance prefix as an echo message.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="messageBuilder">The message builder containing the message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <param name="senderName">The optional sender name to display for the chat entry.</param>
    /// <param name="senderForegroundColor">The optional foreground RGB color of the sender name.</param>
    /// <param name="senderGlowColor">The optional glow RGB color of the sender name.</param>
    public static void PrintToChat<T>(T instance, ChatMessageBuilder messageBuilder, string? prefix = null, string? senderName = null, Vector3? senderForegroundColor = null, Vector3? senderGlowColor = null) where T : class
        => PrintToChat(instance, XivChatType.Echo, messageBuilder, prefix, senderName, senderForegroundColor, senderGlowColor);

    /// <summary>
    /// Prints a message to the in-game chat as an echo message.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <param name="senderName">The optional sender name to display for the chat entry.</param>
    /// <param name="senderForegroundColor">The optional foreground RGB color of the sender name.</param>
    /// <param name="senderGlowColor">The optional glow RGB color of the sender name.</param>
    public static void PrintToChat(string message, string? prefix = null, string? senderName = null, Vector3? senderForegroundColor = null, Vector3? senderGlowColor = null)
        => PrintToChatInternal(XivChatType.Echo, message, prefix, senderName, null, null, senderForegroundColor, senderGlowColor);

    /// <summary>
    /// Prints a message to the in-game chat as an echo message with the caller instance prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <param name="senderName">The optional sender name to display for the chat entry.</param>
    /// <param name="senderForegroundColor">The optional foreground RGB color of the sender name.</param>
    /// <param name="senderGlowColor">The optional glow RGB color of the sender name.</param>
    public static void PrintToChat<T>(T instance, string message, string? prefix = null, string? senderName = null, Vector3? senderForegroundColor = null, Vector3? senderGlowColor = null) where T : class
        => PrintToChat(instance, XivChatType.Echo, message, prefix, senderName, senderForegroundColor, senderGlowColor);

    /// <summary>
    /// Prints a tagged message to the in-game chat as an echo message.
    /// </summary>
    /// <remarks>
    /// Supported tags are <c>&lt;color=#RRGGBB&gt;</c>, <c>&lt;glow=#RRGGBB&gt;</c>, and
    /// <c>&lt;style color=#RRGGBB glow=#RRGGBB&gt;</c>, with matching closing tags.
    /// </remarks>
    /// <param name="taggedMessage">The tagged message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <param name="senderName">The optional sender name to display for the chat entry.</param>
    /// <param name="senderForegroundColor">The optional foreground RGB color of the sender name.</param>
    /// <param name="senderGlowColor">The optional glow RGB color of the sender name.</param>
    public static void PrintToChatTagged(string taggedMessage, string? prefix = null, string? senderName = null, Vector3? senderForegroundColor = null, Vector3? senderGlowColor = null)
        => PrintToChat(ParseTaggedChatMessage(taggedMessage), prefix, senderName, senderForegroundColor, senderGlowColor);

    /// <summary>
    /// Prints a tagged message to the in-game chat as an echo message with the caller instance prefix.
    /// </summary>
    /// <remarks>
    /// Supported tags are <c>&lt;color=#RRGGBB&gt;</c>, <c>&lt;glow=#RRGGBB&gt;</c>, and
    /// <c>&lt;style color=#RRGGBB glow=#RRGGBB&gt;</c>, with matching closing tags.
    /// </remarks>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="taggedMessage">The tagged message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <param name="senderName">The optional sender name to display for the chat entry.</param>
    /// <param name="senderForegroundColor">The optional foreground RGB color of the sender name.</param>
    /// <param name="senderGlowColor">The optional glow RGB color of the sender name.</param>
    public static void PrintToChatTagged<T>(T instance, string taggedMessage, string? prefix = null, string? senderName = null, Vector3? senderForegroundColor = null, Vector3? senderGlowColor = null) where T : class
        => PrintToChat(instance, ParseTaggedChatMessage(taggedMessage), prefix, senderName, senderForegroundColor, senderGlowColor);

    /// <summary>
    /// Prints a message to the in-game chat with specified chat type.
    /// </summary>
    /// <param name="chatType">The type of chat message.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <param name="senderName">The optional sender name to display for the chat entry.</param>
    /// <param name="senderForegroundColor">The optional foreground RGB color of the sender name.</param>
    /// <param name="senderGlowColor">The optional glow RGB color of the sender name.</param>
    public static void PrintToChat(XivChatType chatType, string message, string? prefix = null, string? senderName = null, Vector3? senderForegroundColor = null, Vector3? senderGlowColor = null)
        => PrintToChatInternal(chatType, message, prefix, senderName, null, null, senderForegroundColor, senderGlowColor);

    /// <summary>
    /// Prints a message to the in-game chat with specified chat type and the caller instance prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="chatType">The type of chat message.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <param name="senderName">The optional sender name to display for the chat entry.</param>
    /// <param name="senderForegroundColor">The optional foreground RGB color of the sender name.</param>
    /// <param name="senderGlowColor">The optional glow RGB color of the sender name.</param>
    public static void PrintToChat<T>(T instance, XivChatType chatType, string message, string? prefix = null, string? senderName = null, Vector3? senderForegroundColor = null, Vector3? senderGlowColor = null) where T : class
        => PrintToChatInternal(chatType, GetLogStringWithCaller(instance, message, prefix), null, senderName, null, null, senderForegroundColor, senderGlowColor);

    /// <summary>
    /// Prints a built chat message to the in-game chat with specified chat type.
    /// </summary>
    /// <param name="chatType">The type of chat message.</param>
    /// <param name="messageBuilder">The message builder containing the message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <param name="senderName">The optional sender name to display for the chat entry.</param>
    /// <param name="senderForegroundColor">The optional foreground RGB color of the sender name.</param>
    /// <param name="senderGlowColor">The optional glow RGB color of the sender name.</param>
    public static void PrintToChat(XivChatType chatType, ChatMessageBuilder messageBuilder, string? prefix = null, string? senderName = null, Vector3? senderForegroundColor = null, Vector3? senderGlowColor = null)
        => PrintToChatInternal(chatType, messageBuilder.Build(GetPrefix(prefix)), senderName, senderForegroundColor, senderGlowColor);

    /// <summary>
    /// Prints a built chat message to the in-game chat with specified chat type and the caller instance prefix.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="chatType">The type of chat message.</param>
    /// <param name="messageBuilder">The message builder containing the message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <param name="senderName">The optional sender name to display for the chat entry.</param>
    /// <param name="senderForegroundColor">The optional foreground RGB color of the sender name.</param>
    /// <param name="senderGlowColor">The optional glow RGB color of the sender name.</param>
    public static void PrintToChat<T>(T instance, XivChatType chatType, ChatMessageBuilder messageBuilder, string? prefix = null, string? senderName = null, Vector3? senderForegroundColor = null, Vector3? senderGlowColor = null) where T : class
        => PrintToChatInternal(chatType, messageBuilder.Build(GetChatLeadingText(instance, prefix)), senderName, senderForegroundColor, senderGlowColor);

    /// <summary>
    /// Prints a tagged message to the in-game chat with specified chat type.
    /// </summary>
    /// <remarks>
    /// Supported tags are <c>&lt;color=#RRGGBB&gt;</c>, <c>&lt;glow=#RRGGBB&gt;</c>, and
    /// <c>&lt;style color=#RRGGBB glow=#RRGGBB&gt;</c>, with matching closing tags.
    /// </remarks>
    /// <param name="chatType">The type of chat message.</param>
    /// <param name="taggedMessage">The tagged message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <param name="senderName">The optional sender name to display for the chat entry.</param>
    /// <param name="senderForegroundColor">The optional foreground RGB color of the sender name.</param>
    /// <param name="senderGlowColor">The optional glow RGB color of the sender name.</param>
    public static void PrintToChatTagged(XivChatType chatType, string taggedMessage, string? prefix = null, string? senderName = null, Vector3? senderForegroundColor = null, Vector3? senderGlowColor = null)
        => PrintToChat(chatType, ParseTaggedChatMessage(taggedMessage), prefix, senderName, senderForegroundColor, senderGlowColor);

    /// <summary>
    /// Prints a tagged message to the in-game chat with specified chat type and the caller instance prefix.
    /// </summary>
    /// <remarks>
    /// Supported tags are <c>&lt;color=#RRGGBB&gt;</c>, <c>&lt;glow=#RRGGBB&gt;</c>, and
    /// <c>&lt;style color=#RRGGBB glow=#RRGGBB&gt;</c>, with matching closing tags.
    /// </remarks>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="chatType">The type of chat message.</param>
    /// <param name="taggedMessage">The tagged message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <param name="senderName">The optional sender name to display for the chat entry.</param>
    /// <param name="senderForegroundColor">The optional foreground RGB color of the sender name.</param>
    /// <param name="senderGlowColor">The optional glow RGB color of the sender name.</param>
    public static void PrintToChatTagged<T>(T instance, XivChatType chatType, string taggedMessage, string? prefix = null, string? senderName = null, Vector3? senderForegroundColor = null, Vector3? senderGlowColor = null) where T : class
        => PrintToChat(instance, chatType, ParseTaggedChatMessage(taggedMessage), prefix, senderName, senderForegroundColor, senderGlowColor);

    /// <summary>
    /// Prints a message to the in-game chat with specified chat type and RGB color formatting as Vector3 values.
    /// </summary>
    /// <param name="chatType">The type of chat message.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="foregroundColor">The foreground RGB color of the message.</param>
    /// <param name="glowColor">The glow RGB color of the message.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <param name="senderName">The optional sender name to display for the chat entry.</param>
    /// <param name="senderForegroundColor">The optional foreground RGB color of the sender name.</param>
    /// <param name="senderGlowColor">The optional glow RGB color of the sender name.</param>
    public static void PrintToChat(XivChatType chatType, string message, Vector3 foregroundColor, Vector3? glowColor = null, string? prefix = null, string? senderName = null, Vector3? senderForegroundColor = null, Vector3? senderGlowColor = null)
        => PrintToChatInternal(chatType, message, prefix, senderName, foregroundColor, glowColor, senderForegroundColor, senderGlowColor);

    /// <summary>
    /// Prints a message to the in-game chat with specified chat type, caller instance prefix, and RGB color formatting.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="chatType">The type of chat message.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="foregroundColor">The foreground RGB color of the message.</param>
    /// <param name="glowColor">The glow RGB color of the message.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <param name="senderName">The optional sender name to display for the chat entry.</param>
    /// <param name="senderForegroundColor">The optional foreground RGB color of the sender name.</param>
    /// <param name="senderGlowColor">The optional glow RGB color of the sender name.</param>
    public static void PrintToChat<T>(T instance, XivChatType chatType, string message, Vector3 foregroundColor, Vector3? glowColor = null, string? prefix = null, string? senderName = null, Vector3? senderForegroundColor = null, Vector3? senderGlowColor = null) where T : class
        => PrintToChatInternal(chatType, GetLogStringWithCaller(instance, message, prefix), null, senderName, foregroundColor, glowColor, senderForegroundColor, senderGlowColor);

    /// <summary>
    /// Prints a message to the in-game chat with optional colors for both the message and sender name.
    /// </summary>
    /// <param name="chatType">The type of chat message.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <param name="senderName">The optional sender name to display for the chat entry.</param>
    /// <param name="messageForegroundColor">The optional foreground RGB color of the message.</param>
    /// <param name="messageGlowColor">The optional glow RGB color of the message.</param>
    /// <param name="senderForegroundColor">The optional foreground RGB color of the sender name.</param>
    /// <param name="senderGlowColor">The optional glow RGB color of the sender name.</param>
    private static void PrintToChatInternal(XivChatType chatType, string message, string? prefix = null, string? senderName = null, Vector3? messageForegroundColor = null, Vector3? messageGlowColor = null, Vector3? senderForegroundColor = null, Vector3? senderGlowColor = null)
        => PrintToChatInternal(chatType, BuildChatMessage(message, prefix, messageForegroundColor, messageGlowColor), senderName, senderForegroundColor, senderGlowColor);

    /// <summary>
    /// Prints a message to the in-game chat.
    /// </summary>
    /// <param name="chatType">The type of chat message.</param>
    /// <param name="message">The message to display.</param>
    /// <param name="senderName">The optional sender name to display for the chat entry.</param>
    /// <param name="senderForegroundColor">The optional foreground RGB color for the sender name.</param>
    /// <param name="senderGlowColor">The optional glow RGB color for the sender name.</param>
    private static void PrintToChatInternal(XivChatType chatType, SeString message, string? senderName = null, Vector3? senderForegroundColor = null, Vector3? senderGlowColor = null)
    {
        if (message.Payloads.Count == 0)
            message = BuildChatMessage(" ");

        var entry = new XivChatEntry
        {
            Type = chatType,
            Message = message,
        };

        if (!senderName.IsNullOrWhitespace())
            entry.Name = BuildSenderName(senderName!, senderForegroundColor, senderGlowColor);

        NoireService.ChatGui.Print(entry);
    }

    #endregion

    #region Helper Methods

    private readonly record struct ChatMessageSegment(string Text, Vector3? ForegroundColor, Vector3? GlowColor);

    private readonly record struct ChatStyle(Vector3? ForegroundColor, Vector3? GlowColor);

    private readonly record struct ChatStyleFrame(string TagName, ChatStyle PreviousStyle);

    private readonly record struct ChatTag(string Name, bool IsClosing, Vector3? ForegroundColor, Vector3? GlowColor);

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
    /// Gets the friendly name of the caller type, stripping any generic type information.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <returns>The friendly name of the caller type.</returns>
    private static string GetCallerFriendlyName<T>() where T : class
        => typeof(T).Name.Split('`')[0];

    /// <summary>
    /// Gets a log string with caller type name.
    /// </summary>
    /// <returns>The formatted log string.</returns>
    private static string GetLogStringWithCaller<T>(string message, string? prefix = null) where T : class
    {
        prefix = GetPrefix(prefix);
        var caller = GetCallerFriendlyName<T>();
        return $"{prefix}[{caller}] {message}";
    }

    /// <summary>
    /// Gets a log string with caller instance, including module ID if applicable.
    /// </summary>
    /// <returns>The formatted log string.</returns>
    private static string GetLogStringWithCaller<T>(T instance, string message, string? prefix = null) where T : class
    {
        prefix = GetPrefix(prefix);
        var caller = GetCallerFriendlyName<T>();

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

    /// <summary>
    /// Gets the leading text to prepend to chat messages for a caller instance.
    /// </summary>
    /// <typeparam name="T">The caller type.</typeparam>
    /// <param name="instance">The caller instance.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <returns>The formatted leading text to use in chat messages.</returns>
    private static string GetChatLeadingText<T>(T instance, string? prefix = null) where T : class
        => GetLogStringWithCaller(instance, string.Empty, prefix);

    /// <summary>
    /// Builds a styled chat message from plain text.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="prefix">The optional prefix to prepend to the message.</param>
    /// <param name="foregroundColor">The optional foreground RGB color of the message.</param>
    /// <param name="glowColor">The optional glow RGB color of the message.</param>
    /// <returns>The built chat message.</returns>
    private static SeString BuildChatMessage(string message, string? prefix = null, Vector3? foregroundColor = null, Vector3? glowColor = null)
    {
        var builder = new SeStringBuilder();
        AppendStyledText(builder, GetLogString(message, prefix), foregroundColor, glowColor);
        return builder.Build();
    }

    /// <summary>
    /// Builds the sender name string for a chat entry.
    /// </summary>
    /// <param name="senderName">The sender name of the message.</param>
    /// <param name="foregroundColor">The optional foreground RGB color of the sender name.</param>
    /// <param name="glowColor">The optional glow RGB color of the sender name.</param>
    /// <returns>The built sender name string.</returns>
    private static SeString BuildSenderName(string senderName, Vector3? foregroundColor = null, Vector3? glowColor = null)
    {
        var builder = new SeStringBuilder();

        // DO NOT add a player payload if the sender is the local player
        // Actually, never add a player payload since it could cause issues if sender has "incorrect" data, such as a made up name and world ID
        AppendStyledText(builder, senderName, foregroundColor, glowColor);

        return builder.Build();
    }

    /// <summary>
    /// Appends styled text to a chat string builder.
    /// </summary>
    /// <param name="builder">The chat string builder.</param>
    /// <param name="text">The text to append.</param>
    /// <param name="foregroundColor">The optional foreground RGB color of the text.</param>
    /// <param name="glowColor">The optional glow RGB color of the text.</param>
    private static void AppendStyledText(SeStringBuilder builder, string text, Vector3? foregroundColor = null, Vector3? glowColor = null)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (foregroundColor.HasValue)
            builder.Add(new ColorPayload(foregroundColor.Value).AsRaw());

        if (glowColor.HasValue)
            builder.Add(new GlowPayload(glowColor.Value).AsRaw());

        builder.AddText(text);

        if (glowColor.HasValue)
            builder.Add(new GlowEndPayload().AsRaw());

        if (foregroundColor.HasValue)
            builder.Add(new ColorEndPayload().AsRaw());
    }

    /// <summary>
    /// Adds a chat message segment to the segment collection.
    /// </summary>
    /// <param name="segments">The target segment collection.</param>
    /// <param name="text">The text to add.</param>
    /// <param name="style">The style to apply to the text.</param>
    private static void AddChatMessageSegment(List<ChatMessageSegment> segments, string text, ChatStyle style)
    {
        if (string.IsNullOrEmpty(text))
            return;

        segments.Add(new ChatMessageSegment(text, style.ForegroundColor, style.GlowColor));
    }

    /// <summary>
    /// Appends tagged text as styled segments.
    /// </summary>
    /// <param name="segments">The target segment collection.</param>
    /// <param name="taggedText">The tagged text to parse.</param>
    private static void AppendTaggedTextSegments(List<ChatMessageSegment> segments, string taggedText)
    {
        if (string.IsNullOrEmpty(taggedText))
            return;

        var styleStack = new Stack<ChatStyleFrame>();
        var currentStyle = default(ChatStyle);
        var textStartIndex = 0;
        var index = 0;

        while (index < taggedText.Length)
        {
            if (taggedText[index] != '<')
            {
                index++;
                continue;
            }

            var tagEndIndex = taggedText.IndexOf('>', index);
            if (tagEndIndex < 0)
                break;

            var rawTag = taggedText[(index + 1)..tagEndIndex];
            if (!TryParseChatTag(rawTag, out var tag))
            {
                index++;
                continue;
            }

            AddChatMessageSegment(segments, taggedText[textStartIndex..index], currentStyle);

            if (tag.IsClosing)
            {
                if (TryPopChatStyle(styleStack, tag.Name, out var previousStyle))
                    currentStyle = previousStyle;
                else
                    AddChatMessageSegment(segments, taggedText[index..(tagEndIndex + 1)], currentStyle);
            }
            else
            {
                styleStack.Push(new ChatStyleFrame(tag.Name, currentStyle));
                currentStyle = ApplyChatStyle(currentStyle, tag);
            }

            index = tagEndIndex + 1;
            textStartIndex = index;
        }

        AddChatMessageSegment(segments, taggedText[textStartIndex..], currentStyle);
    }

    /// <summary>
    /// Tries to parse a chat tag.
    /// </summary>
    /// <param name="rawTag">The raw tag content without angle brackets.</param>
    /// <param name="tag">The parsed tag when successful.</param>
    /// <returns><see langword="true"/> when the tag was parsed successfully; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseChatTag(string rawTag, out ChatTag tag)
    {
        rawTag = rawTag.Trim();

        if (string.IsNullOrEmpty(rawTag))
        {
            tag = default;
            return false;
        }

        if (rawTag[0] == '/')
        {
            var tagName = rawTag[1..].Trim();
            if (IsSupportedChatTag(tagName))
            {
                tag = new ChatTag(tagName, true, null, null);
                return true;
            }

            tag = default;
            return false;
        }

        const string colorPrefix = "color=";
        if (rawTag.StartsWith(colorPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseHexColor(rawTag[colorPrefix.Length..].Trim(), out var foregroundColor))
            {
                tag = new ChatTag("color", false, foregroundColor, null);
                return true;
            }

            tag = default;
            return false;
        }

        const string glowPrefix = "glow=";
        if (rawTag.StartsWith(glowPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseHexColor(rawTag[glowPrefix.Length..].Trim(), out var glowColor))
            {
                tag = new ChatTag("glow", false, null, glowColor);
                return true;
            }

            tag = default;
            return false;
        }

        if (rawTag.StartsWith("style", StringComparison.OrdinalIgnoreCase))
        {
            var attributes = rawTag.Length > "style".Length ? rawTag["style".Length..].Trim() : string.Empty;
            Vector3? foregroundColor = TryGetChatTagColorAttribute(attributes, "color", out var parsedForegroundColor) ? parsedForegroundColor : null;
            Vector3? glowColor = TryGetChatTagColorAttribute(attributes, "glow", out var parsedGlowColor) ? parsedGlowColor : null;

            if (foregroundColor.HasValue || glowColor.HasValue)
            {
                tag = new ChatTag("style", false, foregroundColor, glowColor);
                return true;
            }
        }

        tag = default;
        return false;
    }

    /// <summary>
    /// Determines whether a tag name is supported by the chat parser.
    /// </summary>
    /// <param name="tagName">The tag name to check.</param>
    /// <returns><see langword="true"/> when the tag is supported; otherwise, <see langword="false"/>.</returns>
    private static bool IsSupportedChatTag(string tagName)
        => tagName.Equals("color", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("glow", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("style", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Applies a parsed tag to the current chat style.
    /// </summary>
    /// <param name="currentStyle">The current chat style.</param>
    /// <param name="tag">The parsed tag.</param>
    /// <returns>The resulting chat style.</returns>
    private static ChatStyle ApplyChatStyle(ChatStyle currentStyle, ChatTag tag)
        => new(tag.ForegroundColor ?? currentStyle.ForegroundColor, tag.GlowColor ?? currentStyle.GlowColor);

    /// <summary>
    /// Tries to restore the previous chat style for a closing tag.
    /// </summary>
    /// <param name="styleStack">The style stack.</param>
    /// <param name="tagName">The closing tag name.</param>
    /// <param name="previousStyle">The restored previous style when successful.</param>
    /// <returns><see langword="true"/> when a matching style was restored; otherwise, <see langword="false"/>.</returns>
    private static bool TryPopChatStyle(Stack<ChatStyleFrame> styleStack, string tagName, out ChatStyle previousStyle)
    {
        if (styleStack.Count > 0 && styleStack.Peek().TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase))
        {
            previousStyle = styleStack.Pop().PreviousStyle;
            return true;
        }

        previousStyle = default;
        return false;
    }

    /// <summary>
    /// Tries to read a color attribute from a chat style tag.
    /// </summary>
    /// <param name="attributes">The attribute text to parse.</param>
    /// <param name="attributeName">The attribute name to read.</param>
    /// <param name="color">The parsed color when successful.</param>
    /// <returns><see langword="true"/> when the attribute was found and parsed successfully; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetChatTagColorAttribute(string attributes, string attributeName, out Vector3 color)
    {
        var parts = attributes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var attributePrefix = $"{attributeName}=";

        foreach (var part in parts)
        {
            if (!part.StartsWith(attributePrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            return TryParseHexColor(part[attributePrefix.Length..].Trim(), out color);
        }

        color = default;
        return false;
    }

    /// <summary>
    /// Tries to parse a hexadecimal color into an RGB vector.
    /// </summary>
    /// <param name="value">The color value to parse.</param>
    /// <param name="color">The parsed RGB color when successful.</param>
    /// <returns><see langword="true"/> when the color was parsed successfully; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseHexColor(string value, out Vector3 color)
    {
        value = value.Trim().Trim('"', '\'');

        if (value.StartsWith('#'))
            value = value[1..];

        if (value.Length == 6
            && byte.TryParse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
            && byte.TryParse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
            && byte.TryParse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            color = new Vector3(red / 255f, green / 255f, blue / 255f);
            return true;
        }

        color = default;
        return false;
    }

    #endregion
}
