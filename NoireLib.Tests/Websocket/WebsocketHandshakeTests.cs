using FluentAssertions;
using NoireLib.Remote.Internal;
using NoireLib.Websocket.Internal;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the server side of RFC 6455 section 4.2: the accept value is the specification's own vector, a version this
/// listener does not speak answers 426 naming the one it does, a malformed key never reaches the codec, and the answer
/// declines every extension by carrying none.
/// </summary>
public sealed class WebsocketHandshakeTests
{
    private const string SampleKey = "dGhlIHNhbXBsZSBub25jZQ==";

    private const string SampleAccept = "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=";

    private static HttpRequestData Upgrade(string? key = SampleKey, string? version = "13", string? offered = null)
    {
        var request = new HttpRequestData { Method = "GET", Path = "/noire/ws/chat", IsWebsocketUpgrade = true };

        request.Headers["upgrade"] = "websocket";
        request.Headers["connection"] = "Upgrade";

        if (key != null)
            request.Headers["sec-websocket-key"] = key;

        if (version != null)
            request.Headers["sec-websocket-version"] = version;

        if (offered != null)
            request.Headers["sec-websocket-protocol"] = offered;

        return request;
    }

    [Fact]
    public void ComputeAccept_TheSpecificationVector_ProducesTheSpecificationAnswer()
    {
        WebsocketHandshake.ComputeAccept(SampleKey).Should().Be(SampleAccept);
    }

    [Fact]
    public void Negotiate_AWellFormedUpgrade_AcceptsWithThatValue()
    {
        var result = WebsocketHandshake.Negotiate(Upgrade(), null, false);

        result.Failure.Should().BeNull();
        result.Accept.Should().Be(SampleAccept);
        result.SubProtocol.Should().BeNull();
    }

    [Fact]
    public void Negotiate_ARequestThatIsNotAnUpgrade_IsRefused()
    {
        var request = Upgrade();
        request.IsWebsocketUpgrade = false;

        var result = WebsocketHandshake.Negotiate(request, null, false);

        result.Failure!.Status.Should().Be(400);
    }

    [Fact]
    public void Negotiate_AVersionThisListenerDoesNotSpeak_Answers426NamingTheOneItDoes()
    {
        var result = WebsocketHandshake.Negotiate(Upgrade(version: "8"), null, false);

        result.Failure!.Status.Should().Be(426);

        var header = result.Failure.ExtraHeaders.Should().ContainSingle().Subject;

        header.Key.Should().Be("Sec-WebSocket-Version");
        header.Value.Should().Be("13");
    }

    [Fact]
    public void Negotiate_NoVersionAtAll_Answers426()
    {
        var result = WebsocketHandshake.Negotiate(Upgrade(version: null), null, false);

        result.Failure!.Status.Should().Be(426);
    }

    [Fact]
    public void Negotiate_NoKey_IsRefused()
    {
        var result = WebsocketHandshake.Negotiate(Upgrade(key: null), null, false);

        result.Failure!.Status.Should().Be(400);
    }

    [Fact]
    public void Negotiate_AKeyOfTheWrongLength_IsRefused()
    {
        var result = WebsocketHandshake.Negotiate(Upgrade(key: "c2hvcnQ="), null, false);

        result.Failure!.Status.Should().Be(400);
    }

    [Fact]
    public void Negotiate_AKeyOfTwentyFourCharactersThatIsNotSixteenBytes_IsRefused()
    {
        var result = WebsocketHandshake.Negotiate(Upgrade(key: new string('A', 24)), null, false);

        result.Failure!.Status.Should().Be(400);
    }

    [Fact]
    public void Negotiate_AKeyThatIsNotBase64_IsRefused()
    {
        var result = WebsocketHandshake.Negotiate(Upgrade(key: new string('*', 24)), null, false);

        result.Failure!.Status.Should().Be(400);
    }

    [Fact]
    public void Negotiate_ASubProtocolBothSidesSpeak_EchoesTheFirstTheEndpointDeclares()
    {
        var result = WebsocketHandshake.Negotiate(Upgrade(offered: "superchat, chat"), ["chat", "superchat"], false);

        result.Failure.Should().BeNull();
        result.SubProtocol.Should().Be("chat", "the endpoint's own order decides this");
    }

    [Fact]
    public void Negotiate_ASubProtocolTheEndpointDoesNotDeclare_EchoesNothingAndStillAccepts()
    {
        var result = WebsocketHandshake.Negotiate(Upgrade(offered: "mqtt"), ["chat"], false);

        result.Failure.Should().BeNull();
        result.SubProtocol.Should().BeNull();
    }

    [Fact]
    public void Negotiate_NoMatchingSubProtocolWhenOneIsRequired_IsRefused()
    {
        var result = WebsocketHandshake.Negotiate(Upgrade(offered: "mqtt"), ["chat"], true);

        result.Failure!.Status.Should().Be(400);
        result.Failure.Detail.Should().Be("chat");
    }

    [Fact]
    public void Negotiate_AnEndpointDeclaringNothing_AcceptsEvenWhenOneIsRequired()
    {
        var result = WebsocketHandshake.Negotiate(Upgrade(offered: "chat"), null, true);

        result.Failure.Should().BeNull();
        result.SubProtocol.Should().BeNull();
    }

    [Fact]
    public async Task WriteUpgradeAsync_TheAnswer_CarriesNeitherALengthNorAClose()
    {
        var answer = await WriteUpgradeAsync(SampleAccept, null);

        answer.Should().StartWith("HTTP/1.1 101 Switching Protocols\r\n");
        answer.Should().Contain("Upgrade: websocket\r\n");
        answer.Should().Contain("Connection: Upgrade\r\n");
        answer.Should().Contain("Sec-WebSocket-Accept: " + SampleAccept + "\r\n");
        answer.Should().NotContain("Content-Length");
        answer.Should().NotContain("Connection: close");
        answer.Should().EndWith("\r\n\r\n");
    }

    [Fact]
    public async Task Negotiate_AnExtensionOffer_IsDeclinedByOmission()
    {
        var request = Upgrade();
        request.Headers["sec-websocket-extensions"] = "permessage-deflate; client_max_window_bits";

        var result = WebsocketHandshake.Negotiate(request, null, false);

        result.Failure.Should().BeNull("an offered extension is declined, never refused outright");

        var answer = await WriteUpgradeAsync(result.Accept, result.SubProtocol);

        answer.Should().NotContain("Sec-WebSocket-Extensions", "omitting the header is how RFC 6455 declines every offer");
    }

    [Fact]
    public async Task WriteUpgradeAsync_ASelectedSubProtocol_IsEchoed()
    {
        var answer = await WriteUpgradeAsync(SampleAccept, "chat");

        answer.Should().Contain("Sec-WebSocket-Protocol: chat\r\n");
    }

    private static async Task<string> WriteUpgradeAsync(string accept, string? subProtocol)
    {
        using var stream = new MemoryStream();
        await HttpResponseWriter.WriteUpgradeAsync(stream, accept, subProtocol, Guid.Empty, CancellationToken.None);

        return Encoding.ASCII.GetString(stream.ToArray());
    }
}
