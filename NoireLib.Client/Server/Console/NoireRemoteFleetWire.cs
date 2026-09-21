using System;
using System.Collections.Generic;

namespace NoireLib.Remote;

/// <summary>
/// One listener the console can see on the local network.
/// </summary>
public sealed class NoireRemoteFleetHost
{
    /// <summary>Gets or sets the listener's instance id.</summary>
    public Guid Instance { get; set; }

    /// <summary>
    /// Gets or sets the machine it runs on.
    /// </summary>
    public string Machine { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the label telling it from another listener on the same machine.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of whatever published its surface.
    /// </summary>
    public string Plugin { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version of whatever published its surface.
    /// </summary>
    public string PluginVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the address it is bound to.
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the port it is bound to.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Gets or sets the endpoint names it publishes.
    /// </summary>
    public IReadOnlyList<string> Endpoints { get; set; } = [];

    /// <summary>
    /// Gets or sets whether it answered a connection.
    /// </summary>
    public bool IsReachable { get; set; } = true;

    /// <summary>
    /// Gets or sets whether it is the listener serving this console.
    /// </summary>
    public bool IsSelf { get; set; }

    /// <summary>
    /// Gets or sets the process the listener runs in. Two listeners in one game client share one process, and one
    /// Dalamud log.
    /// </summary>
    public int ProcessId { get; set; }
}

/// <summary>
/// The answer to the console's fleet listing.
/// </summary>
public sealed class NoireRemoteFleetListing
{
    /// <summary>
    /// Gets or sets whether the listing was produced.
    /// </summary>
    public bool Ok { get; set; } = true;

    /// <summary>
    /// Gets or sets the listeners found, local ones first.
    /// </summary>
    public IReadOnlyList<NoireRemoteFleetHost> Hosts { get; set; } = [];
}

/// <summary>
/// The body the console posts to drive several listeners at once.
/// </summary>
public sealed class NoireRemoteFleetCall
{
    /// <summary>
    /// Gets or sets the arguments every target is called with.
    /// </summary>
    public Newtonsoft.Json.Linq.JToken? Args { get; set; }

    /// <summary>
    /// Gets or sets which listeners to call, by instance id. A call names them explicitly. There is no "every
    /// listener" option, because two different plugins do not share a surface.
    /// </summary>
    public IReadOnlyList<Guid>? Instances { get; set; }

    /// <summary>
    /// Gets or sets the contract fingerprint of the member the page showed. A listener whose member carries another
    /// one is refused on its own row. Its arguments might otherwise mean something else there.
    /// </summary>
    public string? Contract { get; set; }

    /// <summary>
    /// Gets or sets how many calls run at once. Zero runs them all at once, the shape a fleet call needs.
    /// </summary>
    public int MaxConcurrency { get; set; }
}
