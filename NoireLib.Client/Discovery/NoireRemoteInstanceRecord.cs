using System;
using System.Collections.Generic;

namespace NoireLib.Remote;

/// <summary>
/// The file one listening plugin writes so a caller can find it: where it listens, what it publishes and the
/// credential to use. It is a hint, never the truth. The liveness route confirms it.
/// </summary>
public sealed class NoireRemoteInstanceRecord
{
    /// <summary>
    /// Gets or sets the protocol version the listener speaks. A reader skips a record it does not understand.
    /// </summary>
    public int Protocol { get; set; } = NoireRemotePaths.Protocol;

    /// <summary>Gets or sets the instance id, also the file name.</summary>
    public Guid Instance { get; set; }

    /// <summary>
    /// Gets or sets the address the listener is bound to.
    /// </summary>
    public string Address { get; set; } = "127.0.0.1";

    /// <summary>
    /// Gets or sets the port the listener is bound to.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Gets or sets the loopback credential. It is accepted only on a loopback connection.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the process id of the game client.
    /// </summary>
    public int Pid { get; set; }

    /// <summary>Gets or sets when that process started. Tells a reused process id from the original.</summary>
    public DateTime ProcessStartUtc { get; set; }

    /// <summary>
    /// Gets or sets when the listener started.
    /// </summary>
    public DateTime StartedUtc { get; set; }

    /// <summary>
    /// Gets or sets when the record was last rewritten. A reader with no process API uses it as the liveness signal.
    /// </summary>
    public DateTime HeartbeatUtc { get; set; }

    /// <summary>
    /// Gets or sets the internal name of the publishing plugin.
    /// </summary>
    public string Plugin { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version of the publishing plugin.
    /// </summary>
    public string PluginVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the NoireLib version the plugin runs.
    /// </summary>
    public string LibraryVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a label telling one running game client from another. Empty when identity publication is off.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the endpoint names published.
    /// </summary>
    public IReadOnlyList<string> Endpoints { get; set; } = [];

    /// <summary>
    /// Gets or sets the machine the listener runs on. Several listeners on one machine read as one machine with
    /// several ports.
    /// </summary>
    public string Machine { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the one-way identifier of the network secret this listener holds. It lets one responder announce
    /// a listener whose secret it does not have, and it never carries a usable credential.
    /// </summary>
    public string SecretId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what the instance tags itself with. A caller can aim at a tag as well as at a character name.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets or sets whether another listener's console may drive this one in a fleet.
    /// </summary>
    public bool AllowFleetControl { get; set; }

    /// <summary>
    /// Gets the base URL this record points at.
    /// </summary>
    /// <returns>The base URL, with a trailing slash.</returns>
    public string BaseUrl()
        => NoireRemotePaths.BaseUrl(Address, Port);

    /// <summary>
    /// Checks whether this record lists an endpoint, ignoring case.
    /// </summary>
    /// <param name="endpoint">The endpoint name.</param>
    /// <returns>True when the record lists it.</returns>
    public bool Publishes(string endpoint)
    {
        foreach (var name in Endpoints)
        {
            if (string.Equals(name, endpoint, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
