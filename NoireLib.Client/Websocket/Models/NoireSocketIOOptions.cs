using NoireLib.Remote;
using System;
using System.Collections.Generic;
using PackageOptions = SocketIOClient.SocketIOOptions;

namespace NoireLib.Websocket;

/// <summary>
/// What one Socket.IO connection is opened with. Usable as-is. Every property carries a working default.<br/>
/// The values are read when the client is constructed. Set them first, or reach
/// <see cref="NoireSocketIOClient.Underlying"/> afterwards.
/// </summary>
public sealed class NoireSocketIOOptions
{
    /// <summary>
    /// The Engine.IO mount point a Socket.IO server serves on unless it was told otherwise.
    /// </summary>
    public const string DefaultPath = "/socket.io";

    /// <summary>
    /// Gets or sets the Socket.IO namespace to join. Null joins the default one. The protocol package reads the
    /// namespace from the address. This replaces the path of the URL the client was given.
    /// </summary>
    public string? Namespace { get; set; } = null;

    /// <summary>
    /// Gets or sets which Engine.IO transports the connection may use.
    /// </summary>
    public NoireSocketIOTransport Transport { get; set; } = NoireSocketIOTransport.PollingThenUpgrade;

    /// <summary>
    /// Gets or sets the Engine.IO mount point where the handshake is sent. It has no effect on which namespace is joined.
    /// </summary>
    public string Path { get; set; } = DefaultPath;

    /// <summary>
    /// Gets or sets the object sent as the handshake's auth payload, serialized with the rest of the connection.
    /// </summary>
    public object? Auth { get; set; } = null;

    /// <summary>
    /// Gets the query parameters appended to the handshake address.
    /// </summary>
    public IDictionary<string, string> Query { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets or sets the HTTP settings for the handshake and every poll. All are honoured on both transports.</summary>
    public NoireSocketHttpOptions Http { get; set; } = new();

    /// <summary>
    /// Gets or sets the reconnection schedule, mapped onto the protocol package's own loop.
    /// </summary>
    public NoireSocketIOReconnect Reconnect { get; set; } = NoireSocketIOReconnect.Default;

    /// <summary>
    /// Gets or sets the thread callbacks run on. <see cref="NoireRemoteThread.Inherit"/> takes the host's own default.
    /// </summary>
    public NoireRemoteThread Thread { get; set; } = NoireRemoteThread.Inherit;

    /// <summary>
    /// Gets or sets the host callbacks are handed to. Null takes the host installed when the connection opens.
    /// </summary>
    public INoireRemoteHost? Host { get; set; } = null;

    /// <summary>
    /// Gets or sets the last-resort hook on the protocol package's own options, run once after everything above has
    /// been mapped onto them.
    /// </summary>
    public Action<PackageOptions>? ConfigureOptions { get; set; } = null;

    /// <summary>
    /// Copies these options.
    /// </summary>
    /// <returns>A copy whose query and HTTP settings are its own.</returns>
    public NoireSocketIOOptions Clone()
    {
        var copy = new NoireSocketIOOptions
        {
            Namespace = Namespace,
            Transport = Transport,
            Path = Path,
            Auth = Auth,
            Http = Http.Clone(),
            Reconnect = Reconnect,
            Thread = Thread,
            Host = Host,
            ConfigureOptions = ConfigureOptions,
        };

        foreach (var pair in Query)
            copy.Query[pair.Key] = pair.Value;

        return copy;
    }
}
