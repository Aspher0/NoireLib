using System;
using System.Collections.Generic;

namespace NoireLib.Remote;

/// <summary>
/// The body the console page posts to open a session. Both members are optional: an address
/// <see cref="NoireRemoteOptions.ConsoleAccess"/> lets in on its own posts neither.
/// </summary>
public sealed class NoireRemoteConsoleGrant
{
    /// <summary>
    /// Gets or sets the one-use grant read out of the page's URL fragment.
    /// </summary>
    public string? Grant { get; set; }

    /// <summary>
    /// Gets or sets the password typed into the page's gate, matched against <see cref="NoireRemoteOptions.ConsoleKey"/>.
    /// </summary>
    public string? Key { get; set; }
}

/// <summary>
/// The answer to a redeemed grant.
/// </summary>
public sealed class NoireRemoteConsoleSession
{
    /// <summary>
    /// Gets or sets whether the grant was accepted.
    /// </summary>
    public bool Ok { get; set; } = true;

    /// <summary>
    /// Gets or sets the session credential the page sets on every later request.
    /// </summary>
    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// Everything the console page reads once, at load.
/// </summary>
public sealed class NoireRemoteConsoleBootstrap
{
    /// <summary>
    /// Gets or sets whether the bootstrap was served.
    /// </summary>
    public bool Ok { get; set; } = true;

    /// <summary>
    /// Gets or sets the published surface the page draws its forms from.
    /// </summary>
    public NoireRemoteManifest? Manifest { get; set; }

    /// <summary>
    /// Gets or sets the title the page carries.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the instance id of the listener behind the page.
    /// </summary>
    public Guid Instance { get; set; }

    /// <summary>
    /// Gets or sets the prefix every member call the page makes sits under.
    /// </summary>
    public string ApiBaseUrl { get; set; } = NoireRemotePaths.Prefix;

    /// <summary>
    /// Gets or sets the loopback credential, present only for a page opened over loopback.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets whether the page was opened over loopback.
    /// </summary>
    public bool IsLoopback { get; set; }

    /// <summary>
    /// Gets or sets whether the page remembers the last values typed into a member.
    /// </summary>
    public bool RememberValues { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the page draws a code to open itself from a phone.
    /// </summary>
    public bool ShowQrCode { get; set; } = true;

    /// <summary>
    /// Gets or sets the channels the page can subscribe to.
    /// </summary>
    public IReadOnlyList<NoireRemoteManifestChannel>? Channels { get; set; }

    /// <summary>
    /// Gets or sets the custom properties the page writes onto its root, keyed without the prefix.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Variables { get; set; }

    /// <summary>
    /// Gets or sets the stylesheet written after the page's own.
    /// </summary>
    public string? StyleSheet { get; set; }

    /// <summary>
    /// Gets or sets the URL another device on the network opens, or null when the console is on loopback.
    /// </summary>
    public string? RemoteUrl { get; set; }

    /// <summary>
    /// Gets or sets the route the page upgrades its live channel on.
    /// </summary>
    public string LivePath { get; set; } = NoireRemoteConsolePaths.Live;

    /// <summary>
    /// Gets or sets the sub-protocol the live channel speaks, offered with the session token beside it.
    /// </summary>
    public string LiveProtocol { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the schedule the page waits on before opening the live channel again.
    /// </summary>
    public NoireRemoteConsoleRetry? Reconnect { get; set; }
}

/// <summary>
/// How long the console page waits before opening its live channel again, in milliseconds so a script reads it
/// without parsing.
/// </summary>
public sealed class NoireRemoteConsoleRetry
{
    /// <summary>
    /// Gets or sets the delay before the first retry.
    /// </summary>
    public int InitialDelayMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets what each delay is multiplied by for the next attempt.
    /// </summary>
    public double Multiplier { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets the ceiling no delay passes.
    /// </summary>
    public int MaxDelayMs { get; set; } = 30000;

    /// <summary>
    /// Gets or sets how far a delay may be moved either side of its computed value, as a fraction of it.
    /// </summary>
    public double Jitter { get; set; } = 0.25;
}

/// <summary>
/// The frame kinds the console's live channel carries, in <see cref="NoireRemoteConsoleLiveCommand.Op"/> and
/// <see cref="NoireRemoteConsoleLiveFrame.Op"/>.
/// </summary>
public static class NoireRemoteConsoleLiveOps
{
    /// <summary>Sent once when the channel opens, naming the listener and what the session starts on.</summary>
    public const string Ready = "ready";

    /// <summary>A batch of events with the cursor to resume each channel from.</summary>
    public const string Events = "events";

    /// <summary>The page asking for a set of channels, topics, filters and cursors.</summary>
    public const string Subscribe = "subscribe";

    /// <summary>The listener's answer to a subscribe, naming what the session now watches.</summary>
    public const string Subscribed = "subscribed";

    /// <summary>The page dropping channels from what it watches.</summary>
    public const string Unsubscribe = "unsubscribe";

    /// <summary>A command the listener refused, carrying why.</summary>
    public const string Error = "error";

    /// <summary>The page asking the listener to open one of its published sockets on its behalf.</summary>
    public const string SocketOpen = "socket.open";

    /// <summary>The page sending one message on a socket the listener holds for it.</summary>
    public const string SocketSend = "socket.send";

    /// <summary>The page closing a socket the listener holds for it.</summary>
    public const string SocketClose = "socket.close";

    /// <summary>What a relayed socket is doing: open, sent or closed.</summary>
    public const string SocketState = "socket.state";

    /// <summary>One message that arrived on a relayed socket.</summary>
    public const string SocketMessage = "socket.message";

    /// <summary>Listener to page: a part of the page is stale and has to be fetched again, named by <c>scope</c>.</summary>
    public const string Invalidate = "invalidate";

    /// <summary>Page to listener: the other listeners in the page's fleet whose events it wants, by instance id.</summary>
    public const string FleetWatch = "fleet.watch";

    /// <summary>Listener to page: events another listener in the fleet published, its id in <c>source</c>.</summary>
    public const string FleetEvents = "fleet.events";
}

/// <summary>
/// What the console page sends over its live channel.
/// </summary>
public sealed class NoireRemoteConsoleLiveCommand
{
    /// <summary>
    /// Gets or sets which command this is, from <see cref="NoireRemoteConsoleLiveOps"/>.
    /// </summary>
    public string Op { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what a subscribe asks for, in the same vocabulary the event poll takes.
    /// </summary>
    public NoireRemoteEventRequest? Subscription { get; set; }

    /// <summary>
    /// Gets or sets the channels an unsubscribe drops.
    /// </summary>
    public IReadOnlyList<string>? Channels { get; set; }

    /// <summary>
    /// Gets or sets which published socket a socket command is about, by name.
    /// </summary>
    public string? Socket { get; set; }

    /// <summary>
    /// Gets or sets what a socket send carries.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Gets or sets the listeners a fleet command is about, by instance id.
    /// </summary>
    public IReadOnlyList<Guid>? Instances { get; set; }
}

/// <summary>
/// What the listener pushes to the console page over its live channel.
/// </summary>
public sealed class NoireRemoteConsoleLiveFrame
{
    /// <summary>
    /// Gets or sets which frame this is, from <see cref="NoireRemoteConsoleLiveOps"/>.
    /// </summary>
    public string Op { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the instance id of the listener behind the channel.
    /// </summary>
    public Guid Instance { get; set; }

    /// <summary>
    /// Gets or sets which relayed socket a socket frame is about.
    /// </summary>
    public string? Socket { get; set; }

    /// <summary>
    /// Gets or sets what a relayed socket is doing, or what one of its messages carried.
    /// </summary>
    public string? State { get; set; }

    /// <summary>
    /// Gets or sets the text a relayed socket message carried.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Gets or sets which part of the page an invalidation is about: <c>settings</c>, <c>sockets</c> or <c>fleet</c>.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets which listener a fleet frame's events came from.
    /// </summary>
    public Guid? Source { get; set; }

    /// <summary>
    /// Gets or sets the channels the session watches.
    /// </summary>
    public IReadOnlyList<string>? Channels { get; set; }

    /// <summary>
    /// Gets or sets the topics the session watches, or null for every topic.
    /// </summary>
    public IReadOnlyList<string>? Topics { get; set; }

    /// <summary>
    /// Gets or sets the events, oldest first.
    /// </summary>
    public IReadOnlyList<NoireRemoteEvent>? Events { get; set; }

    /// <summary>
    /// Gets or sets the cursor to resume each channel from, keyed by channel name.
    /// </summary>
    public IReadOnlyDictionary<string, long>? Cursors { get; set; }

    /// <summary>
    /// Gets or sets whether events were dropped from a buffer before this frame read it.
    /// </summary>
    public bool Missed { get; set; }

    /// <summary>
    /// Gets or sets which channels dropped events, or null when none did.
    /// </summary>
    public IReadOnlyList<string>? MissedChannels { get; set; }

    /// <summary>
    /// Gets or sets why a command was refused, present on an error frame alone.
    /// </summary>
    public NoireRemoteError? Error { get; set; }
}
