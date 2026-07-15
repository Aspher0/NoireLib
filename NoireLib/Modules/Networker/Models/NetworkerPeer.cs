using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Networker;

/// <summary>
/// Represents a peer on a <see cref="NoireNetworker"/> network - another game instance on this PC or on the LAN.<br/>
/// Peer state is only ever mutated on the delivery thread, so anything read from inside a handler is coherent.
/// </summary>
public class NetworkerPeer
{
    private readonly object stateGate = new();
    private readonly Dictionary<string, string> metadata = new();
    private readonly HashSet<string> flags = new();

    internal NetworkerPeer(Guid id)
    {
        Id = id;
    }

    /// <summary>
    /// The unique, session-scoped identifier of this peer. A relaunched instance gets a new id;
    /// durable identity belongs in metadata (character name, role, ...).
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Whether this peer runs on the same machine as the local instance. Diagnostic only - the API never behaves differently for LAN peers.
    /// </summary>
    public bool IsSameMachine { get; internal set; } = true;

    /// <summary>
    /// The peer generation this peer was last confirmed in; used to sweep stale entries after a hub failover.
    /// </summary>
    internal long SeenGeneration { get; set; }

    /// <summary>
    /// Gets a metadata value for this peer, or null when the key is not set.
    /// </summary>
    /// <param name="key">The metadata key.</param>
    /// <returns>The metadata value, or null.</returns>
    public string? this[string key]
    {
        get
        {
            lock (stateGate)
                return metadata.TryGetValue(key, out var value) ? value : null;
        }
    }

    /// <summary>
    /// Gets a snapshot of this peer's metadata.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata
    {
        get
        {
            lock (stateGate)
                return new Dictionary<string, string>(metadata);
        }
    }

    /// <summary>
    /// Gets a snapshot of this peer's coordination flags.
    /// </summary>
    public IReadOnlyCollection<string> Flags
    {
        get
        {
            lock (stateGate)
                return flags.ToArray();
        }
    }

    /// <summary>
    /// Determines whether this peer currently carries the given coordination flag.
    /// </summary>
    /// <param name="flag">The flag name.</param>
    /// <returns>True if the flag is set; otherwise, false.</returns>
    public bool HasFlag(string flag)
    {
        lock (stateGate)
            return flags.Contains(flag);
    }

    internal bool SetMetadataInternal(string key, string? value)
    {
        lock (stateGate)
        {
            if (value == null)
                return metadata.Remove(key);

            if (metadata.TryGetValue(key, out var existing) && existing == value)
                return false;

            metadata[key] = value;
            return true;
        }
    }

    internal bool SetFlagInternal(string flag, bool set)
    {
        lock (stateGate)
            return set ? flags.Add(flag) : flags.Remove(flag);
    }

    internal void ReplaceState(Dictionary<string, string> newMetadata, IEnumerable<string> newFlags)
    {
        lock (stateGate)
        {
            metadata.Clear();

            foreach (var pair in newMetadata)
                metadata[pair.Key] = pair.Value;

            flags.Clear();
            flags.UnionWith(newFlags);
        }
    }

    internal (Dictionary<string, string> Metadata, string[] Flags) CaptureState()
    {
        lock (stateGate)
            return (new Dictionary<string, string>(metadata), flags.ToArray());
    }

    /// <summary>
    /// Returns a string representation of this peer.
    /// </summary>
    /// <returns>The string representation.</returns>
    public override string ToString()
    {
        var character = this["character"];
        return character != null ? $"{Id} ({character})" : Id.ToString();
    }
}

/// <summary>
/// The local instance's own presence on a <see cref="NoireNetworker"/> network.<br/>
/// Metadata set here is automatically synchronized to every peer.
/// </summary>
public sealed class NetworkerSelf : NetworkerPeer
{
    private readonly Action<string> onChanged;

    internal NetworkerSelf(Guid id, Action<string> onChanged) : base(id)
    {
        this.onChanged = onChanged;
    }

    /// <summary>
    /// Sets a metadata value on the local instance and announces it to every peer.
    /// </summary>
    /// <param name="key">The metadata key.</param>
    /// <param name="value">The metadata value.</param>
    /// <returns>This instance for chaining.</returns>
    public NetworkerSelf Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        if (SetMetadataInternal(key, value))
            onChanged(key);

        return this;
    }

    /// <summary>
    /// Removes a metadata value from the local instance and announces the removal to every peer.
    /// </summary>
    /// <param name="key">The metadata key to remove.</param>
    /// <returns>This instance for chaining.</returns>
    public NetworkerSelf Remove(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (SetMetadataInternal(key, null))
            onChanged(key);

        return this;
    }
}
