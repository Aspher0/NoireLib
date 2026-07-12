using NoireLib.EventBus;
using System;

namespace NoireLib.Networker;

/// <summary>
/// Optional settings for a <see cref="NoireNetworker"/> instance.<br/>
/// Same-PC operation requires no configuration at all; everything here has a sensible default.
/// </summary>
public sealed class NetworkerOptions
{
    /// <summary>
    /// Whether this network participates on the LAN. Defaults to false (same-PC only).<br/>
    /// Note: the first LAN use may require allowing inbound connections for the game process in Windows Firewall.
    /// </summary>
    public bool EnableLan { get; set; } = false;

    /// <summary>
    /// An optional pre-shared secret gating LAN peers. The handshake proves knowledge of it without sending it over the wire.<br/>
    /// When null and <see cref="EnableLan"/> is true, the network is open on the LAN (a log line will say so).
    /// </summary>
    public string? LanSecret { get; set; } = null;

    /// <summary>
    /// An optional <see cref="NoireEventBus"/> to integrate with.<br/>
    /// Enables <see cref="NoireNetworker.ShareEvent{TEvent}(NetworkerShareDirection)"/> and the publication of networker lifecycle events.
    /// </summary>
    public NoireEventBus? EventBus { get; set; } = null;

    /// <summary>
    /// Whether networker lifecycle events (peer joined/left/updated, state changed) are published to the attached <see cref="EventBus"/>. Defaults to true.
    /// </summary>
    public bool PublishModuleEvents { get; set; } = true;

    /// <summary>
    /// Overrides the UDP port used for LAN discovery beacons. When null, the port is derived from the network name.
    /// </summary>
    public int? BeaconPort { get; set; } = null;

    /// <summary>
    /// The default timeout for <see cref="NoireNetworker.Request{TRequest, TResponse}(NetworkerPeer, TRequest, TimeSpan?)"/> when none is provided. Defaults to 10 seconds.
    /// </summary>
    public TimeSpan DefaultRequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The maximum number of inbound deliveries queued for the framework thread before the oldest are dropped with an error log. Defaults to 4096.
    /// </summary>
    public int DeliveryQueueCapacity { get; set; } = 4096;

    /// <summary>
    /// The maximum number of outbound messages buffered while the network is starting or re-electing. Defaults to 4096.
    /// </summary>
    public int OutboundBufferCapacity { get; set; } = 4096;

    /// <summary>
    /// The maximum size of a single wire frame in bytes. Oversized frames are rejected and logged. Defaults to 1 MB.
    /// </summary>
    public int MaxFrameBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// The interval between LAN discovery beacons. Defaults to 3 seconds.
    /// </summary>
    public TimeSpan BeaconInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The interval between keep-alive pings on network links. Defaults to 2 seconds.
    /// </summary>
    public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a LAN hub link may stay silent before it is considered dead. Defaults to 8 seconds.
    /// </summary>
    public TimeSpan LanLinkTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Creates a shallow copy of these options.
    /// </summary>
    /// <returns>The copied options.</returns>
    public NetworkerOptions Clone()
        => (NetworkerOptions)MemberwiseClone();
}
