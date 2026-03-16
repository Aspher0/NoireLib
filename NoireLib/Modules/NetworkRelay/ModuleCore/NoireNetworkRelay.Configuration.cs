using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NoireLib.Enums;
using NoireLib.EventBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace NoireLib.NetworkRelay;

public partial class NoireNetworkRelay
{
    #region Module Configuration

    /// <summary>
    /// Sets the EventBus used for relay integration events and bridge registrations.
    /// </summary>
    /// <param name="eventBus">The EventBus instance to associate with the relay, or <see langword="null"/> to detach it.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetEventBus(NoireEventBus? eventBus)
    {
        EventBus = eventBus;
        return this;
    }

    /// <summary>
    /// Defines how exceptions thrown by relay callbacks and background transport operations are handled.
    /// </summary>
    public ExceptionBehavior ExceptionHandling { get; set; } = ExceptionBehavior.LogAndContinue;

    /// <summary>
    /// Sets how relay exceptions should be handled.
    /// </summary>
    /// <param name="mode">The exception handling mode to apply.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetExceptionHandling(ExceptionBehavior mode)
    {
        ExceptionHandling = mode;
        return this;
    }

    /// <summary>
    /// The unique identity used by this relay instance on the network.
    /// </summary>
    public string InstanceId { get; private set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Sets the relay instance identifier.
    /// </summary>
    /// <param name="instanceId">The unique relay instance identifier.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetInstanceId(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("Instance ID cannot be empty.", nameof(instanceId));

        InstanceId = instanceId.Trim();
        return this;
    }

    /// <summary>
    /// A friendly name advertised to remote peers.
    /// </summary>
    public string DisplayName { get; private set; } = Environment.MachineName;

    /// <summary>
    /// Sets the display name advertised to remote peers.
    /// </summary>
    /// <param name="displayName">The friendly display name to advertise.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name cannot be empty.", nameof(displayName));

        DisplayName = displayName.Trim();
        return this;
    }

    /// <summary>
    /// The local IP address to bind the listeners to.
    /// </summary>
    public IPAddress BindAddress { get; private set; } = IPAddress.Any;

    /// <summary>
    /// Sets the bind address used by the listeners.
    /// </summary>
    /// <param name="bindAddress">The IP address to bind the listeners to.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetBindAddress(IPAddress bindAddress)
    {
        BindAddress = bindAddress ?? throw new ArgumentNullException(nameof(bindAddress));
        RestartTransportIfRunning();
        return this;
    }

    /// <summary>
    /// Sets the bind address used by the listeners.
    /// </summary>
    /// <param name="bindAddress">The string representation of the IP address to bind to.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetBindAddress(string bindAddress)
    {
        if (!IPAddress.TryParse(bindAddress, out var address))
            throw new ArgumentException("Bind address must be a valid IP address.", nameof(bindAddress));

        return SetBindAddress(address);
    }

    /// <summary>
    /// The UDP port used for listening and broadcast announcements.
    /// </summary>
    public int Port { get; private set; } = DefaultPortUDP;

    /// <summary>
    /// Sets the UDP port used by the relay.
    /// </summary>
    /// <param name="port">The UDP port to listen on.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetPort(int port)
    {
        var previousPort = Port;
        Port = ValidatePort(port);

        if (ReliablePort == previousPort)
            ReliablePort = Port;

        RestartTransportIfRunning();
        return this;
    }

    /// <summary>
    /// Whether UDP broadcast sending is enabled.
    /// </summary>
    public bool EnableBroadcast { get; private set; } = true;

    /// <summary>
    /// Sets whether UDP broadcast sending is enabled.
    /// </summary>
    /// <param name="enableBroadcast">Whether UDP broadcast sending should be enabled.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetBroadcast(bool enableBroadcast)
    {
        EnableBroadcast = enableBroadcast;
        RestartTransportIfRunning();
        return this;
    }

    /// <summary>
    /// Whether remote peer announcements should be processed.
    /// </summary>
    public bool EnablePeerDiscovery { get; private set; } = true;

    /// <summary>
    /// Whether unknown peers discovered on the network are automatically registered.
    /// </summary>
    public bool AutoRegisterPeers { get; private set; } = true;

    /// <summary>
    /// Sets peer discovery behavior.
    /// </summary>
    /// <param name="enablePeerDiscovery">Whether peer discovery should be enabled.</param>
    /// <param name="autoRegisterPeers">Optional override for whether discovered peers are automatically registered.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetPeerDiscovery(bool enablePeerDiscovery, bool? autoRegisterPeers = null)
    {
        EnablePeerDiscovery = enablePeerDiscovery;

        if (autoRegisterPeers.HasValue)
            AutoRegisterPeers = autoRegisterPeers.Value;

        RestartTransportIfRunning();
        return this;
    }

    /// <summary>
    /// Whether datagrams originating from this instance should be processed.
    /// </summary>
    public bool AllowLoopbackMessages { get; private set; }

    /// <summary>
    /// Sets whether loopback packets should be processed.
    /// </summary>
    /// <param name="allowLoopbackMessages">Whether self-originated packets should be processed.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetAllowLoopbackMessages(bool allowLoopbackMessages)
    {
        AllowLoopbackMessages = allowLoopbackMessages;
        return this;
    }

    /// <summary>
    /// Whether sending a message while inactive should automatically activate the relay first.
    /// </summary>
    public bool AutoActivateOnSend { get; private set; } = true;

    /// <summary>
    /// Sets whether sending should automatically activate the relay first.
    /// </summary>
    /// <param name="autoActivateOnSend">Whether send operations should auto-activate the relay if needed.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetAutoActivateOnSend(bool autoActivateOnSend)
    {
        AutoActivateOnSend = autoActivateOnSend;
        return this;
    }

    /// <summary>
    /// Whether a presence announcement should be broadcast immediately after activation.
    /// </summary>
    public bool AutoAnnounceOnStart { get; private set; } = true;

    /// <summary>
    /// Sets whether a presence announcement should be sent immediately when the relay activates.
    /// </summary>
    /// <param name="autoAnnounceOnStart">Whether a presence announcement should be sent on activation.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetAutoAnnounceOnStart(bool autoAnnounceOnStart)
    {
        AutoAnnounceOnStart = autoAnnounceOnStart;
        return this;
    }

    /// <summary>
    /// Whether duplicate messages should be dropped within <see cref="DuplicateMessageWindow"/>.
    /// </summary>
    public bool SuppressDuplicateMessages { get; private set; } = true;

    /// <summary>
    /// The time window used to suppress duplicate message identifiers.
    /// </summary>
    public TimeSpan DuplicateMessageWindow { get; private set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Sets duplicate message suppression behavior.
    /// </summary>
    /// <param name="enabled">Whether duplicate suppression should be enabled.</param>
    /// <param name="window">Optional duplicate suppression time window.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetDuplicateSuppression(bool enabled, TimeSpan? window = null)
    {
        SuppressDuplicateMessages = enabled;

        if (window.HasValue)
            DuplicateMessageWindow = window.Value <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : window.Value;

        return this;
    }

    /// <summary>
    /// Whether async subscription handlers should be awaited in the receive loop.
    /// </summary>
    public bool AwaitAsyncHandlersOnReceive { get; private set; } = true;

    /// <summary>
    /// Sets how async receive handlers are processed.
    /// </summary>
    /// <param name="awaitAsyncHandlers">Whether async handlers should be awaited in the receive loop.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetAwaitAsyncHandlersOnReceive(bool awaitAsyncHandlers)
    {
        AwaitAsyncHandlersOnReceive = awaitAsyncHandlers;
        return this;
    }

    /// <summary>
    /// The default logical channel used when none is specified by callers.
    /// </summary>
    public string DefaultChannel { get; private set; } = "default";

    /// <summary>
    /// Sets the default logical channel used when none is specified by callers.
    /// </summary>
    /// <param name="channel">The default channel name.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetDefaultChannel(string channel)
    {
        DefaultChannel = NormalizeChannel(channel);
        return this;
    }

    /// <summary>
    /// Whether the reliable TCP listener is enabled.
    /// </summary>
    public bool EnableReliableTransport { get; private set; } = true;

    /// <summary>
    /// The TCP port used for reliable transport.
    /// </summary>
    public int ReliablePort { get; private set; } = DefaultPortTCP;

    /// <summary>
    /// The timeout used when establishing outbound reliable TCP connections.
    /// </summary>
    public TimeSpan ReliableConnectTimeout { get; private set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The timeout used for reliable TCP read and write operations.
    /// </summary>
    public TimeSpan ReliableOperationTimeout { get; private set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The timeout used while waiting for an acknowledgement from a reliable TCP receiver.
    /// </summary>
    public TimeSpan ReliableAcknowledgementTimeout { get; private set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Sets reliable transport behavior.
    /// </summary>
    /// <param name="enableReliableTransport">Whether the reliable TCP listener should be enabled.</param>
    /// <param name="reliablePort">Optional TCP port to use for reliable transport.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetReliableTransport(bool enableReliableTransport, int? reliablePort = null)
    {
        EnableReliableTransport = enableReliableTransport;

        if (reliablePort.HasValue)
            ReliablePort = ValidatePort(reliablePort.Value);
        else if (ReliablePort <= 0)
            ReliablePort = Port;

        RestartTransportIfRunning();
        return this;
    }

    /// <summary>
    /// Sets the reliable TCP timeouts.
    /// </summary>
    /// <param name="connectTimeout">The timeout used while connecting to a remote reliable endpoint.</param>
    /// <param name="operationTimeout">The timeout used while reading or writing a reliable payload.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetReliableTimeouts(TimeSpan connectTimeout, TimeSpan operationTimeout)
    {
        if (connectTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(connectTimeout), "Reliable connect timeout must be greater than zero.");

        if (operationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(operationTimeout), "Reliable operation timeout must be greater than zero.");

        ReliableConnectTimeout = connectTimeout;
        ReliableOperationTimeout = operationTimeout;
        return this;
    }

    /// <summary>
    /// Sets the timeout used while waiting for a reliable TCP acknowledgement.
    /// </summary>
    /// <param name="acknowledgementTimeout">The timeout used while waiting for an acknowledgement from the receiver.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetReliableAcknowledgementTimeout(TimeSpan acknowledgementTimeout)
    {
        if (acknowledgementTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(acknowledgementTimeout), "Reliable acknowledgement timeout must be greater than zero.");

        ReliableAcknowledgementTimeout = acknowledgementTimeout;
        return this;
    }

    /// <summary>
    /// The serializer settings used for network envelopes and typed payloads.
    /// </summary>
    public JsonSerializerSettings SerializerSettings { get; private set; } = CreateDefaultSerializerSettings();

    /// <summary>
    /// Replaces the serializer settings used by this relay.
    /// </summary>
    /// <param name="serializerSettings">The serializer settings to use for envelope and payload serialization.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetSerializerSettings(JsonSerializerSettings serializerSettings)
    {
        SerializerSettings = serializerSettings == null
            ? throw new ArgumentNullException(nameof(serializerSettings))
            : CloneSerializerSettings(serializerSettings);

        return this;
    }

    /// <summary>
    /// Mutates the serializer settings used by this relay.
    /// </summary>
    /// <param name="configure">The callback used to mutate a cloned settings instance.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay ConfigureSerializer(Action<JsonSerializerSettings> configure)
    {
        if (configure == null)
            throw new ArgumentNullException(nameof(configure));

        var serializerSettings = CloneSerializerSettings(SerializerSettings);
        configure(serializerSettings);
        SerializerSettings = serializerSettings;
        return this;
    }

    /// <summary>
    /// Gets the shared payload limit convenience value.<br/>
    /// When UDP and TCP limits differ, the smaller value is returned.
    /// </summary>
    public int MaxPayloadBytes => Math.Min(UdpMaxPayloadBytes, ReliableMaxPayloadBytes);

    /// <summary>
    /// The maximum allowed serialized payload size for UDP best-effort messages.
    /// </summary>
    public int UdpMaxPayloadBytes { get; private set; } = DefaultMaxPayloadBytes;

    /// <summary>
    /// The maximum allowed serialized payload size for reliable TCP messages.
    /// </summary>
    public int ReliableMaxPayloadBytes { get; private set; } = DefaultMaxPayloadBytes;

    /// <summary>
    /// Sets the maximum allowed serialized payload size for both UDP and TCP transports.
    /// </summary>
    /// <param name="maxPayloadBytes">The maximum serialized payload size in bytes.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetMaxPayloadBytes(int maxPayloadBytes)
        => SetTransportPayloadLimits(maxPayloadBytes, maxPayloadBytes);

    /// <summary>
    /// Sets the maximum allowed serialized payload size for UDP best-effort messages.
    /// </summary>
    /// <param name="maxPayloadBytes">The maximum UDP serialized payload size in bytes.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetUdpMaxPayloadBytes(int maxPayloadBytes)
    {
        UdpMaxPayloadBytes = ValidatePayloadLimit(maxPayloadBytes, nameof(maxPayloadBytes));
        return this;
    }

    /// <summary>
    /// Sets the maximum allowed serialized payload size for reliable TCP messages.
    /// </summary>
    /// <param name="maxPayloadBytes">The maximum TCP serialized payload size in bytes.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetReliableMaxPayloadBytes(int maxPayloadBytes)
    {
        ReliableMaxPayloadBytes = ValidatePayloadLimit(maxPayloadBytes, nameof(maxPayloadBytes));
        return this;
    }

    /// <summary>
    /// Sets separate maximum serialized payload sizes for UDP and TCP transports.
    /// </summary>
    /// <param name="udpMaxPayloadBytes">The maximum UDP serialized payload size in bytes.</param>
    /// <param name="reliableMaxPayloadBytes">The maximum TCP serialized payload size in bytes.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetTransportPayloadLimits(int udpMaxPayloadBytes, int reliableMaxPayloadBytes)
    {
        UdpMaxPayloadBytes = ValidatePayloadLimit(udpMaxPayloadBytes, nameof(udpMaxPayloadBytes));
        ReliableMaxPayloadBytes = ValidatePayloadLimit(reliableMaxPayloadBytes, nameof(reliableMaxPayloadBytes));
        return this;
    }

    /// <summary>
    /// The UDP receive buffer size in bytes.
    /// </summary>
    public int UdpReceiveBufferSize { get; private set; } = 256 * 1024;

    /// <summary>
    /// The UDP send buffer size in bytes.
    /// </summary>
    public int UdpSendBufferSize { get; private set; } = 256 * 1024;

    /// <summary>
    /// The reliable TCP receive buffer size in bytes.
    /// </summary>
    public int ReliableReceiveBufferSize { get; private set; } = 256 * 1024;

    /// <summary>
    /// The reliable TCP send buffer size in bytes.
    /// </summary>
    public int ReliableSendBufferSize { get; private set; } = 256 * 1024;

    /// <summary>
    /// Sets the underlying socket buffer sizes for both UDP and TCP transports.
    /// </summary>
    /// <param name="receiveBufferSize">The receive buffer size in bytes.</param>
    /// <param name="sendBufferSize">The send buffer size in bytes.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetSocketBuffers(int receiveBufferSize, int sendBufferSize)
        => SetTransportSocketBuffers(receiveBufferSize, sendBufferSize, receiveBufferSize, sendBufferSize);

    /// <summary>
    /// Sets the UDP socket buffer sizes.
    /// </summary>
    /// <param name="receiveBufferSize">The UDP receive buffer size in bytes.</param>
    /// <param name="sendBufferSize">The UDP send buffer size in bytes.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetUdpSocketBuffers(int receiveBufferSize, int sendBufferSize)
    {
        UdpReceiveBufferSize = ValidateBufferSize(receiveBufferSize, nameof(receiveBufferSize));
        UdpSendBufferSize = ValidateBufferSize(sendBufferSize, nameof(sendBufferSize));
        RestartTransportIfRunning();
        return this;
    }

    /// <summary>
    /// Sets the reliable TCP socket buffer sizes.
    /// </summary>
    /// <param name="receiveBufferSize">The TCP receive buffer size in bytes.</param>
    /// <param name="sendBufferSize">The TCP send buffer size in bytes.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetReliableSocketBuffers(int receiveBufferSize, int sendBufferSize)
    {
        ReliableReceiveBufferSize = ValidateBufferSize(receiveBufferSize, nameof(receiveBufferSize));
        ReliableSendBufferSize = ValidateBufferSize(sendBufferSize, nameof(sendBufferSize));
        RestartTransportIfRunning();
        return this;
    }

    /// <summary>
    /// Sets separate socket buffer sizes for UDP and TCP transports.
    /// </summary>
    /// <param name="udpReceiveBufferSize">The UDP receive buffer size in bytes.</param>
    /// <param name="udpSendBufferSize">The UDP send buffer size in bytes.</param>
    /// <param name="reliableReceiveBufferSize">The TCP receive buffer size in bytes.</param>
    /// <param name="reliableSendBufferSize">The TCP send buffer size in bytes.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetTransportSocketBuffers(int udpReceiveBufferSize, int udpSendBufferSize, int reliableReceiveBufferSize, int reliableSendBufferSize)
    {
        UdpReceiveBufferSize = ValidateBufferSize(udpReceiveBufferSize, nameof(udpReceiveBufferSize));
        UdpSendBufferSize = ValidateBufferSize(udpSendBufferSize, nameof(udpSendBufferSize));
        ReliableReceiveBufferSize = ValidateBufferSize(reliableReceiveBufferSize, nameof(reliableReceiveBufferSize));
        ReliableSendBufferSize = ValidateBufferSize(reliableSendBufferSize, nameof(reliableSendBufferSize));
        RestartTransportIfRunning();
        return this;
    }

    /// <summary>
    /// The TTL used for outgoing UDP packets.
    /// </summary>
    public short TimeToLive { get; private set; } = 1;

    /// <summary>
    /// Sets the TTL used for outgoing UDP packets.
    /// </summary>
    /// <param name="timeToLive">The TTL value to apply to outgoing packets.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetTimeToLive(short timeToLive)
    {
        if (timeToLive <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeToLive), "TTL must be greater than zero.");

        TimeToLive = timeToLive;
        RestartTransportIfRunning();
        return this;
    }

    /// <summary>
    /// How long a discovered peer remains active without receiving a new announcement or message.
    /// </summary>
    public TimeSpan PeerExpiration { get; private set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Sets the peer expiration timeout.
    /// </summary>
    /// <param name="peerExpiration">The duration after which inactive dynamic peers are expired.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetPeerExpiration(TimeSpan peerExpiration)
    {
        PeerExpiration = peerExpiration < TimeSpan.Zero ? TimeSpan.Zero : peerExpiration;
        return this;
    }

    /// <summary>
    /// The interval between periodic presence announcements while active.<br/>
    /// Set to <see cref="TimeSpan.Zero"/> to disable periodic announcements.
    /// </summary>
    public TimeSpan AnnouncementInterval { get; private set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Sets the periodic announcement interval.
    /// </summary>
    /// <param name="announcementInterval">The interval between presence announcements while active.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetAnnouncementInterval(TimeSpan announcementInterval)
    {
        AnnouncementInterval = announcementInterval < TimeSpan.Zero ? TimeSpan.Zero : announcementInterval;
        RestartTransportIfRunning();
        return this;
    }

    /// <summary>
    /// Replaces the allow-list of accepted peer IDs.<br/>
    /// Passing <see langword="null"/> or an empty collection disables the allow-list.
    /// </summary>
    /// <param name="peerIds">The peer IDs to allow, or <see langword="null"/> to clear the allow-list.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireNetworkRelay SetAllowedPeerIds(IEnumerable<string>? peerIds)
    {
        lock (peerLock)
        {
            allowedPeerIds.Clear();

            if (peerIds == null)
                return this;

            foreach (var peerId in peerIds.Where(id => !string.IsNullOrWhiteSpace(id)))
                allowedPeerIds.Add(peerId.Trim());
        }

        return this;
    }

    #endregion

    #region Private Helpers

    private static JsonSerializerSettings CreateDefaultSerializerSettings()
        => new()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
            DateParseHandling = DateParseHandling.DateTimeOffset,
            TypeNameHandling = TypeNameHandling.None,
        };

    private static JsonSerializerSettings CloneSerializerSettings(JsonSerializerSettings serializerSettings)
        => new()
        {
            ContractResolver = serializerSettings.ContractResolver,
            NullValueHandling = serializerSettings.NullValueHandling,
            Formatting = serializerSettings.Formatting,
            DateParseHandling = serializerSettings.DateParseHandling,
            DateFormatHandling = serializerSettings.DateFormatHandling,
            DateTimeZoneHandling = serializerSettings.DateTimeZoneHandling,
            FloatFormatHandling = serializerSettings.FloatFormatHandling,
            FloatParseHandling = serializerSettings.FloatParseHandling,
            DefaultValueHandling = serializerSettings.DefaultValueHandling,
            MissingMemberHandling = serializerSettings.MissingMemberHandling,
            MetadataPropertyHandling = serializerSettings.MetadataPropertyHandling,
            ObjectCreationHandling = serializerSettings.ObjectCreationHandling,
            ConstructorHandling = serializerSettings.ConstructorHandling,
            Culture = serializerSettings.Culture,
            ReferenceLoopHandling = serializerSettings.ReferenceLoopHandling,
            StringEscapeHandling = serializerSettings.StringEscapeHandling,
            TypeNameAssemblyFormatHandling = serializerSettings.TypeNameAssemblyFormatHandling,
            TypeNameHandling = serializerSettings.TypeNameHandling,
        };

    private JsonSerializer CreateJsonSerializer()
        => JsonSerializer.CreateDefault(SerializerSettings);

    private string BuildDefaultInstanceId()
    {
        var moduleIdentifier = !string.IsNullOrWhiteSpace(ModuleId)
            ? ModuleId
            : GetType().Name;

        return $"{Environment.MachineName}-{moduleIdentifier}-{InstanceCounter}";
    }

    private static int ValidatePort(int port)
    {
        if (port is <= 0 or > 65_535)
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");

        return port;
    }

    private static int ValidatePayloadLimit(int maxPayloadBytes, string paramName)
    {
        if (maxPayloadBytes <= 0)
            throw new ArgumentOutOfRangeException(paramName, "Maximum payload size must be greater than zero.");

        return maxPayloadBytes;
    }

    private static int ValidateBufferSize(int bufferSize, string paramName)
    {
        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(paramName, "Socket buffer size must be greater than zero.");

        return bufferSize;
    }

    private string NormalizeChannel(string? channel)
    {
        var resolvedChannel = string.IsNullOrWhiteSpace(channel) ? DefaultChannel : channel.Trim();

        if (string.IsNullOrWhiteSpace(resolvedChannel))
            throw new ArgumentException("Channel cannot be empty.", nameof(channel));

        return resolvedChannel;
    }

    private static string NormalizeSubscriptionChannel(string? channel)
    {
        var resolvedChannel = string.IsNullOrWhiteSpace(channel) ? WildcardChannel : channel.Trim();

        if (string.IsNullOrWhiteSpace(resolvedChannel))
            throw new ArgumentException("Channel cannot be empty.", nameof(channel));

        return resolvedChannel;
    }

    private void EnsureCanSend()
    {
        if (!IsSelfRegistered)
            throw new InvalidOperationException("The local relay instance must be registered before sending. Call RegisterSelf() first.");

        if (!IsSelfActive)
            throw new InvalidOperationException("The local relay instance must be active before sending. Call ActivateSelf() or RegisterSelf() with activateSelf: true.");

        if (IsActive)
            return;

        if (AutoActivateOnSend)
        {
            Activate();
            return;
        }

        throw new InvalidOperationException("NetworkRelay is not active.");
    }

    private void EnsureReliableTransportEnabled()
    {
        if (!EnableReliableTransport)
            throw new InvalidOperationException("Reliable TCP transport is disabled for this relay.");
    }

    #endregion
}
