using FluentAssertions;
using NoireLib.Websocket;
using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The limits an accepted socket runs under, driven through a real connection and not through the structures
/// behind it. The overflow policy decides what a peer that stopped reading costs, a message past a cap ends the
/// connection with 1009 on the wire, an upgrade past the socket budget is answered 503, and both timers fire.
/// </summary>
public sealed class NoireWebsocketServerLimitsTests
{
    [Fact]
    public async Task PeerQueueOverflow_DropNewest_KeepsTheConnectionAndEverythingAlreadyQueued()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Options.Overflow = NoireWebsocketPeerOverflow.DropNewest;
        harness.Sockets.Options.PeerQueueCapacity = 2;

        var chat = harness.Sockets.Publish("chat");
        using var stalled = new StalledPeerStream();

        var connection = Accept(chat, stalled);
        var queued = await FillTheQueueAsync(connection, stalled, 2);

        await connection.SendAsync("newest", TestContext.Current.CancellationToken);

        connection.QueuedMessages.Should().Be(2, "the newest message was discarded on arrival, never queued");
        connection.State.Should().Be(NoireSocketState.Connected, "a drop policy keeps the peer");
        queued[0].IsCompleted.Should().BeFalse("nothing already queued was touched");
        queued[1].IsCompleted.Should().BeFalse();

        await ReleaseAsync(connection, stalled, queued);
    }

    [Fact]
    public async Task PeerQueueOverflow_DropOldest_KeepsTheConnectionAndDiscardsTheMessageThatWaitedLongest()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Options.Overflow = NoireWebsocketPeerOverflow.DropOldest;
        harness.Sockets.Options.PeerQueueCapacity = 2;

        var chat = harness.Sockets.Publish("chat");
        using var stalled = new StalledPeerStream();

        var connection = Accept(chat, stalled);
        var queued = await FillTheQueueAsync(connection, stalled, 2);

        await connection.SendAsync("newest", TestContext.Current.CancellationToken);

        connection.QueuedMessages.Should().Be(2, "one message left as the new one arrived");
        connection.State.Should().Be(NoireSocketState.Connected, "a drop policy keeps the peer");
        queued[0].IsCompleted.Should().BeTrue("the message that waited longest is the one discarded");
        queued[1].IsCompleted.Should().BeFalse();

        await ReleaseAsync(connection, stalled, queued);
    }

    [Fact]
    public async Task PeerQueueOverflow_CloseConnection_ThrowsAndEndsTheConnectionWithTryAgainLater()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Options.Overflow = NoireWebsocketPeerOverflow.CloseConnection;
        harness.Sockets.Options.PeerQueueCapacity = 2;

        var chat = harness.Sockets.Publish("chat");
        using var stalled = new StalledPeerStream();

        var connection = Accept(chat, stalled);
        var queued = await FillTheQueueAsync(connection, stalled, 2);

        Action overflowing = () => connection.SendAsync("newest", TestContext.Current.CancellationToken);

        overflowing.Should().Throw<NoireSocketQueueFullException>().Which.Capacity.Should().Be(2);

        (await NoireWebsocketServerTests.WaitUntilAsync(() => connection.State == NoireSocketState.Disconnected))
            .Should().BeTrue("a peer that stopped reading is dropped; nothing buffers for it without a bound");

        connection.Close!.RawCode.Should().Be((int)NoireWebsocketCloseCode.TryAgainLater);
        connection.Close.Initiator.Should().Be(NoireWebsocketCloseInitiator.Local);

        await ReleaseAsync(connection, stalled, queued);
    }

    [Fact]
    public async Task MaxMessageSize_Exceeded_ClosesWithMessageTooBig()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Options.MaxMessageSize = 64;

        var chat = harness.Sockets.Publish("chat");
        using var peer = harness.Connect(chat);

        await peer.SendAsync(WebsocketTestFrames.Text(new string('x', 200)));

        var close = await peer.ReadFrameAsync();

        CloseCodeOf(close).Should().Be((int)NoireWebsocketCloseCode.MessageTooBig);
        CloseReasonOf(close).Should().Contain("64 byte cap");

        (await NoireWebsocketServerTests.WaitUntilAsync(() => peer.Connection.State == NoireSocketState.Disconnected))
            .Should().BeTrue();

        peer.Connection.Close!.Initiator.Should().Be(NoireWebsocketCloseInitiator.Local);
    }

    [Fact]
    public async Task MaxFrameSize_Exceeded_ClosesOffTheLengthFieldAlone()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Options.MaxFrameSize = 64;

        var chat = harness.Sockets.Publish("chat");
        using var peer = harness.Connect(chat);

        // Only the header is sent.
        var announced = WebsocketTestFrames.Text(new string('x', 200));

        await peer.SendAsync(announced[..8]);

        var close = await peer.ReadFrameAsync();

        CloseCodeOf(close).Should().Be((int)NoireWebsocketCloseCode.MessageTooBig);
        CloseReasonOf(close).Should().Contain("64 byte frame cap");
    }

    [Fact]
    public async Task MaxMessageFragments_Exceeded_ClosesWithMessageTooBig()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Options.MaxMessageFragments = 2;

        var chat = harness.Sockets.Publish("chat");
        using var peer = harness.Connect(chat);

        await peer.SendAsync(
            WebsocketTestFrames.Frame(0x1, "a"u8, false),
            WebsocketTestFrames.Frame(0x0, "b"u8, false),
            WebsocketTestFrames.Frame(0x0, "c"u8, false));

        var close = await peer.ReadFrameAsync();

        CloseCodeOf(close).Should().Be((int)NoireWebsocketCloseCode.MessageTooBig);
        CloseReasonOf(close).Should().Contain("more than 2 fragments");
    }

    [Fact]
    public async Task MaxSockets_Exhausted_AnswersTheUpgradeWith503()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Options.MaxSockets = 1;

        var chat = harness.Sockets.Publish("chat");
        harness.Http.Start();

        using var held = harness.Connect(chat);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, harness.Http.Port, TestContext.Current.CancellationToken);

        var stream = client.GetStream();

        await stream.WriteAsync(UpgradeRequest("chat", harness.Http.Port, harness.Http.Token), TestContext.Current.CancellationToken);

        var answer = await ReadHeaderBlockAsync(stream);

        answer.Should().StartWith("HTTP/1.1 503");
        answer.Should().Contain("Retry-After: 5", "a refused upgrade tells the caller when to come back");
        harness.Sockets.ConnectionCount.Should().Be(1, "the socket that was refused was never registered");
    }

    [Fact]
    public async Task HeartbeatInterval_Elapsing_SendsAPingToASilentPeer()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Options.HeartbeatInterval = TimeSpan.FromMilliseconds(200);

        var chat = harness.Sockets.Publish("chat");

        var clock = Stopwatch.StartNew();
        using var peer = harness.Connect(chat);

        var frame = await peer.ReadFrameAsync();

        clock.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(100, "nothing goes out before the interval elapses");
        (frame[0] & 0x0F).Should().Be(0x9);
        (frame[1] & 0x80).Should().Be(0, "a server never masks");
        frame[1].Should().Be(0, "the heartbeat ping carries no payload");
    }

    [Fact]
    public async Task IdleTimeout_Elapsing_DropsAPeerThatSendsNothing()
    {
        using var harness = new WebsocketHarness();
        harness.Sockets.Options.IdleTimeout = TimeSpan.FromMilliseconds(250);

        var chat = harness.Sockets.Publish("chat");

        var clock = Stopwatch.StartNew();
        using var peer = harness.Connect(chat);

        (await NoireWebsocketServerTests.WaitUntilAsync(() => peer.Connection.State == NoireSocketState.Disconnected))
            .Should().BeTrue();

        clock.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(120, "a peer gets the whole timeout before it is dropped");
        peer.Connection.Close!.RawCode.Should().Be((int)NoireWebsocketCloseCode.AbnormalClosure);
        peer.Connection.Close.Initiator.Should().Be(NoireWebsocketCloseInitiator.Transport);
        peer.Connection.Close.WasClean.Should().BeFalse();

        (await NoireWebsocketServerTests.WaitUntilAsync(() => chat.ClientCount == 0))
            .Should().BeTrue("a dropped connection gives its slot back");
    }

    private static NoireWebsocketConnection Accept(NoireWebsocketEndpoint endpoint, Stream stream)
    {
        var connection = endpoint.Register(stream, null, "127.0.0.1", true, null, out var claim);

        connection.Should().NotBeNull("the socket budget refused the connection: " + claim);
        connection!.Start(ReadOnlyMemory<byte>.Empty);

        return connection;
    }

    // The writer takes one message out before it blocks. Waits follow what the connection reports.
    private static async Task<Task[]> FillTheQueueAsync(NoireWebsocketConnection connection, StalledPeerStream stalled, int capacity)
    {
        connection.Send("in flight");

        (await NoireWebsocketServerTests.WaitUntilAsync(() => stalled.Writes == 1))
            .Should().BeTrue("the write loop has to be holding a message before the queue can fill");

        var queued = new Task[capacity];

        for (var index = 0; index < capacity; index++)
            queued[index] = connection.SendAsync("queued " + index, CancellationToken.None);

        (await NoireWebsocketServerTests.WaitUntilAsync(() => connection.QueuedMessages == capacity))
            .Should().BeTrue("the queue has to be exactly full before the next message decides the outcome");

        return queued;
    }

    // Every failed message is awaited, leaving no unobserved fault.
    private static async Task ReleaseAsync(NoireWebsocketConnection connection, StalledPeerStream stalled, Task[] queued)
    {
        connection.Dispose();
        stalled.Release();

        foreach (var task in queued)
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(4));
            }
            catch (Exception)
            {
            }
        }
    }

    private static int CloseCodeOf(byte[] frame)
    {
        (frame[0] & 0x0F).Should().Be(0x8, "a limit ends the connection; it does not drop the message alone");
        return BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(2, 2));
    }

    private static string CloseReasonOf(byte[] frame)
        => Encoding.UTF8.GetString(frame, 4, frame.Length - 4);

    private static byte[] UpgradeRequest(string name, int port, string token)
        => Encoding.ASCII.GetBytes(
            "GET /noire/ws/" + name + " HTTP/1.1\r\n"
            + "Host: 127.0.0.1:" + port + "\r\n"
            + "Upgrade: websocket\r\nConnection: Upgrade\r\n"
            + "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n"
            + "Authorization: Bearer " + token + "\r\n\r\n");

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
}

// A peer that stopped reading: writes never complete and nothing arrives.
internal sealed class StalledPeerStream : Stream
{
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int writes;

    public int Writes => Volatile.Read(ref writes);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public void Release()
        => released.TrySetResult();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref writes);
        await released.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await released.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
        => Interlocked.Increment(ref writes);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        released.TrySetResult();
        base.Dispose(disposing);
    }
}
