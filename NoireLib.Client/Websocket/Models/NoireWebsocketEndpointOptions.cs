using NoireLib.Remote;
using System.Collections.Generic;

namespace NoireLib.Websocket;

/// <summary>
/// The settings one published socket runs under. Every property has a working default. <c>new
/// NoireWebsocketEndpointOptions()</c> is a complete configuration. They are read when a peer upgrades. A change
/// reaches only the next connection. Connections already open keep their existing settings.
/// </summary>
public sealed class NoireWebsocketEndpointOptions
{
    /// <summary>
    /// Gets the sub-protocols this socket speaks, in order of preference. The first one the handshake also offers is
    /// echoed back. An empty list negotiates none.
    /// </summary>
    public IList<string> SubProtocols { get; } = new List<string>();

    /// <summary>
    /// Gets or sets whether a handshake offering none of <see cref="SubProtocols"/> is refused. Otherwise it is
    /// accepted with no sub-protocol. It does nothing while the list is empty.
    /// </summary>
    public bool RequireSubProtocol { get; set; } = false;

    /// <summary>
    /// Gets or sets whether the socket accepts an upgrade from another machine. Inherit resolves to
    /// <see cref="NoireRemoteAccess.Local"/>.
    /// </summary>
    public NoireRemoteAccess Access { get; set; } = NoireRemoteAccess.Inherit;

    /// <summary>
    /// Gets or sets the character state handlers need before they run. Checked once per drain, covering every
    /// message in it. A drain that fails it delivers nothing and reports the reason once.
    /// </summary>
    public NoireRemoteReadiness Requires { get; set; } = NoireRemoteReadiness.Inherit;

    /// <summary>
    /// Gets or sets the thread handlers run on. Inherit asks the server options, then the host.
    /// </summary>
    public NoireRemoteThread Thread { get; set; } = NoireRemoteThread.Inherit;

    /// <summary>
    /// Copies these options.
    /// </summary>
    /// <returns>A copy sharing no state with this one.</returns>
    public NoireWebsocketEndpointOptions Clone()
    {
        var copy = new NoireWebsocketEndpointOptions
        {
            RequireSubProtocol = RequireSubProtocol,
            Access = Access,
            Requires = Requires,
            Thread = Thread,
        };

        foreach (var protocol in SubProtocols)
            copy.SubProtocols.Add(protocol);

        return copy;
    }
}
