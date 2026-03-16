using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NoireLib.Enums;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.NetworkRelay;

public partial class NoireNetworkRelay
{
    private void RestartTransportIfRunning()
    {
        if (!IsActive)
            return;

        StopTransport();
        StartTransport();
    }

    private void StartTransport()
    {
        try
        {
            lock (transportLock)
            {
                if (udpClient != null)
                    return;

                if (EnableBroadcast && BindAddress.AddressFamily == AddressFamily.InterNetworkV6)
                    throw new NotSupportedException("UDP broadcast is only supported with IPv4 bind addresses.");

                var client = new UdpClient(BindAddress.AddressFamily);
                client.ExclusiveAddressUse = false;
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                client.Client.ReceiveBufferSize = UdpReceiveBufferSize;
                client.Client.SendBufferSize = UdpSendBufferSize;
                client.EnableBroadcast = EnableBroadcast;
                client.Ttl = TimeToLive;
                client.Client.Bind(new IPEndPoint(BindAddress, Port));

                udpClient = client;
                transportCts = new CancellationTokenSource();
                receiveLoopTask = Task.Run(() => ReceiveLoopAsync(transportCts.Token), transportCts.Token);

                if (EnableReliableTransport)
                {
                    var listener = new TcpListener(BindAddress, ReliablePort);
                    listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    listener.Server.ReceiveBufferSize = ReliableReceiveBufferSize;
                    listener.Server.SendBufferSize = ReliableSendBufferSize;
                    listener.Server.NoDelay = true;
                    listener.Start();

                    tcpListener = listener;
                    tcpAcceptLoopTask = Task.Run(() => AcceptReliableClientsAsync(transportCts.Token), transportCts.Token);
                }

                if (EnablePeerDiscovery && AnnouncementInterval > TimeSpan.Zero)
                    announcementLoopTask = Task.Run(() => AnnouncementLoopAsync(transportCts.Token), transportCts.Token);
            }
        }
        catch (Exception ex)
        {
            HandleException(ex, "starting relay transport");
        }
    }

    private void StopTransport()
    {
        CancellationTokenSource? cts;
        UdpClient? client;
        TcpListener? listener;

        lock (transportLock)
        {
            cts = transportCts;
            client = udpClient;
            listener = tcpListener;

            transportCts = null;
            udpClient = null;
            tcpListener = null;
            receiveLoopTask = null;
            tcpAcceptLoopTask = null;
            announcementLoopTask = null;
        }

        try
        {
            cts?.Cancel();
        }
        catch
        {
        }

        try
        {
            client?.Dispose();
        }
        catch
        {
        }

        try
        {
            listener?.Stop();
        }
        catch
        {
        }

        cts?.Dispose();
    }

    private async Task AnnouncementLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(AnnouncementInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                SweepExpiredPeers();
                AnnouncePresence();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref totalSendFailures);
                HandleException(ex, "sending relay presence announcement");
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                var client = GetClientOrThrow();
                received = await client.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (ex is InvalidOperationException && IsUdpTransportUnavailable())
                    break;

                Interlocked.Increment(ref totalReceiveFailures);
                HandleException(ex, "receiving relay datagram");
                continue;
            }

            try
            {
                await ProcessIncomingBufferAsync(received.Buffer, received.RemoteEndPoint, NetworkRelayTransportKind.Udp);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref totalReceiveFailures);
                HandleException(ex, "processing relay datagram");
            }
        }
    }

    private async Task AcceptReliableClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                var listener = GetReliableListenerOrThrow();
                client = await listener.AcceptTcpClientAsync(cancellationToken);
                Interlocked.Increment(ref totalReliableConnectionsAccepted);
                _ = Task.Run(() => ProcessReliableClientAsync(client, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                client?.Dispose();
                break;
            }
            catch (ObjectDisposedException)
            {
                client?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                if (ex is InvalidOperationException && IsReliableTransportUnavailable())
                {
                    client?.Dispose();
                    break;
                }

                client?.Dispose();
                Interlocked.Increment(ref totalReceiveFailures);
                HandleException(ex, "accepting reliable relay connection");
            }
        }
    }

    private async Task ProcessReliableClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var ownedClient = client;
            using var stream = ownedClient.GetStream();

            if (ownedClient.Client.RemoteEndPoint is not IPEndPoint remoteEndPoint)
                return;

            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] lengthBuffer = new byte[sizeof(int)];
                try
                {
                    using var headerTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    headerTimeoutCts.CancelAfter(ReliableOperationTimeout);
                    await stream.ReadExactlyAsync(lengthBuffer, headerTimeoutCts.Token);
                }
                catch (EndOfStreamException)
                {
                    break;
                }

                var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
                if (length <= 0 || length > ReliableMaxPayloadBytes)
                    throw new InvalidOperationException($"Received reliable relay payload length {length} exceeds the configured TCP maximum of {ReliableMaxPayloadBytes} bytes.");

                var buffer = GC.AllocateUninitializedArray<byte>(length);
                using (var payloadTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    payloadTimeoutCts.CancelAfter(ReliableOperationTimeout);
                    await stream.ReadExactlyAsync(buffer, payloadTimeoutCts.Token);
                }

                var envelope = DeserializeEnvelope(buffer);
                await ProcessIncomingBufferAsync(buffer, envelope, remoteEndPoint, NetworkRelayTransportKind.Tcp, stream, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref totalReceiveFailures);
            HandleException(ex, "processing reliable relay connection");
        }
    }

    private async Task ProcessIncomingBufferAsync(
        byte[] buffer,
        IPEndPoint remoteEndPoint,
        NetworkRelayTransportKind transportKind)
        => await ProcessIncomingBufferAsync(buffer, null, remoteEndPoint, transportKind, null, CancellationToken.None);

    private async Task ProcessIncomingBufferAsync(
        byte[] buffer,
        RelayEnvelope? envelope,
        IPEndPoint remoteEndPoint,
        NetworkRelayTransportKind transportKind,
        NetworkStream? acknowledgementStream,
        CancellationToken cancellationToken)
    {
        if (buffer.Length == 0)
            return;

        if (transportKind == NetworkRelayTransportKind.Udp && buffer.Length > UdpMaxPayloadBytes)
        {
            Interlocked.Increment(ref totalMessagesDropped);
            HandleException(new InvalidOperationException($"Received UDP relay payload size {buffer.Length} exceeds the configured UDP maximum of {UdpMaxPayloadBytes} bytes."), "receiving relay datagram");
            return;
        }

        Interlocked.Increment(ref totalMessagesReceived);
        Interlocked.Add(ref totalBytesReceived, buffer.Length);

        if (transportKind == NetworkRelayTransportKind.Tcp)
            Interlocked.Increment(ref totalReliableMessagesReceived);
        else
            Interlocked.Increment(ref totalBestEffortMessagesReceived);

        try
        {
            envelope ??= DeserializeEnvelope(buffer);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref totalMessagesDropped);
            HandleException(ex, "deserializing relay payload");
            return;
        }

        if (envelope == null || string.IsNullOrWhiteSpace(envelope.MessageId) || string.IsNullOrWhiteSpace(envelope.SenderId))
        {
            Interlocked.Increment(ref totalMessagesDropped);
            return;
        }

        if (!AllowLoopbackMessages && string.Equals(envelope.SenderId, InstanceId, StringComparison.OrdinalIgnoreCase))
            return;

        if (!IsPeerAllowed(envelope.SenderId))
        {
            Interlocked.Increment(ref totalMessagesDropped);
            await TrySendAcknowledgementAsync(acknowledgementStream, envelope, success: false, "Sender peer is not allowed.", cancellationToken);
            return;
        }

        if (SuppressDuplicateMessages && IsDuplicateMessage(envelope.MessageId))
        {
            Interlocked.Increment(ref totalMessagesDropped);
            Interlocked.Increment(ref totalDuplicateMessagesDropped);
            await TrySendAcknowledgementAsync(acknowledgementStream, envelope, success: false, "Duplicate message suppressed.", cancellationToken);
            return;
        }

        if (EnablePeerDiscovery)
            HandlePeerActivity(envelope, remoteEndPoint, transportKind);

        if (string.Equals(envelope.Kind, EnvelopeKindHello, StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref totalPeerAnnouncementsReceived);
            return;
        }

        if (string.Equals(envelope.Kind, EnvelopeKindAcknowledgement, StringComparison.OrdinalIgnoreCase))
            return;

        if (!IsMessageTargetedToThisInstance(envelope))
        {
            await TrySendAcknowledgementAsync(acknowledgementStream, envelope, success: false, "Message target did not match this relay instance.", cancellationToken);
            return;
        }

        var message = new NetworkRelayMessage(
            MessageId: envelope.MessageId,
            Channel: NormalizeChannel(envelope.Channel),
            SenderId: envelope.SenderId,
            SenderDisplayName: envelope.SenderDisplayName,
            MessageType: envelope.MessageType,
            Payload: envelope.Payload?.DeepClone() ?? JValue.CreateNull(),
            SentAtUtc: envelope.SentAtUtc,
            RemoteEndPoint: remoteEndPoint,
            TargetPeerId: envelope.TargetPeerId,
            TargetPeerIds: envelope.TargetPeerIds?.ToArray() ?? [],
            TransportKind: transportKind);

        try
        {
            RaiseMessageReceived(message);
            await DispatchMessageAsync(message);
            await TrySendAcknowledgementAsync(acknowledgementStream, envelope, success: true, null, cancellationToken);
        }
        catch (Exception ex)
        {
            await TrySendAcknowledgementAsync(acknowledgementStream, envelope, success: false, ex.Message, cancellationToken);

            if (ShouldRethrowException())
                ExceptionDispatchInfo.Capture(ex).Throw();
        }
    }

    private bool IsMessageTargetedToThisInstance(RelayEnvelope envelope)
    {
        if (!string.IsNullOrWhiteSpace(envelope.TargetPeerId))
            return string.Equals(envelope.TargetPeerId, InstanceId, StringComparison.OrdinalIgnoreCase);

        if (envelope.TargetPeerIds == null || envelope.TargetPeerIds.Count == 0)
            return true;

        return envelope.TargetPeerIds.Any(peerId => string.Equals(peerId, InstanceId, StringComparison.OrdinalIgnoreCase));
    }

    private void SendHelloEnvelope(RelayEnvelope envelope, IPEndPoint endPoint)
    {
        SendEnvelopeInternal(envelope, endPoint, NetworkRelayDeliveryMode.BestEffort, countAsMessage: false);
    }

    private void SendEnvelope(RelayEnvelope envelope, IPEndPoint endPoint, NetworkRelayDeliveryMode deliveryMode)
    {
        SendEnvelopeInternal(envelope, endPoint, deliveryMode, countAsMessage: true);
    }

    private void SendEnvelopeInternal(RelayEnvelope envelope, IPEndPoint endPoint, NetworkRelayDeliveryMode deliveryMode, bool countAsMessage)
    {
        byte[] buffer;
        try
        {
            buffer = SerializeEnvelope(envelope, deliveryMode);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref totalSendFailures);
            HandleException(ex, "serializing relay envelope");
            return;
        }

        try
        {
            if (deliveryMode == NetworkRelayDeliveryMode.Reliable)
                QueueReliableBufferSend(buffer, endPoint, envelope, countAsMessage);
            else
                SendBestEffortBuffer(buffer, endPoint, envelope, countAsMessage);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref totalSendFailures);
            HandleException(ex, $"sending relay envelope to {endPoint}");
        }
    }

    private void QueueReliableBufferSend(byte[] buffer, IPEndPoint endPoint, RelayEnvelope envelope, bool countAsMessage)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await SendReliableBufferAsync(buffer, endPoint, envelope, countAsMessage);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref totalSendFailures);
                HandleException(ex, $"sending relay envelope to {endPoint}");
            }
        });
    }

    private async Task<NetworkRelaySendReceipt> SendReliableEnvelopeWithAcknowledgementAsync(
        RelayEnvelope envelope,
        IPEndPoint endPoint,
        TimeSpan acknowledgementTimeout,
        CancellationToken cancellationToken)
    {
        var buffer = SerializeEnvelope(envelope, NetworkRelayDeliveryMode.Reliable);

        using var client = new TcpClient(endPoint.AddressFamily);
        client.NoDelay = true;
        client.ReceiveBufferSize = ReliableReceiveBufferSize;
        client.SendBufferSize = ReliableSendBufferSize;

        using (var connectTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            connectTimeoutCts.CancelAfter(ReliableConnectTimeout);
            await client.ConnectAsync(endPoint.Address, endPoint.Port, connectTimeoutCts.Token);
        }

        using var stream = client.GetStream();
        await WriteReliableFrameAsync(stream, buffer, ReliableOperationTimeout, cancellationToken);

        Interlocked.Increment(ref totalMessagesSent);
        Interlocked.Increment(ref totalReliableMessagesSent);
        Interlocked.Add(ref totalBytesSent, buffer.Length);

        if (EnableLogging)
            NoireLogger.LogVerbose(this, $"Sent TCP relay {envelope.Kind} '{envelope.Channel}' to {endPoint} ({buffer.Length} bytes) awaiting acknowledgement.");

        var acknowledgementEnvelope = await ReadReliableEnvelopeAsync(stream, acknowledgementTimeout, cancellationToken);
        if (!string.Equals(acknowledgementEnvelope.Kind, EnvelopeKindAcknowledgement, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Reliable send acknowledgement for message '{envelope.MessageId}' was invalid.");

        if (!string.Equals(acknowledgementEnvelope.CorrelationMessageId, envelope.MessageId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Reliable send acknowledgement correlation mismatch for message '{envelope.MessageId}'.");

        var acknowledgement = acknowledgementEnvelope.Payload?.ToObject<RelayAcknowledgementPayload>(CreateJsonSerializer())
            ?? throw new InvalidOperationException($"Reliable send acknowledgement for message '{envelope.MessageId}' was missing its payload.");

        if (!acknowledgement.Success)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(acknowledgement.ErrorMessage)
                ? $"Reliable send acknowledgement for message '{envelope.MessageId}' reported a failure."
                : acknowledgement.ErrorMessage);

        return new NetworkRelaySendReceipt(
            envelope.MessageId,
            envelope.Channel,
            envelope.TargetPeerId,
            endPoint,
            envelope.SentAtUtc,
            acknowledgement.AcknowledgedAtUtc);
    }

    private RelayEnvelope DeserializeEnvelope(byte[] buffer)
        => JsonConvert.DeserializeObject<RelayEnvelope>(Encoding.UTF8.GetString(buffer), SerializerSettings)
           ?? throw new InvalidOperationException("Relay payload deserialized to null.");

    private RelayEnvelope CreateAcknowledgementEnvelope(RelayEnvelope envelope, bool success, string? errorMessage)
        => new()
        {
            Kind = EnvelopeKindAcknowledgement,
            Channel = envelope.Channel,
            MessageId = Guid.NewGuid().ToString("N"),
            CorrelationMessageId = envelope.MessageId,
            SenderId = InstanceId,
            SenderDisplayName = DisplayName,
            SenderReliablePort = EnableReliableTransport ? ReliablePort : null,
            MessageType = typeof(RelayAcknowledgementPayload).FullName ?? nameof(RelayAcknowledgementPayload),
            SentAtUtc = DateTimeOffset.UtcNow,
            TargetPeerId = envelope.SenderId,
            Payload = CreatePayloadToken(new RelayAcknowledgementPayload(envelope.MessageId, success, errorMessage, DateTimeOffset.UtcNow)),
        };

    private async Task TrySendAcknowledgementAsync(NetworkStream? acknowledgementStream, RelayEnvelope envelope, bool success, string? errorMessage, CancellationToken cancellationToken)
    {
        if (acknowledgementStream == null || !envelope.RequiresAcknowledgement)
            return;

        var acknowledgementEnvelope = CreateAcknowledgementEnvelope(envelope, success, errorMessage);
        var acknowledgementBuffer = SerializeEnvelope(acknowledgementEnvelope, NetworkRelayDeliveryMode.Reliable);
        await WriteReliableFrameAsync(acknowledgementStream, acknowledgementBuffer, ReliableOperationTimeout, cancellationToken);
    }

    private async Task<RelayEnvelope> ReadReliableEnvelopeAsync(NetworkStream stream, TimeSpan timeout, CancellationToken cancellationToken)
    {
        byte[] lengthBuffer = new byte[sizeof(int)];
        using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeoutCts.CancelAfter(timeout);
            await stream.ReadExactlyAsync(lengthBuffer, timeoutCts.Token);
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
        if (length <= 0 || length > ReliableMaxPayloadBytes)
            throw new InvalidOperationException($"Received reliable relay payload length {length} exceeds the configured TCP maximum of {ReliableMaxPayloadBytes} bytes.");

        var buffer = GC.AllocateUninitializedArray<byte>(length);
        using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeoutCts.CancelAfter(timeout);
            await stream.ReadExactlyAsync(buffer, timeoutCts.Token);
        }

        return DeserializeEnvelope(buffer);
    }

    private static async Task WriteReliableFrameAsync(NetworkStream stream, byte[] buffer, TimeSpan timeout, CancellationToken cancellationToken)
    {
        byte[] lengthBuffer = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, buffer.Length);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        await stream.WriteAsync(lengthBuffer, timeoutCts.Token);
        await stream.WriteAsync(buffer, timeoutCts.Token);
        await stream.FlushAsync(timeoutCts.Token);
    }

    private byte[] SerializeEnvelope(RelayEnvelope envelope, NetworkRelayDeliveryMode deliveryMode)
    {
        var buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope, SerializerSettings));
        var maxPayloadBytes = deliveryMode == NetworkRelayDeliveryMode.Reliable ? ReliableMaxPayloadBytes : UdpMaxPayloadBytes;
        var transportName = deliveryMode == NetworkRelayDeliveryMode.Reliable ? "TCP" : "UDP";
        if (buffer.Length > maxPayloadBytes)
            throw new InvalidOperationException($"Serialized payload size {buffer.Length} exceeds the configured {transportName} maximum of {maxPayloadBytes} bytes.");

        return buffer;
    }

    private void SendBestEffortBuffer(byte[] buffer, IPEndPoint endPoint, RelayEnvelope envelope, bool countAsMessage)
    {
        var client = GetClientOrThrow();
        client.Send(buffer, buffer.Length, endPoint);

        if (countAsMessage)
        {
            Interlocked.Increment(ref totalMessagesSent);
            Interlocked.Increment(ref totalBestEffortMessagesSent);
        }

        Interlocked.Add(ref totalBytesSent, buffer.Length);

        if (EnableLogging)
            NoireLogger.LogVerbose(this, $"Sent UDP relay {envelope.Kind} '{envelope.Channel}' to {endPoint} ({buffer.Length} bytes).");
    }

    private async Task SendReliableBufferAsync(byte[] buffer, IPEndPoint endPoint, RelayEnvelope envelope, bool countAsMessage)
    {
        EnsureReliableTransportEnabled();

        using var client = new TcpClient(endPoint.AddressFamily);
        client.NoDelay = true;
        client.ReceiveBufferSize = ReliableReceiveBufferSize;
        client.SendBufferSize = ReliableSendBufferSize;

        using (var connectTimeoutCts = new CancellationTokenSource(ReliableConnectTimeout))
            await client.ConnectAsync(endPoint.Address, endPoint.Port, connectTimeoutCts.Token);

        using var stream = client.GetStream();
        byte[] lengthBuffer = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, buffer.Length);

        using (var sendTimeoutCts = new CancellationTokenSource(ReliableOperationTimeout))
        {
            await stream.WriteAsync(lengthBuffer, sendTimeoutCts.Token);
            await stream.WriteAsync(buffer, sendTimeoutCts.Token);
            await stream.FlushAsync(sendTimeoutCts.Token);
        }

        if (countAsMessage)
        {
            Interlocked.Increment(ref totalMessagesSent);
            Interlocked.Increment(ref totalReliableMessagesSent);
        }

        Interlocked.Add(ref totalBytesSent, buffer.Length);

        if (EnableLogging)
            NoireLogger.LogVerbose(this, $"Sent TCP relay {envelope.Kind} '{envelope.Channel}' to {endPoint} ({buffer.Length} bytes).");
    }

    private UdpClient GetClientOrThrow()
    {
        lock (transportLock)
            return udpClient ?? throw new InvalidOperationException("NetworkRelay UDP transport is not started.");
    }

    private TcpListener GetReliableListenerOrThrow()
    {
        lock (transportLock)
            return tcpListener ?? throw new InvalidOperationException("NetworkRelay reliable transport is not started.");
    }

    private bool IsUdpTransportUnavailable()
    {
        lock (transportLock)
            return udpClient == null;
    }

    private bool IsReliableTransportUnavailable()
    {
        lock (transportLock)
            return tcpListener == null;
    }

    private bool IsDuplicateMessage(string messageId)
    {
        lock (peerLock)
        {
            CleanupRecentMessageCache();

            if (recentMessageCache.ContainsKey(messageId))
                return true;

            recentMessageCache[messageId] = DateTimeOffset.UtcNow;
            return false;
        }
    }

    private void CleanupRecentMessageCache()
    {
        if (DuplicateMessageWindow == TimeSpan.Zero)
        {
            recentMessageCache.Clear();
            return;
        }

        var cutoff = DateTimeOffset.UtcNow - DuplicateMessageWindow;
        foreach (var messageId in recentMessageCache.Where(entry => entry.Value < cutoff).Select(entry => entry.Key).ToList())
            recentMessageCache.Remove(messageId);
    }

    private void ReportError(Exception ex, string operation)
    {
        Interlocked.Increment(ref totalExceptionsCaught);

        var error = new NetworkRelayError(operation, ex, DateTimeOffset.UtcNow);
        try
        {
            Error?.Invoke(error);
        }
        catch
        {
        }

        PublishIntegrationEvent(new NetworkRelayErrorEvent(error));

        if (ExceptionHandling is ExceptionBehavior.LogAndContinue or ExceptionBehavior.LogAndThrow)
            NoireLogger.LogError(this, ex, $"Exception while {operation}");
    }

    private void HandleException(Exception ex, string operation)
    {
        ReportError(ex, operation);

        switch (ExceptionHandling)
        {
            case ExceptionBehavior.LogAndContinue:
                break;
            case ExceptionBehavior.LogAndThrow:
                ExceptionDispatchInfo.Capture(ex).Throw();
                break;
            case ExceptionBehavior.Throw:
                ExceptionDispatchInfo.Capture(ex).Throw();
                break;
            case ExceptionBehavior.Suppress:
                break;
        }
    }

    private T HandleExceptionOrReturn<T>(Exception ex, string operation, T fallback)
    {
        HandleException(ex, operation);
        return fallback;
    }

    private bool ShouldRethrowException()
        => ExceptionHandling is ExceptionBehavior.LogAndThrow or ExceptionBehavior.Throw;

    private static IPAddress ResolveAddress(string hostOrAddress, AddressFamily addressFamily)
    {
        if (IPAddress.TryParse(hostOrAddress, out var parsed))
            return parsed;

        var match = Dns.GetHostAddresses(hostOrAddress)
            .FirstOrDefault(address => address.AddressFamily == addressFamily);

        if (match == null)
            throw new InvalidOperationException($"Could not resolve host '{hostOrAddress}' for address family {addressFamily}.");

        return match;
    }
}
