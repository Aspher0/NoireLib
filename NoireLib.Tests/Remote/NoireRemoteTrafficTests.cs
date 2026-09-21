using FluentAssertions;
using Newtonsoft.Json.Linq;
using NoireLib.Remote;
using NoireLib.Websocket;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The traffic channel: what the listener is serving, published as it serves it. A page holding the live socket
/// shows a call landing without asking for it. The cost when nothing is watching is the point of most of these.
/// </summary>
[Collection("NoireRemote")]
public sealed class NoireRemoteTrafficTests : NoireRemoteTestBase
{
    private HttpClient client = null!;
    private string baseUrl = null!;

    [NoireRemoteClass("Traffic", Thread = NoireRemoteThread.Background, Requires = NoireRemoteReadiness.None)]
    public static class TrafficProbe
    {
        [NoireRemote]
        public static int Twice(int value) => value * 2;

        [NoireRemote]
        public static int Refuses() => throw new InvalidOperationException("no");
    }

    public override void Dispose()
    {
        client?.Dispose();
        base.Dispose();
    }

    private void StartListener()
    {
        NoireRemote.PublishType(typeof(TrafficProbe));
        NoireRemote.Start();

        baseUrl = "http://127.0.0.1:" + NoireRemote.Port;
        client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", NoireRemote.Token);
    }

    private Task<NoireRemoteEventBatch> WatchAsync(int waitMs = 5000)
        => PollAsync(new NoireRemoteEventRequest
        {
            Channels = [NoireRemoteChannels.Traffic],
            Since = 0,
            WaitMs = waitMs,
        });

    private async Task<NoireRemoteEventBatch> PollAsync(NoireRemoteEventRequest request)
    {
        using var content = new StringContent(NoireRemoteJson.Write(request), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(baseUrl + NoireRemotePaths.Prefix + NoireRemotePaths.Events, content);

        return NoireRemoteJson.Read<NoireRemoteEventBatch>(await response.Content.ReadAsStringAsync())!;
    }

    private async Task CallAsync(string member, string? args = null)
    {
        var body = new NoireRemoteRequest { Args = args == null ? null : JObject.Parse(args) };

        using var content = new StringContent(NoireRemoteJson.Write(body), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(baseUrl + NoireRemotePaths.Prefix + "Traffic/" + member, content);

        await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task AFinishedCall_ReachesAWatcherWithItsWireAndItsTime()
    {
        StartListener();

        var watching = WatchAsync();

        await Task.Delay(150, TestContext.Current.CancellationToken);
        await CallAsync("Twice", "{\"value\":21}");

        var batch = await watching;
        var call = batch.Events.Should().ContainSingle(published => published.Topic == NoireRemoteTrafficTopics.Call).Subject;
        var report = call.Data!.ToObject<NoireRemoteTrafficCall>()!;

        report.Wire.Should().Be("http");
        report.Endpoint.Should().Be("Traffic");
        report.Member.Should().Be("Twice");
        report.Ok.Should().BeTrue();
        report.ElapsedMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task AFailedCall_SaysWhyOnTheChannel()
    {
        StartListener();

        var watching = WatchAsync();

        await Task.Delay(150, TestContext.Current.CancellationToken);
        await CallAsync("Refuses");

        var batch = await watching;
        var report = batch.Events[0].Data!.ToObject<NoireRemoteTrafficCall>()!;

        report.Ok.Should().BeFalse();
        report.ErrorCode.Should().Be(NoireRemoteErrorCodes.HandlerFault);
    }

    [Fact]
    public async Task ASocketConnection_ReachesAWatcherWhenItOpensAndWhenItCloses()
    {
        StartListener();

        using var endpoint = NoireWebsocket.Publish("watched");
        using var caller = new NoireWebsocketClient(
            NoireWebsocket.LocalUrl("watched"),
            new NoireWebsocketClientOptions { Retry = NoireRetryPolicy.None });

        caller.Options.Http.Headers[NoireRemoteHeaders.Authorization] = NoireRemoteHeaders.BearerScheme + " " + NoireRemote.Token;

        var watching = WatchAsync();

        await Task.Delay(150, TestContext.Current.CancellationToken);
        await caller.ConnectAsync(TestContext.Current.CancellationToken);

        var batch = await watching;
        var opened = batch.Events[0].Data!.ToObject<NoireRemoteTrafficSocket>()!;

        opened.Socket.Should().Be("watched");
        opened.State.Should().Be("open");
    }

    [Fact]
    public async Task AFrame_StaysOffTheChannelUntilItIsAskedFor()
    {
        StartListener();

        using var endpoint = NoireWebsocket.Publish("quiet");

        endpoint.OnMessage((connection, message) => connection.Send("back: " + message.Text));

        using var caller = new NoireWebsocketClient(
            NoireWebsocket.LocalUrl("quiet"),
            new NoireWebsocketClientOptions { Retry = NoireRetryPolicy.None });

        caller.Options.Http.Headers[NoireRemoteHeaders.Authorization] = NoireRemoteHeaders.BearerScheme + " " + NoireRemote.Token;

        await caller.ConnectAsync(TestContext.Current.CancellationToken);

        NoireRemote.Options.PublishTrafficFrames.Should().BeFalse();

        var quiet = WatchAsync(700);

        await Task.Delay(150, TestContext.Current.CancellationToken);
        await caller.SendAsync("one", TestContext.Current.CancellationToken);

        (await quiet).Events.Should().NotContain(published => published.Topic == NoireRemoteTrafficTopics.Frame);

        NoireRemote.Options.PublishTrafficFrames = true;

        var watching = WatchAsync();

        await Task.Delay(150, TestContext.Current.CancellationToken);
        await caller.SendAsync("two", TestContext.Current.CancellationToken);

        var batch = await watching;
        var frame = batch.Events.Should().Contain(published => published.Topic == NoireRemoteTrafficTopics.Frame)
            .Which.Data!.ToObject<NoireRemoteTrafficFrame>()!;

        frame.Socket.Should().Be("quiet");
        frame.Direction.Should().Be("in");
        frame.Preview.Should().Be("two");
    }

    [Fact]
    public async Task TurningTheFeedOff_LeavesTheChannelEmpty()
    {
        StartListener();

        NoireRemote.Options.PublishTraffic = false;

        var watching = WatchAsync(700);

        await Task.Delay(150, TestContext.Current.CancellationToken);
        await CallAsync("Twice", "{\"value\":1}");

        (await watching).Events.Should().BeEmpty();
    }

    [Fact]
    public async Task NothingWatching_PublishesNothing()
    {
        StartListener();

        await CallAsync("Twice", "{\"value\":3}");
        await CallAsync("Twice", "{\"value\":4}");

        // With no page open the ring must stay empty. Nothing is built per call.
        var batch = await PollAsync(new NoireRemoteEventRequest
        {
            Channels = [NoireRemoteChannels.Traffic],
            Since = 0,
            WaitMs = 0,
        });

        batch.Events.Should().BeEmpty();
    }

    [Fact]
    public void TheChannelIsDeclared_SoAPageSubscribesToItWithoutBeingTold()
    {
        StartListener();

        NoireRemote.Manifest().Channels.Should().Contain(channel => channel.Name == NoireRemoteChannels.Traffic);
    }
}
