using FluentAssertions;
using NoireLib.Remote;
using NoireLib.Remote.Internal;
using NoireLib.Websocket.Internal;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The console's live channel. The envelope it carries in both directions, the cursor model it maps onto the event
/// hub without changing it, the token it reads out of a handshake a browser cannot set a header on, and the backoff
/// the page is handed in place of a flat retry. The NDJSON route it falls back to stays where a caller with no
/// browser reads it.
/// </summary>
public sealed class ConsoleLiveSocketTests : IDisposable
{
    private readonly HttpClient client = new();

    public void Dispose()
        => client.Dispose();

    [Fact]
    public void Handle_WithASubscribe_AnswersWithWhatTheSessionWatches()
    {
        using var hub = new RemoteEventHub(new NoireRemoteOptions());
        using var channel = new ConsoleLiveChannel(hub, Guid.NewGuid(), 16);

        var answer = RoundTrip(channel.Handle(NoireRemoteJson.Write(new NoireRemoteConsoleLiveCommand
        {
            Op = NoireRemoteConsoleLiveOps.Subscribe,
            Subscription = new NoireRemoteEventRequest { Channels = ["log", "metrics"], Topics = ["listener."] },
        })));

        answer.Op.Should().Be(NoireRemoteConsoleLiveOps.Subscribed);
        answer.Channels.Should().Equal("log", "metrics");
        answer.Topics.Should().Equal("listener.");
    }

    [Fact]
    public void Handle_WithAnUnreadableCommand_AnswersWithARefusal()
    {
        using var hub = new RemoteEventHub(new NoireRemoteOptions());
        using var channel = new ConsoleLiveChannel(hub, Guid.NewGuid(), 16);

        channel.Handle("not json at all").Op.Should().Be(NoireRemoteConsoleLiveOps.Error);
        channel.Handle(NoireRemoteJson.Write(new NoireRemoteConsoleLiveCommand { Op = "explode" })).Error!.Message
            .Should().Contain("explode");
    }

    [Fact]
    public void Handle_WithASubscribe_ChangesWhichChannelsAreDelivered()
    {
        using var hub = new RemoteEventHub(new NoireRemoteOptions());
        using var channel = new ConsoleLiveChannel(hub, Guid.NewGuid(), 16);

        channel.Handle(NoireRemoteJson.Write(new NoireRemoteConsoleLiveCommand
        {
            Op = NoireRemoteConsoleLiveOps.Subscribe,
            Subscription = new NoireRemoteEventRequest { Channels = [NoireRemoteChannels.Log] },
        })).Op.Should().Be(NoireRemoteConsoleLiveOps.Subscribed);

        hub.Publish(NoireRemoteChannels.Events, "call.done", null);
        hub.Publish(NoireRemoteChannels.Log, "listener.line", null);

        var frame = channel.Drain(out _);

        frame!.Events.Should().ContainSingle();
        frame.Events![0].Channel.Should().Be(NoireRemoteChannels.Log);
    }

    [Fact]
    public void Handle_WithAnUnsubscribe_StopsDeliveringThatChannel()
    {
        using var hub = new RemoteEventHub(new NoireRemoteOptions());
        using var channel = new ConsoleLiveChannel(hub, Guid.NewGuid(), 16);

        channel.Handle(NoireRemoteJson.Write(new NoireRemoteConsoleLiveCommand
        {
            Op = NoireRemoteConsoleLiveOps.Subscribe,
            Subscription = new NoireRemoteEventRequest { Channels = [NoireRemoteChannels.Events, NoireRemoteChannels.Log] },
        }));

        var answer = channel.Handle(NoireRemoteJson.Write(new NoireRemoteConsoleLiveCommand
        {
            Op = NoireRemoteConsoleLiveOps.Unsubscribe,
            Channels = [NoireRemoteChannels.Log],
        }));

        answer.Channels.Should().Equal(NoireRemoteChannels.Events);

        hub.Publish(NoireRemoteChannels.Log, "listener.line", null);

        channel.Drain(out _).Should().BeNull("the session no longer watches the channel it dropped");
    }

    [Fact]
    public void Drain_AdvancesPastWhatItAlreadySent()
    {
        using var hub = new RemoteEventHub(new NoireRemoteOptions());
        using var channel = new ConsoleLiveChannel(hub, Guid.NewGuid(), 16);

        hub.Publish("call.one", null);
        hub.Publish("call.two", null);

        var first = RoundTrip(channel.Drain(out _)!);

        first.Op.Should().Be(NoireRemoteConsoleLiveOps.Events);
        first.Events.Should().HaveCount(2);
        first.Cursors![NoireRemoteChannels.Events].Should().Be(2);

        hub.Publish("call.three", null);

        var second = channel.Drain(out _);

        second!.Events.Should().ContainSingle(item => item.Topic == "call.three");
        second.Cursors![NoireRemoteChannels.Events].Should().Be(3);

        channel.Drain(out _).Should().BeNull("everything published has been sent");
    }

    [Fact]
    public void Drain_WithACursorBehindTheBuffer_CarriesTheMissedMarker()
    {
        using var hub = new RemoteEventHub(new NoireRemoteOptions { EventBufferSize = 2 });
        using var channel = new ConsoleLiveChannel(hub, Guid.NewGuid(), 16);

        for (var index = 0; index < 4; index++)
            hub.Publish("call.done", null);

        channel.Handle(NoireRemoteJson.Write(new NoireRemoteConsoleLiveCommand
        {
            Op = NoireRemoteConsoleLiveOps.Subscribe,
            Subscription = new NoireRemoteEventRequest
            {
                Channels = [NoireRemoteChannels.Events],
                Cursors = new Dictionary<string, long> { [NoireRemoteChannels.Events] = 1 },
            },
        }));

        var frame = RoundTrip(channel.Drain(out _)!);

        frame.Missed.Should().BeTrue();
        frame.MissedChannels.Should().Equal(NoireRemoteChannels.Events);
        frame.Events.Should().HaveCount(2, "the ring kept the two newest");

        hub.Publish("call.done", null);

        channel.Drain(out _)!.Missed.Should().BeFalse("the cursor caught up with the buffer");
    }

    [Fact]
    public void TokenOf_TakesTheOfferBesideTheProtocolName()
    {
        ConsoleLiveSocket.TokenOf(ConsoleLiveSocket.SubProtocol + ", abc123").Should().Be("abc123");
        ConsoleLiveSocket.TokenOf("abc123," + ConsoleLiveSocket.SubProtocol).Should().Be("abc123");
        ConsoleLiveSocket.TokenOf(ConsoleLiveSocket.SubProtocol).Should().BeNull();
        ConsoleLiveSocket.TokenOf(null).Should().BeNull();
    }

    [Fact]
    public void Schedule_HandsThePageABackoffRatherThanAFlatRetry()
    {
        var schedule = ConsoleLiveSocket.Schedule();

        schedule.InitialDelayMs.Should().Be(1000);
        schedule.Multiplier.Should().Be(2.0);
        schedule.MaxDelayMs.Should().Be(30000);
        schedule.Jitter.Should().Be(0.25);
    }

    [Fact]
    public void Reconnect_DoublesToItsCeilingInsideTheJitterBand()
    {
        var policy = ConsoleLiveSocket.Reconnect;

        policy.DelayFor(0, 0.5).Should().Be(TimeSpan.FromSeconds(1));
        policy.DelayFor(1, 0.5).Should().Be(TimeSpan.FromSeconds(2));
        policy.DelayFor(5, 0.5).Should().Be(TimeSpan.FromSeconds(30));
        policy.DelayFor(50, 0.5).Should().Be(TimeSpan.FromSeconds(30));
        policy.DelayFor(0, 0).Should().Be(TimeSpan.FromMilliseconds(750));
        policy.DelayFor(0, 1).Should().Be(TimeSpan.FromMilliseconds(1250));
    }

    [Fact]
    public void ThePage_UpgradesFirstAndKeepsTheStreamToFallBackOn()
    {
        using var server = NewConsole();
        var page = server.Console.PageSource();

        page.Should().Contain("new WebSocket(");
        page.Should().Contain(NoireRemoteConsolePaths.Live);
        page.Should().Contain("/noire/v1/_stream", "a caller with no browser reads that route with a plain socket");
        page.Should().Contain("boot.reconnect");
        page.Should().NotContain("setTimeout(openStream", "the flat retry is replaced by the backoff the clients use");
    }

    // 'self' does not cover a WebSocket scheme everywhere. The policy must name the upgrade address.
    [Fact]
    public async Task ThePagePolicy_NamesTheAddressTheLiveChannelUpgradesOn()
    {
        using var server = NewConsole();

        using var response = await client.GetAsync(
            "http://127.0.0.1:" + server.Console.Port + NoireRemoteConsolePaths.Page,
            TestContext.Current.CancellationToken);

        var policy = string.Join(" ", response.Headers.GetValues("Content-Security-Policy"));

        policy.Should().Contain("connect-src 'self' ws://127.0.0.1:" + server.Console.Port);
        policy.Should().NotContain("unsafe-inline", "the page's script and style stay inline under a nonce");
    }

    [Fact]
    public async Task TheBootstrap_NamesTheLiveChannelAndItsSchedule()
    {
        using var server = NewConsole();
        var token = await OpenSessionAsync(server);

        var bootstrap = await ReadBootstrapAsync(server, token);

        bootstrap.LivePath.Should().Be(NoireRemoteConsolePaths.Live);
        bootstrap.LiveProtocol.Should().Be(ConsoleLiveSocket.SubProtocol);
        bootstrap.Reconnect!.MaxDelayMs.Should().Be(ConsoleLiveSocket.Schedule().MaxDelayMs);
    }

    [Fact]
    public async Task TheHandshake_WithoutASessionToken_IsRefused()
    {
        using var server = NewConsole();
        using var socket = new ClientWebSocket();

        socket.Options.AddSubProtocol(ConsoleLiveSocket.SubProtocol);

        var connect = async () => await socket.ConnectAsync(LiveUri(server), TestContext.Current.CancellationToken);

        await connect.Should().ThrowAsync<WebSocketException>();
    }

    [Fact]
    public async Task TheChannel_OverASession_CarriesEventsAndTakesASubscribe()
    {
        using var server = NewConsole();
        var token = await OpenSessionAsync(server);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var socket = new ClientWebSocket();

        socket.Options.AddSubProtocol(ConsoleLiveSocket.SubProtocol);
        socket.Options.AddSubProtocol(token);

        await socket.ConnectAsync(LiveUri(server), cancellation.Token);

        socket.SubProtocol.Should().Be(ConsoleLiveSocket.SubProtocol);
        (await ReadFrameAsync(socket, cancellation.Token)).Op.Should().Be(NoireRemoteConsoleLiveOps.Ready);

        await SendAsync(socket, new NoireRemoteConsoleLiveCommand
        {
            Op = NoireRemoteConsoleLiveOps.Subscribe,
            Subscription = new NoireRemoteEventRequest { Channels = [NoireRemoteChannels.Log] },
        }, cancellation.Token);

        var subscribed = await ReadUntilAsync(socket, frame => frame.Op == NoireRemoteConsoleLiveOps.Subscribed, cancellation.Token);
        subscribed.Channels.Should().Equal(NoireRemoteChannels.Log);

        server.PublishEvent(NoireRemoteChannels.Log, "listener.line", new { message = "opened" });

        var events = await ReadUntilAsync(
            socket,
            frame => frame.Op == NoireRemoteConsoleLiveOps.Events && frame.Events != null && frame.Events.Count > 0,
            cancellation.Token);

        events.Events.Should().Contain(item => item.Topic == "listener.line");
        events.Cursors.Should().ContainKey(NoireRemoteChannels.Log);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellation.Token);
    }

    private static NoireRemoteServer NewConsole()
    {
        var server = new NoireRemoteServer(new NoireRemoteStandaloneHost { Name = "console-live-probe" }, new NoireRemoteOptions
        {
            AutoStart = false,
            PublishDirectoryRecord = false,
            EnableLogging = false,
            EnableConsole = true,
        });

        server.Start();

        return server;
    }

    private static Uri LiveUri(NoireRemoteServer server)
        => new("ws://127.0.0.1:" + server.Console.Port + NoireRemoteConsolePaths.Live);

    private async Task<string> OpenSessionAsync(NoireRemoteServer server)
    {
        using var response = await client.PostAsync(
            "http://127.0.0.1:" + server.Console.Port + NoireRemoteConsolePaths.Session,
            new StringContent(NoireRemoteJson.Write(new NoireRemoteConsoleGrant()), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return NoireRemoteJson.Read<NoireRemoteConsoleSession>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.Token;
    }

    private async Task<NoireRemoteConsoleBootstrap> ReadBootstrapAsync(NoireRemoteServer server, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:" + server.Console.Port + NoireRemoteConsolePaths.Bootstrap);
        request.Headers.TryAddWithoutValidation(NoireRemoteHeaders.Authorization, NoireRemoteHeaders.ConsoleScheme + " " + token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return NoireRemoteJson.Read<NoireRemoteConsoleBootstrap>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
    }

    private static Task SendAsync(ClientWebSocket socket, NoireRemoteConsoleLiveCommand command, CancellationToken cancellationToken)
        => socket.SendAsync(NoireRemoteJson.WriteBytes(command), WebSocketMessageType.Text, true, cancellationToken);

    private static async Task<NoireRemoteConsoleLiveFrame> ReadFrameAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var text = new StringBuilder();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("The channel closed with " + result.CloseStatusDescription + ".");

            text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (result.EndOfMessage)
                break;
        }

        return NoireRemoteJson.Read<NoireRemoteConsoleLiveFrame>(text.ToString())!;
    }

    private static async Task<NoireRemoteConsoleLiveFrame> ReadUntilAsync(
        ClientWebSocket socket, Func<NoireRemoteConsoleLiveFrame, bool> matches, CancellationToken cancellationToken)
    {
        while (true)
        {
            var frame = await ReadFrameAsync(socket, cancellationToken);

            if (matches(frame))
                return frame;
        }
    }

    // Round-tripped like the page would.
    private static NoireRemoteConsoleLiveFrame RoundTrip(NoireRemoteConsoleLiveFrame frame)
        => NoireRemoteJson.Read<NoireRemoteConsoleLiveFrame>(NoireRemoteJson.Write(frame))!;
}
