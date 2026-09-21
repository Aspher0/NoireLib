using FluentAssertions;
using NoireLib.Websocket;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Pins what a raw WebSocket client does before a byte moves. Construction opens nothing, a URL is normalized to a
/// socket scheme, and a send on a client that is not running is refused, never silently queued forever.
/// </summary>
public sealed class NoireWebsocketClientTests
{
    [Fact]
    public void Constructor_OpensNothing()
    {
        using var client = new NoireWebsocketClient("wss://example.invalid/hub");

        client.State.Should().Be(NoireSocketState.Disconnected);
        client.IsConnected.Should().BeFalse();
        client.QueuedMessages.Should().Be(0);
        client.LastClose.Should().BeNull();
        client.LastError.Should().BeNull();
        client.SubProtocol.Should().BeNull();
    }

    [Fact]
    public void Constructor_ReadsAnHttpSchemeAsItsSocketScheme()
    {
        using var secure = new NoireWebsocketClient("https://example.invalid/hub");
        using var plain = new NoireWebsocketClient("http://example.invalid:9000/hub?x=1");

        secure.Url.Should().Be(new Uri("wss://example.invalid/hub"));
        plain.Url.Should().Be(new Uri("ws://example.invalid:9000/hub?x=1"));
    }

    [Fact]
    public void Constructor_RefusesASchemeThatIsNotASocket()
    {
        var build = () => new NoireWebsocketClient("ftp://example.invalid/hub");

        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RefusesARelativeUrl()
    {
        var build = () => new NoireWebsocketClient("/hub");

        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task SendAsync_OnAClientThatWasNeverConnected_Throws()
    {
        using var client = new NoireWebsocketClient("wss://example.invalid/hub");

        var send = async () => await client.SendAsync("hello");

        await send.Should().ThrowAsync<NoireSocketClosedException>();
    }

    [Fact]
    public async Task ConnectAsync_AfterDispose_Throws()
    {
        var client = new NoireWebsocketClient("wss://example.invalid/hub");
        client.Dispose();

        var connect = async () => await client.ConnectAsync();

        await connect.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_RunsTwiceWithoutThrowing()
    {
        var client = new NoireWebsocketClient("wss://example.invalid/hub");

        client.Dispose();
        var second = client.Dispose;

        second.Should().NotThrow();
    }

    [Fact]
    public async Task ConnectAsync_WhenNothingAnswersAndRetryIsOff_FaultsTheClientAndReportsTheFailure()
    {
        using var client = new NoireWebsocketClient("ws://127.0.0.1:" + ClosedPort() + "/hub");
        client.Options.Retry = NoireRetryPolicy.None;
        client.Options.Http.ConnectionTimeout = TimeSpan.FromSeconds(5);

        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnError(exception => reported.TrySetResult(exception));

        var connect = async () => await client.ConnectAsync(TestContext.Current.CancellationToken);

        await connect.Should().ThrowAsync<NoireSocketConnectException>();
        client.State.Should().Be(NoireSocketState.Faulted);
        client.LastError.Should().BeOfType<NoireSocketConnectException>();

        var delivered = await reported.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        delivered.Should().BeOfType<NoireSocketConnectException>();
    }

    [Fact]
    public async Task SendAsync_WhileTheFirstAttemptIsStillInFlight_IsQueuedRatherThanRefused()
    {
        using var client = new NoireWebsocketClient("ws://127.0.0.1:" + ClosedPort() + "/hub");
        client.Options.Retry = NoireRetryPolicy.Default with { InitialDelay = TimeSpan.FromMinutes(5) };

        _ = client.ConnectAsync(TestContext.Current.CancellationToken);
        var send = client.SendAsync("queued", TestContext.Current.CancellationToken);

        send.IsCompleted.Should().BeFalse("the message waits for a socket to write it to");

        await client.CloseAsync(cancellationToken: TestContext.Current.CancellationToken);

        var observe = async () => await send;
        await observe.Should().ThrowAsync<NoireSocketClosedException>();
    }

    [Fact]
    public void Subscriptions_AreRemovedByOwner()
    {
        using var client = new NoireWebsocketClient("wss://example.invalid/hub");

        var owner = new object();
        var options = new NoireSocketSubscribeOptions { Owner = owner };
        var subscription = client.OnMessage(_ => { }, options);

        client.UnsubscribeAll(owner);

        subscription.IsDisposed.Should().BeFalse("removing by owner drops the handler but leaves the caller's handle usable");
        var dispose = subscription.Dispose;
        dispose.Should().NotThrow();
    }

    // A refused port. It never hangs.
    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>
/// Locks the reassembly buffer's contract. Fragments become one payload, the size cap is enforced as the message
/// grows, and the buffer is reused. A payload kept past its callback reads whatever arrived next.
/// </summary>
public sealed class NoireWebsocketReassemblyTests
{
    [Fact]
    public void Append_JoinsFragmentsIntoOneMessage()
    {
        var assembler = new NoireWebsocketClient.MessageAssembler(1024, 4);

        assembler.Append(Encoding.UTF8.GetBytes("Hello, "));
        assembler.Append(Encoding.UTF8.GetBytes("frag"));
        assembler.Append(Encoding.UTF8.GetBytes("mented world"));

        assembler.Length.Should().Be(23);
        Encoding.UTF8.GetString(assembler.Payload.Span).Should().Be("Hello, fragmented world");
    }

    [Fact]
    public void Reset_StartsTheNextMessageAtTheFrontOfTheBuffer()
    {
        var assembler = new NoireWebsocketClient.MessageAssembler(1024, 64);

        assembler.Append(Encoding.UTF8.GetBytes("first"));
        assembler.Reset();
        assembler.Append(Encoding.UTF8.GetBytes("second"));

        assembler.Length.Should().Be(6);
        Encoding.UTF8.GetString(assembler.Payload.Span).Should().Be("second");
    }

    [Fact]
    public void Append_PastTheCap_ThrowsWithBothFigures()
    {
        var assembler = new NoireWebsocketClient.MessageAssembler(8, 8);

        assembler.Append(new byte[6]);
        var append = () => assembler.Append(new byte[4]);

        append.Should().Throw<NoireSocketMessageTooLargeException>()
            .Which.Limit.Should().Be(8);
    }

    [Fact]
    public void Append_PastTheCap_LeavesTheMessageAtItsLastGoodLength()
    {
        var assembler = new NoireWebsocketClient.MessageAssembler(8, 8);

        assembler.Append(new byte[6]);

        try
        {
            assembler.Append(new byte[4]);
        }
        catch (NoireSocketMessageTooLargeException exception)
        {
            exception.Size.Should().Be(10);
        }

        assembler.Length.Should().Be(6);
    }

    [Fact]
    public void Bytes_StopBeingValidOnceTheNextMessageArrives()
    {
        var assembler = new NoireWebsocketClient.MessageAssembler(1024, 64);

        assembler.Append(Encoding.UTF8.GetBytes("first "));
        var message = new NoireWebsocketMessage(NoireWebsocketMessageKind.Text, assembler.Payload);
        var kept = message.ToArray();

        assembler.Reset();
        assembler.Append(Encoding.UTF8.GetBytes("second"));

        Encoding.UTF8.GetString(message.Bytes.Span).Should().Be("second",
            "the payload points into the read loop's own buffer and is only valid inside the callback");
        Encoding.UTF8.GetString(kept).Should().Be("first ", "ToArray is the documented way to keep a payload");
    }
}

/// <summary>Locks the refusal of a text frame whose payload is not UTF-8.</summary>
public sealed class NoireWebsocketTextValidationTests
{
    [Fact]
    public void IsDeliverable_ForATextFrameThatIsNotUtf8_IsFalse()
    {
        NoireWebsocketClient.IsDeliverable(NoireWebsocketMessageKind.Text, new byte[] { 0xC3, 0x28 })
            .Should().BeFalse();
    }

    [Fact]
    public void IsDeliverable_ForATruncatedSequence_IsFalse()
    {
        NoireWebsocketClient.IsDeliverable(NoireWebsocketMessageKind.Text, new byte[] { 0xE2, 0x82 })
            .Should().BeFalse();
    }

    [Fact]
    public void IsDeliverable_ForATextFrameThatIsUtf8_IsTrue()
    {
        NoireWebsocketClient.IsDeliverable(NoireWebsocketMessageKind.Text, Encoding.UTF8.GetBytes("héllo 世界"))
            .Should().BeTrue();
    }

    [Fact]
    public void IsDeliverable_ForABinaryFrame_IsTrueWhateverTheBytes()
    {
        NoireWebsocketClient.IsDeliverable(NoireWebsocketMessageKind.Binary, new byte[] { 0xC3, 0x28, 0xFF })
            .Should().BeTrue();
    }
}

/// <summary>
/// Locks that the client options leave the host unresolved. It is taken when the connection opens, never when the
/// options object was built.
/// </summary>
public sealed class NoireWebsocketClientOptionsTests
{
    [Fact]
    public void Host_DefaultsToNull()
    {
        new NoireWebsocketClientOptions().Host.Should().BeNull(
            "an options object built in a field initializer must not capture the host installed at that moment");
    }
}
