using FluentAssertions;
using NoireLib.Remote;
using NoireLib.Websocket;
using NoireLib.Websocket.Internal;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The socket half of a listener. One name is published once, the endpoint's thread, access and readiness fall back
/// through the same three levels the HTTP half uses, the client and room views are snapshots and never live views,
/// and the character-state gate is asked once per drain and never once per message.
/// </summary>
public sealed class NoireWebsocketServerTests
{
    [NoireWebsocketEndpoint("probe")]
    public static class AttributedProbe
    {
        public static List<string> Seen { get; } = [];

        public static int Opens { get; set; }

        [NoireWebsocketOn]
        public static void Message(NoireWebsocketConnection client, NoireWebsocketMessage message)
            => Seen.Add(message.Text);

        [NoireWebsocketOn]
        public static void Opened(NoireWebsocketConnection client)
            => Opens++;

        // No socket callback takes this shape.
        [NoireWebsocketOn]
        public static void Unusable(int what)
        {
        }
    }

    [Fact]
    public void Publish_TheSameNameTwice_Throws()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Publish("chat");

        var second = () => harness.Sockets.Publish("chat");

        second.Should().Throw<InvalidOperationException>().WithMessage("*already publishes a socket named 'chat'*");
    }

    [Fact]
    public void Publish_ANameTheWireCannotCarry_Throws()
    {
        using var harness = new WebsocketHarness();

        var underscored = () => harness.Sockets.Publish("_chat");
        var slashed = () => harness.Sockets.Publish("chat/say");

        underscored.Should().Throw<InvalidOperationException>();
        slashed.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Publish_AfterTheListenerStopped_StillPublishes()
    {
        using var harness = new WebsocketHarness();
        harness.Http.Start();
        harness.Http.Stop();

        var late = harness.Sockets.Publish("late");

        late.Path.Should().Be("/noire/ws/late");
        harness.Sockets.GetEndpoint("LATE").Should().BeSameAs(late, "a socket name matches the way a route does, ignoring case");
    }

    [Fact]
    public async Task StoppingTheListener_ClosesEveryOpenSocket()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");
        harness.Http.Start();

        using var peer = harness.Connect(chat);

        harness.Http.Stop();

        (await WaitUntilAsync(() => chat.ClientCount == 0)).Should().BeTrue("a socket cannot be resumed across a rebind");
        peer.Connection.Close!.Code.Should().Be(NoireWebsocketCloseCode.GoingAway);
    }

    [Fact]
    public void Thread_FallsBackThroughTheEndpointThenTheServerThenTheHost()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        chat.Thread.Should().Be(NoireRemoteThread.Background, "nothing declared one and the standalone host answers Background");

        harness.Sockets.Options.Thread = NoireRemoteThread.Framework;
        chat.Thread.Should().Be(NoireRemoteThread.Framework);

        chat.Options.Thread = NoireRemoteThread.Background;
        chat.Thread.Should().Be(NoireRemoteThread.Background);
    }

    [Fact]
    public void AccessAndRequires_FallBackTheWayTheHttpHalfDoes()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        chat.Access.Should().Be(NoireRemoteAccess.Local);
        chat.Requires.Should().Be(NoireRemoteReadiness.None, "a background handler reads no character state by default");

        chat.Options.Access = NoireRemoteAccess.Remote;
        chat.Options.Thread = NoireRemoteThread.Framework;

        chat.Access.Should().Be(NoireRemoteAccess.Remote);
        chat.Requires.Should().Be(NoireRemoteReadiness.StateReady, "a framework handler touches game state");

        chat.Options.Requires = NoireRemoteReadiness.PlayerLoaded;
        chat.Requires.Should().Be(NoireRemoteReadiness.PlayerLoaded);
    }

    [Fact]
    public void PublishType_AttachesEveryMethodWhoseSignatureNamesACallback()
    {
        using var harness = new WebsocketHarness();

        var probe = harness.Sockets.PublishType(typeof(AttributedProbe));

        probe.Name.Should().Be("probe");
        harness.Host.Lines.Should().Contain(line => line.Contains("Unusable") && line.Contains("no socket callback takes this signature"));
    }

    [Fact]
    public async Task AnAttributedType_ReceivesTheOpenAndTheMessage()
    {
        using var harness = new WebsocketHarness();
        AttributedProbe.Seen.Clear();
        AttributedProbe.Opens = 0;

        var probe = harness.Sockets.PublishType(typeof(AttributedProbe));
        using var peer = harness.Connect(probe);

        await peer.SendTextAsync("ping");

        (await WaitUntilAsync(() => AttributedProbe.Seen.Count == 1)).Should().BeTrue();
        AttributedProbe.Seen[0].Should().Be("ping");
        AttributedProbe.Opens.Should().Be(1);
    }

    [Fact]
    public void PublishAttributedTypes_FindsEveryTypeCarryingTheEndpointAttribute()
    {
        using var harness = new WebsocketHarness();

        var published = harness.Sockets.PublishAttributedTypes(typeof(NoireWebsocketServerTests).Assembly);

        published.Should().Contain(endpoint => endpoint.Name == "probe");
    }

    [Fact]
    public async Task ThreeMessagesInOnePacket_AskTheHostForReadinessOnce()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat", new NoireWebsocketEndpointOptions { Requires = NoireRemoteReadiness.StateReady });

        var seen = 0;
        chat.OnMessage((_, _) => Interlocked.Increment(ref seen));

        using var peer = harness.Connect(chat);
        harness.Host.ReadinessChecks = 0;

        await peer.SendAsync(WebsocketTestFrames.Text("one"), WebsocketTestFrames.Text("two"), WebsocketTestFrames.Text("three"));

        (await WaitUntilAsync(() => Volatile.Read(ref seen) == 3)).Should().BeTrue();
        harness.Host.ReadinessChecks.Should().Be(1, "the gate belongs to the drain as a whole");
    }

    [Fact]
    public async Task AHostThatIsNotReady_DeliversNothingAndReportsOnce()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat", new NoireWebsocketEndpointOptions { Requires = NoireRemoteReadiness.PlayerLoaded });

        var seen = 0;
        var reported = 0;

        chat.OnMessage((_, _) => Interlocked.Increment(ref seen));
        chat.OnError(_ => Interlocked.Increment(ref reported));

        using var peer = harness.Connect(chat);
        harness.Host.Ready = false;

        await peer.SendAsync(WebsocketTestFrames.Text("one"), WebsocketTestFrames.Text("two"));

        (await WaitUntilAsync(() => Volatile.Read(ref reported) > 0)).Should().BeTrue();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Volatile.Read(ref seen).Should().Be(0);
        Volatile.Read(ref reported).Should().Be(1, "one drain is one report however many messages it carried");
    }

    [Fact]
    public async Task Clients_IsASnapshotAndSurvivesAnAcceptMidEnumeration()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Options.MaxSockets = 512;
        harness.Sockets.Options.MaxSocketsPerAddress = 512;

        var chat = harness.Sockets.Publish("chat");
        var registered = chat.Register(Stream.Null, null, "127.0.0.1", true, null, out _);
        registered.Should().NotBeNull();

        var taken = chat.Clients;

        var accepting = Task.Run(
            () =>
            {
                for (var index = 0; index < 200; index++)
                    chat.Register(Stream.Null, null, "127.0.0.1", true, null, out _);
            },
            TestContext.Current.CancellationToken);

        var walk = () =>
        {
            for (var pass = 0; pass < 200; pass++)
            {
                foreach (var client in chat.Clients)
                    _ = client.Id;
            }
        };

        walk.Should().NotThrow("a live view over the registry would throw into a plugin's draw code");
        await accepting;

        taken.Should().ContainSingle("a snapshot taken before the accepts cannot have grown");
        chat.Clients.Count.Should().Be(201);
    }

    [Fact]
    public async Task DisposingTheEndpoint_FreesEveryConnectionItOwns()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        var peers = new List<WebsocketTestPeer> { harness.Connect(chat), harness.Connect(chat), harness.Connect(chat) };

        chat.ClientCount.Should().Be(3);
        harness.Sockets.ConnectionCount.Should().Be(3);

        chat.Dispose();

        (await WaitUntilAsync(() => chat.ClientCount == 0 && harness.Sockets.ConnectionCount == 0)).Should().BeTrue();
        peers.Should().OnlyContain(peer => peer.Connection.State == NoireSocketState.Disconnected);
        harness.Sockets.GetEndpoint("chat").Should().BeNull("a disposed endpoint is unpublished too");

        foreach (var peer in peers)
            peer.Dispose();
    }

    [Fact]
    public void MaxSockets_RefusesTheOneThatWouldPassIt()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Options.MaxSockets = 2;

        var chat = harness.Sockets.Publish("chat");

        chat.Register(Stream.Null, null, "10.0.0.1", false, null, out _).Should().NotBeNull();
        chat.Register(Stream.Null, null, "10.0.0.2", false, null, out _).Should().NotBeNull();
        chat.Register(Stream.Null, null, "10.0.0.3", false, null, out var claim).Should().BeNull();

        claim.Should().Be(SocketSlotResult.TooManySockets);
    }

    [Fact]
    public void MaxSocketsPerAddress_CountsLoopbackAsOneAddress()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Options.MaxSocketsPerAddress = 2;

        var chat = harness.Sockets.Publish("chat");

        chat.Register(Stream.Null, null, "127.0.0.1", true, null, out _).Should().NotBeNull();
        chat.Register(Stream.Null, null, "127.0.0.1", true, null, out _).Should().NotBeNull();
        chat.Register(Stream.Null, null, "127.0.0.1", true, null, out var claim).Should().BeNull();
        chat.Register(Stream.Null, null, "10.0.0.7", false, null, out _).Should().NotBeNull("another address has its own budget");

        claim.Should().Be(SocketSlotResult.TooManyForAddress);
    }

    [Fact]
    public async Task APeerThatClosesCleanly_IsAnsweredWithItsOwnCode()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        using var peer = harness.Connect(chat);

        await peer.SendAsync(WebsocketTestFrames.Close(1001, "leaving"));

        (await WaitUntilAsync(() => peer.Connection.State == NoireSocketState.Disconnected)).Should().BeTrue();

        peer.Connection.Close!.RawCode.Should().Be(1001);
        peer.Connection.Close.Initiator.Should().Be(NoireWebsocketCloseInitiator.Remote);
        peer.Connection.Close.WasClean.Should().BeTrue();

        var echoed = peer.WrittenFrames();
        echoed.Should().Contain(frame => IsCloseWithCode(frame, 1001));
    }

    [Fact]
    public async Task ACloseFrameWithNoCode_EndsAsNoStatusReceivedAndEchoesAnEmptyClose()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        using var peer = harness.Connect(chat);

        await peer.SendAsync(WebsocketTestFrames.Frame(0x8, []));

        (await WaitUntilAsync(() => peer.Connection.State == NoireSocketState.Disconnected)).Should().BeTrue();

        peer.Connection.Close!.RawCode.Should().Be((int)NoireWebsocketCloseCode.NoStatusReceived);
        peer.WrittenFrames().Should().Contain(frame => frame.Length == 2 && (frame[0] & 0x0F) == 0x8, "1005 is never encoded onto the wire");
    }

    [Fact]
    public async Task AMessageFromTheServer_ReachesThePeerAsOneUnmaskedFrame()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        using var peer = harness.Connect(chat);

        await peer.Connection.SendAsync("hello", TestContext.Current.CancellationToken);

        var frame = await peer.ReadFrameAsync();

        (frame[0] & 0x0F).Should().Be(0x1);
        (frame[1] & 0x80).Should().Be(0, "a server never masks");
        Encoding.UTF8.GetString(frame.AsSpan(2)).Should().Be("hello");
    }

    [Fact]
    public async Task AnUpgradeOnTheListener_IsAnsweredWithA101AndKeepsThePipelinedFirstFrame()
    {
        using var harness = new WebsocketHarness();
        var chat = harness.Sockets.Publish("chat");

        var seen = new List<string>();
        chat.OnMessage((_, message) =>
        {
            lock (seen)
                seen.Add(message.Text);
        });

        harness.Http.Start();
        harness.Http.IsListening.Should().BeTrue();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, harness.Http.Port, TestContext.Current.CancellationToken);

        var stream = client.GetStream();
        var head = UpgradeRequest("chat", harness.Http.Port, harness.Http.Token);

        var pipelined = WebsocketTestFrames.Text("pipelined");
        var packet = new byte[head.Length + pipelined.Length];

        head.CopyTo(packet, 0);
        pipelined.CopyTo(packet, head.Length);

        // Handshake and first frame in one packet.
        await stream.WriteAsync(packet, TestContext.Current.CancellationToken);

        var answer = await ReadHeaderBlockAsync(stream);

        answer.Should().StartWith("HTTP/1.1 101");
        answer.Should().Contain("Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=");

        (await WaitUntilAsync(() =>
        {
            lock (seen)
                return seen.Count == 1;
        })).Should().BeTrue();

        lock (seen)
            seen[0].Should().Be("pipelined");
    }

    [Fact]
    public async Task AnUpgradeToAnUnpublishedName_IsRefusedWithoutTakingTheSocket()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Publish("chat");
        harness.Http.Start();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, harness.Http.Port, TestContext.Current.CancellationToken);

        var stream = client.GetStream();
        var head = UpgradeRequest("nothing", harness.Http.Port, harness.Http.Token);

        await stream.WriteAsync(head, TestContext.Current.CancellationToken);

        var answer = await ReadHeaderBlockAsync(stream);

        answer.Should().StartWith("HTTP/1.1 404");
        harness.Sockets.ConnectionCount.Should().Be(0);
    }

    private static byte[] UpgradeRequest(string name, int port, string token)
        => Encoding.ASCII.GetBytes(
            "GET /noire/ws/" + name + " HTTP/1.1\r\n"
            + "Host: 127.0.0.1:" + port + "\r\n"
            + "Upgrade: websocket\r\nConnection: Upgrade\r\n"
            + "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n"
            + "Authorization: Bearer " + token + "\r\n\r\n");

    // One byte at a time, leaving the frames after the handshake in the socket.
    private static async Task<string> ReadHeaderBlockAsync(NetworkStream stream)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var builder = new StringBuilder();
        var one = new byte[1];

        while (!EndsTheHeaderBlock(builder))
        {
            if (await stream.ReadAsync(one, deadline.Token) == 0)
                break;

            builder.Append((char)one[0]);
        }

        return builder.ToString();
    }

    private static bool EndsTheHeaderBlock(StringBuilder builder)
        => builder.Length >= 4 && builder[^4] == '\r' && builder[^3] == '\n' && builder[^2] == '\r' && builder[^1] == '\n';

    private static bool IsCloseWithCode(byte[] frame, int code)
        => frame.Length >= 4 && (frame[0] & 0x0F) == 0x8 && BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(2, 2)) == code;

    internal static async Task<bool> WaitUntilAsync(Func<bool> condition, int milliseconds = 4000)
    {
        var clock = Stopwatch.StartNew();

        while (clock.ElapsedMilliseconds < milliseconds)
        {
            if (condition())
                return true;

            await Task.Delay(10);
        }

        return condition();
    }
}

// No port is bound. A connection is registered onto an in-memory duplex stream.
internal sealed class WebsocketHarness : IDisposable
{
    public WebsocketHarness()
    {
        Host = new TestHost();
        Http = new NoireRemoteServer(Host, new NoireRemoteOptions
        {
            Port = 0,
            AutoStart = false,
            PublishDirectoryRecord = false,
            PublishCharacterIdentity = false,
            EnableConsole = false,
            EnableLogging = false,
        });

        Sockets = NoireWebsocketServer.For(Http);

        // No real peer answers a ping or a close.
        Sockets.Options.HeartbeatInterval = TimeSpan.Zero;
        Sockets.Options.IdleTimeout = TimeSpan.Zero;
        Sockets.Options.CloseTimeout = TimeSpan.FromMilliseconds(250);
    }

    public TestHost Host { get; }

    public NoireRemoteServer Http { get; }

    public NoireWebsocketServer Sockets { get; }

    public WebsocketTestPeer Connect(NoireWebsocketEndpoint endpoint, string address = "127.0.0.1", bool isLoopback = true)
    {
        var peer = new WebsocketTestPeer();
        var connection = endpoint.Register(peer.ServerStream, null, address, isLoopback, null, out var claim);

        if (connection == null)
            throw new InvalidOperationException("The socket budget refused the connection: " + claim + ".");

        peer.Attach(connection);
        connection.Start(ReadOnlyMemory<byte>.Empty);

        return peer;
    }

    public void Dispose()
    {
        Sockets.Dispose();
        Http.Dispose();
    }

    internal sealed class TestHost : NoireRemoteStandaloneHost
    {
        public int ReadinessChecks;

        public bool Ready { get; set; } = true;

        public List<string> Lines { get; } = [];

        public override NoireRemoteReadinessResult CheckReadiness(NoireRemoteReadiness requires, string route)
        {
            Interlocked.Increment(ref ReadinessChecks);

            return Ready ? NoireRemoteReadinessResult.Ready : NoireRemoteReadinessResult.NotReady("The character is not loaded.");
        }

        public override void Log(NoireRemoteLogLevel level, string message, Exception? exception)
        {
            lock (Lines)
                Lines.Add(level + " " + message);
        }
    }
}

internal sealed class WebsocketTestPeer : IDisposable
{
    private readonly Channel<byte[]> toServer = Channel.CreateUnbounded<byte[]>();
    private readonly Channel<byte[]> fromServer = Channel.CreateUnbounded<byte[]>();
    private readonly List<byte[]> written = [];

    public WebsocketTestPeer()
        => ServerStream = new DuplexStream(toServer, fromServer);

    public Stream ServerStream { get; }

    public NoireWebsocketConnection Connection { get; private set; } = null!;

    public void Attach(NoireWebsocketConnection connection)
        => Connection = connection;

    public Task SendTextAsync(string text)
        => SendAsync(WebsocketTestFrames.Text(text));

    public async Task SendAsync(params byte[][] frames)
    {
        var total = frames.Sum(frame => frame.Length);
        var packet = new byte[total];
        var at = 0;

        // One packet, one drain.
        foreach (var frame in frames)
        {
            frame.CopyTo(packet, at);
            at += frame.Length;
        }

        await toServer.Writer.WriteAsync(packet);
    }

    public async Task<byte[]> ReadFrameAsync()
    {
        var frame = await TryReadFrameAsync(TimeSpan.FromSeconds(4));

        return frame ?? throw new TimeoutException("Nothing was written to the peer.");
    }

    public async Task<byte[]?> TryReadFrameAsync(TimeSpan wait)
    {
        using var deadline = new CancellationTokenSource(wait);

        try
        {
            var frame = await fromServer.Reader.ReadAsync(deadline.Token);

            lock (written)
                written.Add(frame);

            return frame;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public IReadOnlyList<byte[]> WrittenFrames()
    {
        while (fromServer.Reader.TryRead(out var frame))
        {
            lock (written)
                written.Add(frame);
        }

        lock (written)
            return [.. written];
    }

    public void Dispose()
    {
        toServer.Writer.TryComplete();
        fromServer.Writer.TryComplete();
        ServerStream.Dispose();
    }

    private sealed class DuplexStream(Channel<byte[]> inbound, Channel<byte[]> outbound) : Stream
    {
        private byte[]? pending;
        private int offset;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (pending == null || offset >= pending.Length)
            {
                if (!await inbound.Reader.WaitToReadAsync(cancellationToken))
                    return 0;

                if (!inbound.Reader.TryRead(out pending))
                    continue;

                offset = 0;
            }

            var count = Math.Min(buffer.Length, pending.Length - offset);
            pending.AsSpan(offset, count).CopyTo(buffer.Span);
            offset += count;

            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            outbound.Writer.TryWrite(buffer.ToArray());
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public override void Write(byte[] buffer, int offset, int count)
            => outbound.Writer.TryWrite(buffer.AsSpan(offset, count).ToArray());

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            inbound.Writer.TryComplete();
            outbound.Writer.TryComplete();

            base.Dispose(disposing);
        }
    }
}

// Masked, like every client frame. The listener refuses an unmasked one.
internal static class WebsocketTestFrames
{
    private static readonly byte[] Mask = [0x37, 0xFA, 0x21, 0x3D];

    public static byte[] Text(string text)
        => Frame(0x1, Encoding.UTF8.GetBytes(text));

    public static byte[] Close(int code, string? reason)
    {
        var text = reason == null ? [] : Encoding.UTF8.GetBytes(reason);
        var payload = new byte[2 + text.Length];

        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)code);
        text.CopyTo(payload, 2);

        return Frame(0x8, payload);
    }

    public static byte[] Frame(byte opcode, ReadOnlySpan<byte> payload, bool final = true)
    {
        var header = payload.Length <= 125 ? 2 : payload.Length <= ushort.MaxValue ? 4 : 10;
        var frame = new byte[header + 4 + payload.Length];

        frame[0] = (byte)((final ? 0x80 : 0x00) | opcode);

        if (payload.Length <= 125)
        {
            frame[1] = (byte)(0x80 | payload.Length);
        }
        else if (payload.Length <= ushort.MaxValue)
        {
            frame[1] = 0x80 | 126;
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2, 2), (ushort)payload.Length);
        }
        else
        {
            frame[1] = 0x80 | 127;
            BinaryPrimitives.WriteUInt64BigEndian(frame.AsSpan(2, 8), (ulong)payload.Length);
        }

        Mask.CopyTo(frame, header);

        for (var index = 0; index < payload.Length; index++)
            frame[header + 4 + index] = (byte)(payload[index] ^ Mask[index & 3]);

        return frame;
    }
}
