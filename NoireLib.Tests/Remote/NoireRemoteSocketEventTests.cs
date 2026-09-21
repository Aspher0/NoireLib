using FluentAssertions;
using NoireLib.Remote;
using System;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Events pushed over the API socket: a caller subscribes to a topic prefix and receives what matches it as it
/// happens. This is what makes the HTTP channel machinery removable.
/// </summary>
public sealed class NoireRemoteSocketEventTests
{
    [NoireRemoteClass("Events")]
    public static class EventProbe
    {
        [NoireRemote]
        public static event Action<string>? OnMessage;

        internal static void Raise(string text) => OnMessage?.Invoke(text);
    }

    [Fact]
    public async Task ASubscribedSocket_ReceivesAnEvent()
    {
        using var harness = new WebsocketHarness();
        harness.Http.Start();
        using var publication = harness.Http.PublishType(typeof(EventProbe));

        var peer = harness.Connect(harness.Sockets.GetEndpoint(NoireRemotePaths.ApiSocket)!);
        await SubscribeAsync(peer, "events.");

        EventProbe.Raise("hello");
        var frame = ReadFrame(await peer.ReadFrameAsync());

        frame.Kind.Should().Be(NoireRemoteFrameKind.Event);
        frame.Member.Should().Be("events.onmessage");
        frame.Payload!.ToObject<string>().Should().Be("hello");
    }

    [Fact]
    public async Task ASocket_ReceivesNothingItDidNotSubscribeTo()
    {
        using var harness = new WebsocketHarness();
        harness.Http.Start();
        using var publication = harness.Http.PublishType(typeof(EventProbe));

        var peer = harness.Connect(harness.Sockets.GetEndpoint(NoireRemotePaths.ApiSocket)!);
        await SubscribeAsync(peer, "somethingelse.");

        EventProbe.Raise("hello");

        (await peer.TryReadFrameAsync(TimeSpan.FromMilliseconds(300))).Should().BeNull();
    }

    [Fact]
    public async Task Unsubscribing_StopsTheDelivery()
    {
        using var harness = new WebsocketHarness();
        harness.Http.Start();
        using var publication = harness.Http.PublishType(typeof(EventProbe));

        var peer = harness.Connect(harness.Sockets.GetEndpoint(NoireRemotePaths.ApiSocket)!);
        await SubscribeAsync(peer, "events.");
        await ExchangeAsync(peer, new NoireRemoteFrame { Kind = NoireRemoteFrameKind.Unsubscribe, Id = 2, Member = "events." });

        EventProbe.Raise("hello");

        (await peer.TryReadFrameAsync(TimeSpan.FromMilliseconds(300))).Should().BeNull();
    }

    [Fact]
    public async Task AnEvent_ReachesTwoSocketsAtOnce()
    {
        using var harness = new WebsocketHarness();
        harness.Http.Start();
        using var publication = harness.Http.PublishType(typeof(EventProbe));

        var first = harness.Connect(harness.Sockets.GetEndpoint(NoireRemotePaths.ApiSocket)!);
        var second = harness.Connect(harness.Sockets.GetEndpoint(NoireRemotePaths.ApiSocket)!);
        await SubscribeAsync(first, "events.");
        await SubscribeAsync(second, "events.");

        EventProbe.Raise("hello");

        ReadFrame(await first.ReadFrameAsync()).Member.Should().Be("events.onmessage");
        ReadFrame(await second.ReadFrameAsync()).Member.Should().Be("events.onmessage");
    }

    private static Task<NoireRemoteFrame> SubscribeAsync(WebsocketTestPeer peer, string prefix)
        => ExchangeAsync(peer, new NoireRemoteFrame { Kind = NoireRemoteFrameKind.Subscribe, Id = 1, Member = prefix });

    private static async Task<NoireRemoteFrame> ExchangeAsync(WebsocketTestPeer peer, NoireRemoteFrame frame)
    {
        await peer.SendTextAsync(frame.Write());

        return ReadFrame(await peer.ReadFrameAsync());
    }

    private static NoireRemoteFrame ReadFrame(byte[] frame)
    {
        var length = frame[1] & 0x7F;
        var offset = length <= 125 ? 2 : length == 126 ? 4 : 10;

        return NoireRemoteFrame.Parse(Encoding.UTF8.GetString(frame.AsSpan(offset)));
    }
}
