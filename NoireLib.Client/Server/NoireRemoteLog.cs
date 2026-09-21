using System;
using System.Collections.Generic;

namespace NoireLib.Remote;

/// <summary>
/// Feeds a line onto the log channel, where the console and any subscriber read it. A host that logs through
/// something of its own calls this to make those lines visible too.
/// </summary>
public static class NoireRemoteLog
{
    private static readonly List<NoireRemoteServer> Sinks = [];

    /// <summary>
    /// Gets whether any listener publishes log lines at all. A host checks it before building a line.
    /// </summary>
    public static bool IsTailing
    {
        get
        {
            lock (Sinks)
            {
                foreach (var server in Sinks)
                {
                    if (server.Options.LogScope != NoireRemoteLogScope.None)
                        return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Publishes one line of the host's log onto the log channel of every listener whose scope takes it: every line
    /// for <see cref="NoireRemoteLogScope.Everything"/>, and only the lines its own plugin wrote for
    /// <see cref="NoireRemoteLogScope.Plugin"/>.
    /// </summary>
    /// <param name="line">The line, its <see cref="NoireRemoteLogLine.Source"/> naming what wrote it.</param>
    public static void Publish(NoireRemoteLogLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        NoireRemoteServer[] targets;

        lock (Sinks)
        {
            if (Sinks.Count == 0)
                return;

            targets = [.. Sinks];
        }

        var topic = NoireRemotePaths.LogTopicPrefix + line.Level;

        foreach (var server in targets)
        {
            var scope = server.Options.LogScope;

            if (scope == NoireRemoteLogScope.None)
                continue;

            if (scope == NoireRemoteLogScope.Plugin && !string.Equals(line.Source, server.Host.Identity.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                server.PublishEvent(NoireRemoteChannels.Log, topic, line);
            }
            catch (Exception)
            {
            }
        }
    }

    internal static void Attach(NoireRemoteServer server)
    {
        lock (Sinks)
        {
            if (!Sinks.Contains(server))
                Sinks.Add(server);
        }
    }

    internal static void Detach(NoireRemoteServer server)
    {
        lock (Sinks)
            Sinks.Remove(server);
    }
}

/// <summary>
/// One line of a tailed log, as the log channel carries it.
/// </summary>
public sealed class NoireRemoteLogLine
{
    /// <summary>
    /// Gets or sets how severe the line is: <c>verbose</c>, <c>debug</c>, <c>information</c>, <c>warning</c>,
    /// <c>error</c> or <c>fatal</c>.
    /// </summary>
    public string Level { get; set; } = "information";

    /// <summary>
    /// Gets or sets the line.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the line was written, in UTC.
    /// </summary>
    public DateTimeOffset AtUtc { get; set; }

    /// <summary>
    /// Gets or sets the exception behind the line, or null.
    /// </summary>
    public string? Exception { get; set; }

    /// <summary>
    /// Gets or sets what wrote the line: a plugin's internal name, or <c>Dalamud</c>.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the process the line was written in. Two listeners in one game client share one log, and a console
    /// showing both tells them apart by this.
    /// </summary>
    public int ProcessId { get; set; }
}
