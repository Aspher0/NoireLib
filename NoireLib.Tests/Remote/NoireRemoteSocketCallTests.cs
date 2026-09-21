using FluentAssertions;
using NoireLib.Remote;
using NoireLib.Websocket;
using System;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The API socket at <c>/noire/ws/_api</c>: a call frame is answered by a result frame, a member kept off the socket
/// is refused there, and nothing a caller sends closes the connection. A socket that closed on a bad frame would
/// cost the caller every subscription it holds.
/// </summary>
public sealed class NoireRemoteSocketCallTests
{
    [NoireRemoteClass("Socket")]
    public static class SocketProbe
    {
        [NoireRemote]
        public static int Double(int value) => value * 2;

        [NoireRemote]
        public static int Throws() => throw new InvalidOperationException("the member said no");

        [NoireRemote(Transports = NoireRemoteTransport.Http)]
        public static int HttpOnly() => 3;
    }

    [Fact]
    public async Task ACallFrame_GetsItsResult()
    {
        using var harness = new WebsocketHarness();
        harness.Http.Start();
        using var publication = harness.Http.PublishType(typeof(SocketProbe));

        var answer = await CallAsync(harness, 1, "Socket", "Double", """{"value":21}""");

        answer.Kind.Should().Be(NoireRemoteFrameKind.Result);
        answer.Id.Should().Be(1);
        answer.Payload!.ToObject<int>().Should().Be(42);
    }

    [Fact]
    public async Task AMemberKeptOffTheSocket_IsRefusedThere()
    {
        using var harness = new WebsocketHarness();
        harness.Http.Start();
        using var publication = harness.Http.PublishType(typeof(SocketProbe));

        var answer = await CallAsync(harness, 2, "Socket", "HttpOnly");

        answer.Error!.Code.Should().Be(NoireRemoteErrorCodes.TransportNotServed);
    }

    [Fact]
    public async Task AnUnknownMember_AnswersAnErrorAndKeepsTheConnection()
    {
        using var harness = new WebsocketHarness();
        harness.Http.Start();
        using var publication = harness.Http.PublishType(typeof(SocketProbe));

        var peer = harness.Connect(harness.Sockets.GetEndpoint(NoireRemotePaths.ApiSocket)!);

        var answer = await ExchangeAsync(peer, new NoireRemoteFrame
        {
            Kind = NoireRemoteFrameKind.Call,
            Id = 3,
            Api = "Socket",
            Member = "Nope",
        });

        answer.Error!.Code.Should().Be(NoireRemoteErrorCodes.UnknownMember);
        peer.Connection.State.Should().Be(NoireSocketState.Connected);
    }

    [Fact]
    public async Task AMalformedFrame_AnswersAnErrorAndKeepsTheConnection()
    {
        using var harness = new WebsocketHarness();
        harness.Http.Start();

        var peer = harness.Connect(harness.Sockets.GetEndpoint(NoireRemotePaths.ApiSocket)!);

        await peer.SendTextAsync("not json");
        var answer = ReadFrame(await peer.ReadFrameAsync());

        answer.Error!.Code.Should().Be(NoireRemoteErrorCodes.FrameMalformed);
        peer.Connection.State.Should().Be(NoireSocketState.Connected);
    }

    [Fact]
    public async Task AMemberThatThrows_AnswersTheFaultRatherThanDropping()
    {
        using var harness = new WebsocketHarness();
        harness.Http.Start();
        using var publication = harness.Http.PublishType(typeof(SocketProbe));

        var answer = await CallAsync(harness, 4, "Socket", "Throws");

        answer.Error!.Code.Should().Be(NoireRemoteErrorCodes.HandlerFault);
        answer.Error.Message.Should().Contain("the member said no");
    }

    [Fact]
    public async Task APing_IsAnsweredByAPong()
    {
        using var harness = new WebsocketHarness();
        harness.Http.Start();

        var peer = harness.Connect(harness.Sockets.GetEndpoint(NoireRemotePaths.ApiSocket)!);

        var answer = await ExchangeAsync(peer, new NoireRemoteFrame { Kind = NoireRemoteFrameKind.Ping, Id = 9 });

        answer.Kind.Should().Be(NoireRemoteFrameKind.Pong);
        answer.Id.Should().Be(9);
    }

    private static async Task<NoireRemoteFrame> CallAsync(WebsocketHarness harness, long id, string api, string member, string? payload = null)
    {
        var peer = harness.Connect(harness.Sockets.GetEndpoint(NoireRemotePaths.ApiSocket)!);

        return await ExchangeAsync(peer, new NoireRemoteFrame
        {
            Kind = NoireRemoteFrameKind.Call,
            Id = id,
            Api = api,
            Member = member,
            Payload = payload == null ? null : Newtonsoft.Json.Linq.JToken.Parse(payload),
        });
    }

    private static async Task<NoireRemoteFrame> ExchangeAsync(WebsocketTestPeer peer, NoireRemoteFrame frame)
    {
        await peer.SendTextAsync(frame.Write());

        return ReadFrame(await peer.ReadFrameAsync());
    }

    // A server frame is never masked. The payload starts two, four or ten bytes in.
    private static NoireRemoteFrame ReadFrame(byte[] frame)
    {
        var length = frame[1] & 0x7F;
        var offset = length <= 125 ? 2 : length == 126 ? 4 : 10;

        return NoireRemoteFrame.Parse(Encoding.UTF8.GetString(frame.AsSpan(offset)));
    }
}
