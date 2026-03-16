using FluentAssertions;
using Newtonsoft.Json.Linq;
using NoireLib.NetworkRelay;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for the <see cref="NoireNetworkRelay"/> module.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireNetworkRelayTests
{
    [Fact]
    public void RegisterPeer_ShouldStoreReliableEndpoint_WhenExplicitReliablePortIsProvided()
    {
        var relay = new NoireNetworkRelay(active: false, enableLogging: false);

        relay.RegisterPeer("peer-1", "127.0.0.1", 55100, 55101, "Peer 1");

        var found = relay.TryGetPeer("peer-1", out var peer);

        found.Should().BeTrue();
        peer.Should().NotBeNull();
        peer!.EndPoint.Port.Should().Be(55100);
        peer.ReliableEndPoint.Should().NotBeNull();
        peer.ReliableEndPoint!.Port.Should().Be(55101);
    }

    [Fact]
    public async Task ActiveRelay_ShouldNotRegisterDefaultSelfPeer_WithoutExplicitSelfRegistration()
    {
        var udpPort = GetFreePort();
        var relay = new NoireNetworkRelay(active: true, enableLogging: false, port: udpPort, enableReliableTransport: false, enablePeerDiscovery: true, autoAnnounceOnStart: true);

        try
        {
            await Task.Delay(250);

            relay.IsSelfRegistered.Should().BeFalse();
            relay.GetPeers().Should().BeEmpty();
        }
        finally
        {
            relay.Stop();
        }
    }

    [Fact]
    public async Task RegisterSelf_WithActivateSelf_ShouldImmediatelyRefreshKnownPeerLastSeen()
    {
        var peer1Port = GetFreePort();
        var peer2Port = GetFreePort();

        var relay1 = new NoireNetworkRelay(active: true, enableLogging: false, port: peer1Port, enableBroadcast: false, enableReliableTransport: false, enablePeerDiscovery: true);
        var relay2 = new NoireNetworkRelay(active: true, enableLogging: false, port: peer2Port, enableBroadcast: false, enableReliableTransport: false, enablePeerDiscovery: true);
        var relay1SawPeer2 = new TaskCompletionSource<NetworkRelayPeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var relay2SawPeer1 = new TaskCompletionSource<NetworkRelayPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            relay1.RegisterPeer("peer-2", "127.0.0.1", peer2Port, "Peer 2");
            relay2.RegisterPeer("peer-1", "127.0.0.1", peer1Port, "Peer 1");

            relay1.PeerSeen += peer =>
            {
                if (string.Equals(peer.PeerId, "peer-2", StringComparison.OrdinalIgnoreCase))
                    relay1SawPeer2.TrySetResult(peer);
            };

            relay2.PeerSeen += peer =>
            {
                if (string.Equals(peer.PeerId, "peer-1", StringComparison.OrdinalIgnoreCase))
                    relay2SawPeer1.TrySetResult(peer);
            };

            relay1.RegisterSelf("peer-1", "Peer 1", activateSelf: true);
            relay2.RegisterSelf("peer-2", "Peer 2", activateSelf: true);

            var relay1RemotePeer = await WaitWithStatsAsync(relay1SawPeer2.Task, relay1, relay2, "relay1 waiting for peer-2");
            var relay2RemotePeer = await WaitWithStatsAsync(relay2SawPeer1.Task, relay2, relay1, "relay2 waiting for peer-1");

            relay1RemotePeer.EndPoint.Port.Should().Be(peer2Port);
            relay2RemotePeer.EndPoint.Port.Should().Be(peer1Port);
            relay1.IsSelfActive.Should().BeTrue();
            relay2.IsSelfActive.Should().BeTrue();
        }
        finally
        {
            relay1.Stop();
            relay2.Stop();
        }
    }

    [Fact]
    public void ToTyped_ShouldPreserveReliableTransportMetadata()
    {
        var message = new NetworkRelayMessage(
            MessageId: "msg-1",
            Channel: "control",
            SenderId: "peer-1",
            SenderDisplayName: "Peer 1",
            MessageType: typeof(int).FullName,
            Payload: JToken.FromObject(42),
            SentAtUtc: DateTimeOffset.UtcNow,
            RemoteEndPoint: new IPEndPoint(IPAddress.Loopback, 55300),
            TargetPeerId: null,
            TargetPeerIds: [],
            TransportKind: NetworkRelayTransportKind.Tcp);

        var typed = message.ToTyped<int>();

        typed.Payload.Should().Be(42);
        typed.TransportKind.Should().Be(NetworkRelayTransportKind.Tcp);
        typed.IsReliable.Should().BeTrue();
    }

    [Fact]
    public void SetMaxPayloadBytes_ShouldApplyToUdpAndReliableLimits()
    {
        var relay = new NoireNetworkRelay(active: false, enableLogging: false);

        relay.SetMaxPayloadBytes(1234);

        relay.UdpMaxPayloadBytes.Should().Be(1234);
        relay.ReliableMaxPayloadBytes.Should().Be(1234);
        relay.MaxPayloadBytes.Should().Be(1234);
    }

    [Fact]
    public void SetTransportPayloadLimits_ShouldAllowUdpAndReliableToDiffer()
    {
        var relay = new NoireNetworkRelay(active: false, enableLogging: false);

        relay.SetTransportPayloadLimits(1200, 32000);

        relay.UdpMaxPayloadBytes.Should().Be(1200);
        relay.ReliableMaxPayloadBytes.Should().Be(32000);
        relay.MaxPayloadBytes.Should().Be(1200);
    }

    [Fact]
    public void SetTransportSocketBuffers_ShouldAllowUdpAndReliableToDiffer()
    {
        var relay = new NoireNetworkRelay(active: false, enableLogging: false);

        relay.SetTransportSocketBuffers(8 * 1024, 16 * 1024, 32 * 1024, 64 * 1024);

        relay.UdpReceiveBufferSize.Should().Be(8 * 1024);
        relay.UdpSendBufferSize.Should().Be(16 * 1024);
        relay.ReliableReceiveBufferSize.Should().Be(32 * 1024);
        relay.ReliableSendBufferSize.Should().Be(64 * 1024);
    }

    [Fact]
    public void SerializeEnvelope_ShouldUseUdpPayloadLimit()
    {
        var relay = new NoireNetworkRelay(active: false, enableLogging: false);
        relay.SetTransportPayloadLimits(1, 32000);

        var envelope = CreateMessageEnvelope(relay, "hello");
        var act = () => SerializeEnvelope(relay, envelope, NetworkRelayDeliveryMode.BestEffort);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*UDP maximum*");
    }

    [Fact]
    public void SerializeEnvelope_ShouldUseReliablePayloadLimit()
    {
        var relay = new NoireNetworkRelay(active: false, enableLogging: false);
        relay.SetTransportPayloadLimits(32000, 1);

        var envelope = CreateMessageEnvelope(relay, "hello");
        var act = () => SerializeEnvelope(relay, envelope, NetworkRelayDeliveryMode.Reliable);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TCP maximum*");
    }

    [Fact]
    public async Task SendReliableToAsync_ShouldAwaitAcknowledgement()
    {
        var senderUdpPort = GetFreePort();
        var senderTcpPort = GetFreePort();
        var receiverUdpPort = GetFreePort();
        var receiverTcpPort = GetFreePort();

        var sender = new NoireNetworkRelay(active: false, enableLogging: false, port: senderUdpPort, enablePeerDiscovery: false, allowLoopbackMessages: false, enableReliableTransport: true, reliablePort: senderTcpPort)
            .SetAutoActivateOnSend(false)
            .RegisterSelf()
            .ActivateSelf()
            .SetReliableAcknowledgementTimeout(TimeSpan.FromSeconds(2));

        var receiver = new NoireNetworkRelay(active: false, enableLogging: false, port: receiverUdpPort, enablePeerDiscovery: false, allowLoopbackMessages: false, enableReliableTransport: true, reliablePort: receiverTcpPort)
            .SetAutoActivateOnSend(false)
            .RegisterSelf()
            .ActivateSelf()
            .SetReliableAcknowledgementTimeout(TimeSpan.FromSeconds(2));

        var payloadReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.On<string>(payload => payloadReceived.TrySetResult(payload), channel: "ack.test");

        sender.Start();
        receiver.Start();

        try
        {
            var receipt = await sender.SendReliableToAsync("127.0.0.1", receiver.ReliablePort, "hello ack", channel: "ack.test");
            var receivedPayload = await payloadReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));

            receipt.Channel.Should().Be("ack.test");
            receipt.MessageId.Should().NotBeNullOrWhiteSpace();
            receipt.AcknowledgedAtUtc.Should().BeOnOrAfter(receipt.SentAtUtc);
            receivedPayload.Should().Be("hello ack");
        }
        finally
        {
            sender.Stop();
            receiver.Stop();
        }
    }

    [Fact]
    public async Task SendReliableToAsync_ShouldInvokeFailureCallback_WhenTransportFails()
    {
        var senderUdpPort = GetFreePort();
        var senderTcpPort = GetFreePort();
        var unusedTcpPort = GetFreePort();

        var sender = new NoireNetworkRelay(active: false, enableLogging: false, port: senderUdpPort, enablePeerDiscovery: false, allowLoopbackMessages: false, enableReliableTransport: true, reliablePort: senderTcpPort)
            .SetAutoActivateOnSend(false)
            .SetReliableTimeouts(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))
            .SetReliableAcknowledgementTimeout(TimeSpan.FromSeconds(1));

        sender.Start();

        Exception? callbackException = null;

        try
        {
            Func<Task> act = async () => await sender.SendReliableToAsync(
                "127.0.0.1",
                unusedTcpPort,
                "hello ack",
                channel: "ack.test",
                onFailure: ex => callbackException = ex);

            await act.Should().ThrowAsync<Exception>();
            callbackException.Should().NotBeNull();
        }
        finally
        {
            sender.Stop();
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static object CreateMessageEnvelope(NoireNetworkRelay relay, string payload)
    {
        var method = typeof(NoireNetworkRelay)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(method => method.Name == "CreateMessageEnvelope" && method.IsGenericMethodDefinition)
            .MakeGenericMethod(typeof(string));

        return method.Invoke(relay, [payload, "test", null, null, false])!;
    }

    private static byte[] SerializeEnvelope(NoireNetworkRelay relay, object envelope, NetworkRelayDeliveryMode deliveryMode)
    {
        var method = typeof(NoireNetworkRelay).GetMethod("SerializeEnvelope", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            return (byte[])method.Invoke(relay, [envelope, deliveryMode])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }

    private static async Task<T> WaitWithStatsAsync<T>(Task<T> task, NoireNetworkRelay primaryRelay, NoireNetworkRelay secondaryRelay, string label)
    {
        var completedTask = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(3)));
        if (completedTask == task)
            return await task;

        var primaryStats = primaryRelay.GetStatistics();
        var secondaryStats = secondaryRelay.GetStatistics();

        throw new XunitException($"Timed out while {label}. Primary state: Active={primaryRelay.IsActive}, SelfRegistered={primaryRelay.IsSelfRegistered}, SelfActive={primaryRelay.IsSelfActive}. Primary stats: Sent={primaryStats.TotalMessagesSent}, Received={primaryStats.TotalMessagesReceived}, BytesSent={primaryStats.TotalBytesSent}, BytesReceived={primaryStats.TotalBytesReceived}, UdpReceived={primaryStats.TotalBestEffortMessagesReceived}, PeerAnnouncements={primaryStats.TotalPeerAnnouncementsReceived}, SendFailures={primaryStats.TotalSendFailures}, ReceiveFailures={primaryStats.TotalReceiveFailures}. Secondary state: Active={secondaryRelay.IsActive}, SelfRegistered={secondaryRelay.IsSelfRegistered}, SelfActive={secondaryRelay.IsSelfActive}. Secondary stats: Sent={secondaryStats.TotalMessagesSent}, Received={secondaryStats.TotalMessagesReceived}, BytesSent={secondaryStats.TotalBytesSent}, BytesReceived={secondaryStats.TotalBytesReceived}, UdpReceived={secondaryStats.TotalBestEffortMessagesReceived}, PeerAnnouncements={secondaryStats.TotalPeerAnnouncementsReceived}, SendFailures={secondaryStats.TotalSendFailures}, ReceiveFailures={secondaryStats.TotalReceiveFailures}.");
    }
}
