using Dalamud.Game.Text.SeStringHandling.Payloads;
using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// Makes a run of chat text clickable and remembers what each click does. Links are keyed by name, so a message
/// printed many times registers one link rather than one per line, and the oldest link is dropped once
/// <see cref="MaxLinks"/> are live, which eventually stops a very old line responding.
/// </summary>
public static class ChatLinkHelper
{
    /// <summary>How many links stay clickable at once; past this the least recently registered one is dropped.</summary>
    public static int MaxLinks { get; set; } = 256;

    /// <summary>The first command id handed out, well clear of anything a plugin is likely to register by hand.</summary>
    private const uint FirstCommandId = 1000;

    private static readonly Dictionary<string, Registration> ByKey = new(StringComparer.Ordinal);
    private static readonly List<string> KeysInOrder = [];

    private static uint nextCommandId = FirstCommandId;

    /// <summary>What one name is registered as: the command id the game knows it by, and what a click does.</summary>
    /// <param name="CommandId">The chat-link command id.</param>
    /// <param name="Payload">The payload marking the clickable text.</param>
    private sealed record Registration(uint CommandId, DalamudLinkPayload Payload)
    {
        /// <summary>The click action, replaced rather than re-registered when a name is reused, so the command id stays the same.</summary>
        public Action OnClick { get; set; } = static () => { };
    }

    /// <summary>Registers what a click on a named link does and returns the payload that marks the text.</summary>
    /// <param name="key">A stable name for the link, such as <c>"disable:MyMod"</c>; reusing it replaces the action and keeps the same command id.</param>
    /// <param name="onClick">The action run on the framework thread when the text is clicked.</param>
    /// <returns>The payload to put in front of the text, or <see langword="null"/> before NoireLib is initialized.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> or <paramref name="onClick"/> is <see langword="null"/>.</exception>
    public static DalamudLinkPayload? Register(string key, Action onClick)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(onClick);

        if (!NoireService.IsInitialized())
            return null;

        if (ByKey.TryGetValue(key, out var existing))
        {
            existing.OnClick = onClick;
            return existing.Payload;
        }

        DropOldestWhileFull();

        var commandId = nextCommandId++;
        Registration? registration = null;

        try
        {
            var payload = NoireService.ChatGui.AddChatLinkHandler(commandId, (_, _) => Click(registration));
            registration = new Registration(commandId, payload) { OnClick = onClick };
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Could not register the chat link '{key}'; the text is printed without it.",
                nameof(ChatLinkHelper));
            return null;
        }

        ByKey[key] = registration;
        KeysInOrder.Add(key);

        return registration.Payload;
    }

    /// <summary>Stops a named link responding and releases its command id.</summary>
    /// <param name="key">The name it was registered under.</param>
    /// <returns>True when there was a link to drop.</returns>
    public static bool Unregister(string key)
    {
        if (key == null || !ByKey.Remove(key, out var registration))
            return false;

        KeysInOrder.Remove(key);
        RemoveHandler(registration.CommandId);

        return true;
    }

    /// <summary>Drops every link; NoireLib calls this on shutdown.</summary>
    public static void Clear()
    {
        foreach (var registration in ByKey.Values)
            RemoveHandler(registration.CommandId);

        ByKey.Clear();
        KeysInOrder.Clear();
    }

    /// <summary>How many links are clickable right now.</summary>
    public static int Count => ByKey.Count;

    private static void DropOldestWhileFull()
    {
        while (KeysInOrder.Count >= MaxLinks && KeysInOrder.Count > 0)
            Unregister(KeysInOrder[0]);
    }

    /// <summary>Runs a click, swallowing a handler exception so it cannot reach the chat log.</summary>
    /// <param name="registration">The registration whose action to run, or null.</param>
    private static void Click(Registration? registration)
    {
        if (registration == null)
            return;

        try
        {
            registration.OnClick();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "A chat link handler threw.", nameof(ChatLinkHelper));
        }
    }

    private static void RemoveHandler(uint commandId)
    {
        try
        {
            NoireService.ChatGui.RemoveChatLinkHandler(commandId);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Could not remove the chat link handler {commandId}.", nameof(ChatLinkHelper));
        }
    }
}
