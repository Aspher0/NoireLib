using System;
using System.Collections.Generic;

namespace NoireLib.Remote;

/// <summary>
/// One listening plugin, either read from a directory record or addressed by hand.
/// </summary>
public sealed class NoireRemoteInstance
{
    private readonly NoireRemoteInstanceRecord? record;

    internal NoireRemoteInstance(NoireRemoteInstanceRecord record)
    {
        this.record = record;
        Machine = record.Machine;
        Id = record.Instance;
        ProcessId = record.Pid;
        Address = record.Address;
        Port = record.Port;
        Plugin = record.Plugin;
        PluginVersion = record.PluginVersion;
        Label = record.Label;
        Endpoints = record.Endpoints;
        Metadata = record.Metadata;
        StartedUtc = record.StartedUtc;
        Token = record.Token;
        AllowFleetControl = record.AllowFleetControl;
    }

    internal NoireRemoteInstance(string address, int port, string? secret)
    {
        Address = address;
        Port = port;
        Plugin = string.Empty;
        PluginVersion = string.Empty;
        Label = address + ":" + port;
        Endpoints = [];
        Secret = secret;
    }

    /// <summary>
    /// Gets the machine the listener runs on, or an empty string for an instance addressed by hand and never pinged.
    /// </summary>
    public string Machine { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets whether the listener answered a connection. A host that answers a discovery probe but refuses a
    /// connection is listed with this false.
    /// </summary>
    public bool IsReachable { get; internal set; } = true;

    internal void Apply(NoireRemotePing ping)
    {
        Machine = string.IsNullOrEmpty(ping.Machine) ? Machine : ping.Machine;
        Label = string.IsNullOrEmpty(ping.Label) ? Label : ping.Label;
        Plugin = string.IsNullOrEmpty(ping.Plugin) ? Plugin : ping.Plugin;
        PluginVersion = string.IsNullOrEmpty(ping.PluginVersion) ? PluginVersion : ping.PluginVersion;
        Endpoints = ping.Endpoints;
        Metadata = ping.Metadata.Count == 0 ? Metadata : ping.Metadata;
        AllowFleetControl = ping.AllowFleetControl;
        IsReachable = true;
    }

    /// <summary>
    /// Gets whether this listener lets another listener's console drive it in a fleet.
    /// </summary>
    public bool AllowFleetControl { get; internal set; }

    /// <summary>
    /// Gets the instance id. Empty for an instance addressed by hand until it has been pinged.
    /// </summary>
    public Guid Id { get; internal set; }

    /// <summary>
    /// Gets the process id of the game client, or zero for an instance addressed by hand.
    /// </summary>
    public int ProcessId { get; }

    /// <summary>
    /// Gets the address the listener is bound to.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Gets the port the listener is bound to.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Gets the internal name of the publishing plugin.
    /// </summary>
    public string Plugin { get; private set; }

    /// <summary>
    /// Gets the version of the publishing plugin.
    /// </summary>
    public string PluginVersion { get; private set; }

    /// <summary>
    /// Gets the label telling one running game client from another.
    /// </summary>
    public string Label { get; private set; }

    /// <summary>
    /// Gets the endpoint names the record lists. Empty for an instance addressed by hand.
    /// </summary>
    public IReadOnlyList<string> Endpoints { get; private set; }

    /// <summary>
    /// Gets what the instance tags itself with. A caller can aim at a tag as well as at a character name.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; internal set; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets one of the instance's tags, or null when it carries no such tag.
    /// </summary>
    /// <param name="key">The tag name, compared ignoring case.</param>
    /// <returns>The value, or null.</returns>
    public string? this[string key]
        => key != null && Metadata.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Gets when the listener started.
    /// </summary>
    public DateTime StartedUtc { get; }

    /// <summary>
    /// Gets the base URL of this instance.
    /// </summary>
    public string BaseUrl => NoireRemotePaths.BaseUrl(Address, Port);

    internal string? Token { get; }

    internal string? Secret { get; }

    internal NoireRemoteInstanceRecord? Record => record;

    /// <summary>
    /// Targets an endpoint on this instance, skipping discovery.
    /// </summary>
    /// <param name="name">The endpoint name.</param>
    /// <returns>An endpoint bound to this instance.</returns>
    public NoireRemoteApi Endpoint(string name)
        => new(name, this);

    /// <summary>
    /// Asks the listener whether it is serving and what it publishes.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The liveness answer.</returns>
    /// <exception cref="NoireRemoteNotFoundException">If nothing answers on the address and port.</exception>
    public async System.Threading.Tasks.Task<NoireRemotePing> PingAsync(System.Threading.CancellationToken cancellationToken = default)
    {
        var answer = await RemoteWireTransport.SendDocumentAsync<NoireRemotePing>(
            this,
            System.Net.Http.HttpMethod.Get,
            NoireRemotePaths.Prefix + NoireRemotePaths.Ping,
            null,
            NoireRemoteClient.Options.ConnectTimeout + NoireRemoteClient.Options.CallTimeout,
            cancellationToken).ConfigureAwait(false);

        if (Id == Guid.Empty)
            Id = answer.Instance;

        return answer;
    }

    /// <summary>
    /// Reads the published surface of this instance.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The manifest.</returns>
    public async System.Threading.Tasks.Task<NoireRemoteManifest> ManifestAsync(System.Threading.CancellationToken cancellationToken = default)
        => await RemoteWireTransport.SendDocumentAsync<NoireRemoteManifest>(
            this,
            System.Net.Http.HttpMethod.Get,
            NoireRemotePaths.Prefix + NoireRemotePaths.Manifest,
            null,
            NoireRemoteClient.Options.CallTimeout,
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Returns the label, the plugin and the port, the line an ambiguity message prints.
    /// </summary>
    /// <returns>A one line description.</returns>
    public override string ToString()
    {
        var label = string.IsNullOrEmpty(Label) ? Address + ":" + Port : Label;
        var plugin = string.IsNullOrEmpty(Plugin) ? "unknown plugin" : Plugin;

        return ProcessId > 0
            ? label + " (" + plugin + ", pid " + ProcessId + ", port " + Port + ")"
            : label + " (" + plugin + ", port " + Port + ")";
    }
}
