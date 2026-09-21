using FluentAssertions;
using NoireLib.Remote;
using NoireLib.Remote.Internal;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The listener accepts exactly one upgrade shape and refuses every other one. These pin the four parts of that shape,
/// so the refusal cannot quietly widen into "any request carrying an Upgrade header reaches the router".
/// </summary>
public sealed class HttpUpgradeParsingTests
{
    private static readonly NoireRemoteOptions Options = new();

    private const string WellformedUpgrade =
        "GET /noire/ws/chat HTTP/1.1\r\nHost: 127.0.0.1:47821\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
        + "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n";

    private static async Task<HttpHeaderReadResult> ReadAsync(string raw)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(raw));
        return await HttpRequestParser.ReadHeadersAsync(stream, Options, CancellationToken.None);
    }

    [Fact]
    public async Task ReadHeadersAsync_AWellFormedUpgrade_ParsesAndIsFlagged()
    {
        var result = await ReadAsync(WellformedUpgrade);

        result.Failure.Should().BeNull();
        result.Request!.IsWebsocketUpgrade.Should().BeTrue();
        result.Request.Path.Should().Be("/noire/ws/chat");
    }

    [Fact]
    public async Task ReadHeadersAsync_AnOrdinaryRequest_IsNotFlagged()
    {
        var result = await ReadAsync("POST /noire/v1/X/Y HTTP/1.1\r\nHost: h\r\nContent-Length: 0\r\n\r\n");

        result.Failure.Should().BeNull();
        result.Request!.IsWebsocketUpgrade.Should().BeFalse();
    }

    [Fact]
    public async Task ReadHeadersAsync_AnUpgradeToH2c_IsRefused()
    {
        var result = await ReadAsync("GET /noire/ws/chat HTTP/1.1\r\nHost: h\r\nUpgrade: h2c\r\nConnection: Upgrade\r\n\r\n");

        result.Failure.Should().NotBeNull();
        result.Failure!.Status.Should().Be(400);
        result.Failure.Code.Should().Be(NoireRemoteErrorCodes.BadRequest);
    }

    [Fact]
    public async Task ReadHeadersAsync_AnUpgradeNamingASecondProtocol_IsRefused()
    {
        var result = await ReadAsync("GET /noire/ws/chat HTTP/1.1\r\nHost: h\r\nUpgrade: websocket, h2c\r\nConnection: Upgrade\r\n\r\n");

        result.Failure!.Status.Should().Be(400);
    }

    [Fact]
    public async Task ReadHeadersAsync_AnUpgradeCarryingABody_IsRefused()
    {
        var result = await ReadAsync(
            "GET /noire/ws/chat HTTP/1.1\r\nHost: h\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nContent-Length: 2\r\n\r\n{}");

        result.Failure!.Status.Should().Be(400);
    }

    [Fact]
    public async Task ReadHeadersAsync_AnUpgradeOnPost_IsRefused()
    {
        var result = await ReadAsync("POST /noire/ws/chat HTTP/1.1\r\nHost: h\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n\r\n");

        result.Failure!.Status.Should().Be(400);
    }

    [Fact]
    public async Task ReadHeadersAsync_AnUpgradeWithNoConnectionHeader_IsRefused()
    {
        var result = await ReadAsync("GET /noire/ws/chat HTTP/1.1\r\nHost: h\r\nUpgrade: websocket\r\n\r\n");

        result.Failure!.Status.Should().Be(400);
    }

    [Fact]
    public async Task ReadHeadersAsync_AConnectionHeaderNotListingTheToken_IsRefused()
    {
        var result = await ReadAsync("GET /noire/ws/chat HTTP/1.1\r\nHost: h\r\nUpgrade: websocket\r\nConnection: keep-alive\r\n\r\n");

        result.Failure!.Status.Should().Be(400);
    }

    [Fact]
    public async Task ReadHeadersAsync_AChunkedUpgrade_IsRefusedAsChunked()
    {
        var result = await ReadAsync(
            "GET /noire/ws/chat HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n\r\n");

        result.Failure!.Status.Should().Be(400);
        result.Failure.Message.Should().Contain("chunked", "the chunked refusal runs before the upgrade shape check");
    }

    [Fact]
    public async Task ReadHeadersAsync_AConnectionHeaderListingSeveralTokens_IsAccepted()
    {
        var result = await ReadAsync("GET /noire/ws/chat HTTP/1.1\r\nHost: h\r\nUpgrade: websocket\r\nConnection: keep-alive, Upgrade\r\n\r\n");

        result.Failure.Should().BeNull();
        result.Request!.IsWebsocketUpgrade.Should().BeTrue();
    }

    [Fact]
    public async Task ReadHeadersAsync_TheTokens_AreMatchedIgnoringCase()
    {
        var result = await ReadAsync("GET /noire/ws/chat HTTP/1.1\r\nHost: h\r\nupgrade: WebSocket\r\nconnection: UPGRADE\r\n\r\n");

        result.Failure.Should().BeNull();
        result.Request!.IsWebsocketUpgrade.Should().BeTrue();
    }

    [Fact]
    public async Task ReadHeadersAsync_AZeroContentLength_IsAccepted()
    {
        var result = await ReadAsync(
            "GET /noire/ws/chat HTTP/1.1\r\nHost: h\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nContent-Length: 0\r\n\r\n");

        result.Failure.Should().BeNull();
        result.Request!.IsWebsocketUpgrade.Should().BeTrue();
    }

    [Fact]
    public async Task Carried_HoldsTheBytesThatArrivedWithTheHeaderBlock()
    {
        var frame = new byte[] { 0x81, 0x85, 0x37, 0xFA, 0x21, 0x3D, 0x7F, 0x9F, 0x4D, 0x51, 0x58 };
        var request = Encoding.ASCII.GetBytes(WellformedUpgrade);
        var raw = new byte[request.Length + frame.Length];

        request.CopyTo(raw, 0);
        frame.CopyTo(raw, request.Length);

        using var stream = new MemoryStream(raw);
        var headers = await HttpRequestParser.ReadHeadersAsync(stream, Options, CancellationToken.None);

        headers.Carried.ToArray().Should().Equal(frame);
    }

    [Fact]
    public async Task ReadBodyAsync_ABodylessUpgrade_DropsTheCarriedBytes()
    {
        var request = Encoding.ASCII.GetBytes(WellformedUpgrade);
        var raw = new byte[request.Length + 4];

        request.CopyTo(raw, 0);

        using var stream = new MemoryStream(raw);
        var headers = await HttpRequestParser.ReadHeadersAsync(stream, Options, CancellationToken.None);
        var failure = await HttpRequestParser.ReadBodyAsync(stream, headers, CancellationToken.None);

        failure.Should().BeNull();
        headers.Request!.Body.Should().BeEmpty("a bodyless request reads no body; the adopt path reads Carried for its bytes");
        headers.Carried.Length.Should().Be(4);
    }
}
