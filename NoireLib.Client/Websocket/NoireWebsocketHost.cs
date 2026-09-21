using NoireLib.Remote;
using System;

namespace NoireLib.Websocket;

/// <summary>
/// Where the host every connection reports to is installed. A program that is not a plugin gets a standalone host
/// that runs callbacks on the thread that read the message. Inside a plugin NoireLib installs one that hops to the
/// framework thread.
/// </summary>
public static class NoireWebsocketHost
{
    private static INoireRemoteHost current = new NoireRemoteStandaloneHost();

    /// <summary>
    /// Gets the host a connection takes when its options name none. It is read when a connection opens, never
    /// cached at the point an options object was constructed.
    /// </summary>
    public static INoireRemoteHost Current => current;

    /// <summary>
    /// Installs the host every later connection defaults to. A connection already open keeps the host it opened with.
    /// </summary>
    /// <param name="host">The host to install.</param>
    /// <exception cref="ArgumentNullException">If the host is null.</exception>
    public static void Install(INoireRemoteHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        current = host;
    }

    /// <summary>
    /// Puts the default host back. A test does this between cases.
    /// </summary>
    public static void Reset()
        => current = new NoireRemoteStandaloneHost();
}
