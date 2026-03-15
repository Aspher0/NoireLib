using NoireLib.Core.Modules;
using NoireLib.Enums;
using NoireLib.EventBus;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.NetworkRelay;

/// <summary>
/// A module providing a highly configurable hybrid network relay for local-network communication.<br/>
/// UDP remains available for best-effort delivery, while TCP adds a reliable transport path for commands,
/// control flows, and state synchronization that should not be silently dropped.<br/>
/// Supports peer discovery, direct peer messaging, broadcast-style fan-out, typed subscriptions, async handlers,
/// duplicate suppression, compact byte transport, callback-first usage helpers, and EventBus bridging.
/// </summary>
public partial class NoireNetworkRelay : NoireModuleBase<NoireNetworkRelay>
{
    #region Private Properties and Fields

    private const string WildcardChannel = "*";
    private const string SystemChannel = "__relay/system";
    private const string EnvelopeKindMessage = "message";
    private const string EnvelopeKindHello = "hello";
    private const string EnvelopeKindAcknowledgement = "ack";
    private const int DefaultPort = 53741;
    private const int DefaultMaxPayloadBytes = 60_000;

    private readonly Dictionary<string, RelayPeerRegistration> peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<RelaySubscriptionEntry>> subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NetworkRelaySubscriptionToken> keyToSubscription = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> recentMessageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NetworkRelayEventBridgeHandle> keyToEventBridge = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<EventBusBridgeRegistration> eventBusBridgeRegistrations = [];
    private readonly HashSet<string> allowedPeerIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly AsyncLocal<int> suppressEventBusRelayScope = new();

    private readonly object peerLock = new();
    private readonly object subscriptionLock = new();
    private readonly object transportLock = new();
    private readonly object eventBridgeLock = new();

    private UdpClient? udpClient;
    private TcpListener? tcpListener;
    private CancellationTokenSource? transportCts;
    private Task? receiveLoopTask;
    private Task? tcpAcceptLoopTask;
    private Task? announcementLoopTask;

    private long totalMessagesSent;
    private long totalMessagesReceived;
    private long totalBestEffortMessagesSent;
    private long totalBestEffortMessagesReceived;
    private long totalReliableMessagesSent;
    private long totalReliableMessagesReceived;
    private long totalReliableConnectionsAccepted;
    private long totalBytesSent;
    private long totalBytesReceived;
    private long totalMessagesDropped;
    private long totalDuplicateMessagesDropped;
    private long totalPeerAnnouncementsReceived;
    private long totalSendFailures;
    private long totalReceiveFailures;
    private long totalDispatchExceptionsCaught;
    private long totalExceptionsCaught;
    private long totalSubscriptionsCreated;
    private long totalPeersRegistered;
    private long totalPeersRemoved;
    private long totalEventBusEventsRelayed;
    private long totalEventBusEventsPublishedLocally;

    #endregion

    #region Public Properties and Constructors

    /// <summary>
    /// The associated EventBus instance for publishing relay integration events and bridging application events.<br/>
    /// If <see langword="null"/>, relay state is only exposed through CLR events and methods.
    /// </summary>
    public NoireEventBus? EventBus { get; set; }

    /// <summary>
    /// The default constructor needed for internal purposes.
    /// </summary>
    public NoireNetworkRelay() : base() { }

    /// <summary>
    /// Creates a new NetworkRelay module.
    /// </summary>
    /// <param name="moduleId">Optional module ID for multiple relay instances.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="eventBus">Optional EventBus used to publish relay integration events and bridge application events.</param>
    /// <param name="exceptionHandling">How relay exceptions should be handled.</param>
    /// <param name="port">The UDP port used by the relay.</param>
    /// <param name="enableBroadcast">Whether UDP broadcast sending is enabled.</param>
    /// <param name="defaultChannel">The default logical channel used when none is specified.</param>
    /// <param name="autoAnnounceOnStart">Whether to broadcast a presence announcement when the relay is activated.</param>
    /// <param name="enablePeerDiscovery">Whether peer discovery should be enabled.</param>
    /// <param name="allowLoopbackMessages">Whether self-originated packets should be processed.</param>
    /// <param name="enableReliableTransport">Whether the reliable TCP transport listener should be enabled.</param>
    /// <param name="reliablePort">Optional TCP port used for reliable delivery. If omitted, the UDP port value is reused.</param>
    public NoireNetworkRelay(
        string? moduleId = null,
        bool active = true,
        bool enableLogging = true,
        NoireEventBus? eventBus = null,
        ExceptionBehavior exceptionHandling = ExceptionBehavior.LogAndContinue,
        int port = DefaultPort,
        bool enableBroadcast = true,
        string defaultChannel = "default",
        bool autoAnnounceOnStart = true,
        bool enablePeerDiscovery = true,
        bool allowLoopbackMessages = false,
        bool enableReliableTransport = true,
        int? reliablePort = null)
        : base(moduleId, active, enableLogging, eventBus, exceptionHandling, port, enableBroadcast, defaultChannel, autoAnnounceOnStart, enablePeerDiscovery, allowLoopbackMessages, enableReliableTransport, reliablePort) { }

    /// <summary>
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>.<br/>
    /// Only used for internal module management.
    /// </summary>
    /// <param name="moduleId">The module identifier.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    internal NoireNetworkRelay(ModuleId? moduleId, bool active = true, bool enableLogging = true)
        : base(moduleId, active, enableLogging) { }

    #endregion

    #region Module Lifecycle

    /// <summary>
    /// Initializes the module with optional initialization parameters.
    /// </summary>
    /// <param name="args">The initialization parameters passed to the module constructor.</param>
    protected override void InitializeModule(params object?[] args)
    {
        if (args.Length > 0 && args[0] is NoireEventBus eventBus)
            EventBus = eventBus;

        if (args.Length > 1 && args[1] is ExceptionBehavior exceptionHandling)
            ExceptionHandling = exceptionHandling;

        if (args.Length > 2 && args[2] is int port)
            Port = ValidatePort(port);

        if (args.Length > 3 && args[3] is bool enableBroadcast)
            EnableBroadcast = enableBroadcast;

        if (args.Length > 4 && args[4] is string defaultChannel && !string.IsNullOrWhiteSpace(defaultChannel))
            DefaultChannel = defaultChannel.Trim();

        if (args.Length > 5 && args[5] is bool autoAnnounceOnStart)
            AutoAnnounceOnStart = autoAnnounceOnStart;

        if (args.Length > 6 && args[6] is bool enablePeerDiscovery)
            EnablePeerDiscovery = enablePeerDiscovery;

        if (args.Length > 7 && args[7] is bool allowLoopbackMessages)
            AllowLoopbackMessages = allowLoopbackMessages;

        if (args.Length > 8 && args[8] is bool enableReliableTransport)
            EnableReliableTransport = enableReliableTransport;

        ReliablePort = args.Length > 9 && args[9] is int reliablePort
            ? ValidatePort(reliablePort)
            : Port;

        InstanceId = BuildDefaultInstanceId();
        DisplayName = ModuleId ?? Environment.MachineName;

        if (EnableLogging)
            NoireLogger.LogInfo(this, $"NetworkRelay module initialized on UDP {BindAddress}:{Port} and TCP {BindAddress}:{ReliablePort}.");
    }

    /// <summary>
    /// Called when the module is activated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> false to true.
    /// </summary>
    protected override void OnActivated()
    {
        StartTransport();

        if (AutoAnnounceOnStart && EnablePeerDiscovery)
            AnnouncePresence();

        if (EnableLogging)
        {
            var reliablePortInfo = EnableReliableTransport ? ReliablePort.ToString() : "disabled";
            NoireLogger.LogInfo(this, $"NetworkRelay module activated on {BindAddress}:{Port} (Reliable TCP: {reliablePortInfo}).");
        }
    }

    /// <summary>
    /// Called when the module is deactivated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> true to false.
    /// </summary>
    protected override void OnDeactivated()
    {
        StopTransport();

        if (EnableLogging)
            NoireLogger.LogInfo(this, "NetworkRelay module deactivated.");
    }

    #endregion

    #region Events

    /// <summary>
    /// Event raised for every relay message successfully received and parsed.
    /// </summary>
    public event Action<NetworkRelayMessage>? MessageReceived;

    /// <summary>
    /// Event raised when a peer is registered or refreshed.
    /// </summary>
    public event Action<NetworkRelayPeer>? PeerSeen;

    /// <summary>
    /// Event raised when a peer is removed or expired.
    /// </summary>
    public event Action<NetworkRelayPeer>? PeerRemoved;

    /// <summary>
    /// Event raised when a relay error is observed.
    /// </summary>
    public event Action<NetworkRelayError>? Error;

    #endregion

    /// <summary>
    /// Internal dispose method called when the module is disposed.
    /// </summary>
    protected override void DisposeInternal()
    {
        StopTransport();
        ClearEventBridges();
        ClearAllSubscriptions();
        ClearPeers();

        if (EnableLogging)
        {
            var stats = GetStatistics();
            NoireLogger.LogInfo(this, $"NetworkRelay disposed. Sent: {stats.TotalMessagesSent}, Received: {stats.TotalMessagesReceived}, Exceptions: {stats.TotalExceptionsCaught}");
        }
    }
}
