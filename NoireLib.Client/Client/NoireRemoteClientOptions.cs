using Newtonsoft.Json;
using System;

namespace NoireLib.Remote;

/// <summary>
/// The settings a caller outside the game uses to find and reach a listening plugin. Nothing here has to be set for
/// a loopback call to work.
/// </summary>
public sealed class NoireRemoteClientOptions
{
    /// <summary>
    /// Gets or sets how long a connection attempt may take. Defaults to two seconds, since a loopback connect either
    /// completes at once or is refused.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets how long a call may take before the caller gives up. Defaults to ten seconds.
    /// </summary>
    public TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets how long a directory read is reused before the folder is enumerated again. Defaults to one second.
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets how old a record's heartbeat may be before the instance is treated as gone. Defaults to a minute.
    /// </summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the folder the instance records are read from.
    /// </summary>
    public string RegistryDirectory { get; set; } = NoireRemoteDirectory.DefaultDirectory();

    /// <summary>Gets or sets whether a resolved instance is confirmed with the liveness route before the first call. Catches a stale record. Defaults to true.</summary>
    public bool VerifyWithPing { get; set; } = true;

    /// <summary>
    /// Gets or sets whether a record's process is checked before it is used. Defaults to true.
    /// </summary>
    public bool CheckProcessLiveness { get; set; } = true;

    /// <summary>
    /// Gets or sets whether a record whose process is gone is deleted on sight. Defaults to true.
    /// </summary>
    public bool DeleteStaleRecords { get; set; } = true;

    /// <summary>
    /// Gets or sets whether one rejected credential triggers a single silent retry after re-reading the directory.
    /// A plugin reload produces exactly this symptom. Defaults to true.
    /// </summary>
    public bool RetryOnStaleToken { get; set; } = true;

    /// <summary>
    /// Gets or sets an instance discovery never returns. A plugin that both serves and calls sets it to its own
    /// listener, since calling yourself over a member that hops to the host thread deadlocks.
    /// </summary>
    public Guid? ExcludeInstance { get; set; } = null;

    /// <summary>
    /// Gets or sets the shared secret the listeners on the local network were configured with. Without it, discovery
    /// reads the record folder alone and never leaves this machine.
    /// </summary>
    public string? NetworkSecret { get; set; } = null;

    /// <summary>
    /// Gets or sets the port a discovery probe is sent to. Zero derives it from the secret.
    /// </summary>
    public int DiscoveryPort { get; set; } = 0;

    /// <summary>
    /// Gets or sets how far a discovery answer's timestamp may be from this machine's clock. Defaults to two minutes.
    /// </summary>
    public TimeSpan RemoteClockSkew { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Gets or sets the settings used for argument and result conversion. Defaults to the wire settings.
    /// </summary>
    public JsonSerializerSettings SerializerSettings { get; set; } = NoireRemoteJson.CreateSettings();

    /// <summary>
    /// Creates a shallow copy of these options.
    /// </summary>
    /// <returns>The copied options.</returns>
    public NoireRemoteClientOptions Clone()
        => (NoireRemoteClientOptions)MemberwiseClone();
}
