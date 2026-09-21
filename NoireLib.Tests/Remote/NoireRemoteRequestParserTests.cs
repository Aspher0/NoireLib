using FluentAssertions;
using NoireLib.Remote;
using NoireLib.Remote.Internal;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Feeds malformed bytes straight into the reader. The parser runs inside a game process on input an attacker can
/// reach. Every one of these has to come back as a status, never an exception.
/// </summary>
public sealed class NoireRemoteRequestParserTests
{
    private static readonly NoireRemoteOptions Options = new();

    private static async Task<HttpHeaderReadResult> ReadAsync(string raw, NoireRemoteOptions? options = null)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(raw));
        return await HttpRequestParser.ReadHeadersAsync(stream, options ?? Options, CancellationToken.None);
    }

    private const string Wellformed =
        "POST /noire/v1/MyApi/Ping HTTP/1.1\r\nHost: 127.0.0.1:47821\r\nContent-Length: 2\r\n\r\n{}";

    [Fact]
    public async Task AWellFormedRequest_Parses()
    {
        var result = await ReadAsync(Wellformed);

        result.Failure.Should().BeNull();
        result.Request!.Method.Should().Be("POST");
        result.Request.Path.Should().Be("/noire/v1/MyApi/Ping");
        result.Request.Header("host").Should().Be("127.0.0.1:47821");
        result.Request.DeclaredBodyLength.Should().Be(2);
    }

    [Fact]
    public async Task TheBody_IsReadAfterTheHeaders()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(Wellformed));
        var headers = await HttpRequestParser.ReadHeadersAsync(stream, Options, CancellationToken.None);

        var failure = await HttpRequestParser.ReadBodyAsync(stream, headers, CancellationToken.None);

        failure.Should().BeNull();
        Encoding.UTF8.GetString(headers.Request!.Body).Should().Be("{}");
    }

    [Fact]
    public async Task ABodySplitAcrossReads_IsAssembled()
    {
        var body = new string('a', 300);
        var raw = "POST /noire/v1/X/Y HTTP/1.1\r\nHost: h\r\nContent-Length: " + body.Length + "\r\n\r\n" + body;

        using var stream = new SlowStream(Encoding.ASCII.GetBytes(raw), 7);
        var headers = await HttpRequestParser.ReadHeadersAsync(stream, Options, CancellationToken.None);
        var failure = await HttpRequestParser.ReadBodyAsync(stream, headers, CancellationToken.None);

        failure.Should().BeNull();
        Encoding.UTF8.GetString(headers.Request!.Body).Should().Be(body);
    }

    [Fact]
    public async Task AnEmptyStream_IsABadRequest()
    {
        var result = await ReadAsync(string.Empty);

        result.Failure.Should().NotBeNull();
        result.Failure!.Status.Should().Be(400);
        result.Failure.Code.Should().Be(NoireRemoteErrorCodes.BadRequest);
    }

    [Fact]
    public async Task ARequestLineWithTwoParts_IsABadRequest()
    {
        var result = await ReadAsync("GET /noire/v1/_ping\r\nHost: h\r\n\r\n");

        result.Failure!.Status.Should().Be(400);
    }

    [Fact]
    public async Task AnUnknownProtocolVersion_IsABadRequest()
    {
        var result = await ReadAsync("GET /noire/v1/_ping HTTP/2.0\r\nHost: h\r\n\r\n");

        result.Failure!.Status.Should().Be(400);
        result.Failure.Message.Should().Contain("HTTP/1.1");
    }

    [Fact]
    public async Task AChunkedBody_IsRefused()
    {
        var result = await ReadAsync("POST /noire/v1/X/Y HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\n");

        result.Failure!.Status.Should().Be(400);
        result.Failure.Message.Should().Contain("chunked");
    }

    [Fact]
    public async Task AnUpgradeRequest_IsRefused()
    {
        var result = await ReadAsync("GET /noire/v1/_ping HTTP/1.1\r\nHost: h\r\nUpgrade: websocket\r\n\r\n");

        result.Failure!.Status.Should().Be(400);
    }

    [Fact]
    public async Task AFoldedHeaderLine_IsRefused()
    {
        var result = await ReadAsync("GET /noire/v1/_ping HTTP/1.1\r\nHost: h\r\n  continued\r\n\r\n");

        result.Failure!.Status.Should().Be(400);
    }

    [Fact]
    public async Task AHeaderLineWithNoName_IsRefused()
    {
        var result = await ReadAsync("GET /noire/v1/_ping HTTP/1.1\r\n: nothing\r\n\r\n");

        result.Failure!.Status.Should().Be(400);
    }

    [Fact]
    public async Task AContentLengthThatIsNotANumber_IsRefused()
    {
        var result = await ReadAsync("POST /noire/v1/X/Y HTTP/1.1\r\nHost: h\r\nContent-Length: many\r\n\r\n");

        result.Failure!.Status.Should().Be(400);
    }

    [Fact]
    public async Task ABodyLargerThanTheCap_IsRefusedBeforeItIsRead()
    {
        var options = new NoireRemoteOptions { MaxRequestBytes = 16 };
        var result = await ReadAsync("POST /noire/v1/X/Y HTTP/1.1\r\nHost: h\r\nContent-Length: 4096\r\n\r\n", options);

        result.Failure!.Status.Should().Be(413);
        result.Failure.Code.Should().Be(NoireRemoteErrorCodes.TooLarge);
    }

    [Fact]
    public async Task ARequestLineLongerThanTheCap_IsRefused()
    {
        var options = new NoireRemoteOptions { MaxRequestLineBytes = 32 };
        var result = await ReadAsync("GET /noire/v1/" + new string('x', 200) + " HTTP/1.1\r\nHost: h\r\n\r\n", options);

        result.Failure!.Status.Should().Be(413);
    }

    [Fact]
    public async Task MoreHeadersThanTheCap_IsRefused()
    {
        var options = new NoireRemoteOptions { MaxHeaderCount = 3 };
        var builder = new StringBuilder("GET /noire/v1/_ping HTTP/1.1\r\n");

        for (var index = 0; index < 10; index++)
            builder.Append("X-Probe-").Append(index).Append(": value\r\n");

        builder.Append("\r\n");

        var result = await ReadAsync(builder.ToString(), options);

        result.Failure!.Status.Should().Be(413);
    }

    [Fact]
    public async Task AHeaderBlockLargerThanTheCap_IsRefused()
    {
        var options = new NoireRemoteOptions { MaxHeaderBytes = 256 };
        var builder = new StringBuilder("GET /noire/v1/_ping HTTP/1.1\r\n");

        for (var index = 0; index < 40; index++)
            builder.Append("X-Probe-").Append(index).Append(": ").Append(new string('v', 40)).Append("\r\n");

        builder.Append("\r\n");

        var result = await ReadAsync(builder.ToString(), options);

        result.Failure!.Status.Should().Be(413);
    }

    [Fact]
    public async Task ATruncatedBody_IsRefused()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(
            "POST /noire/v1/X/Y HTTP/1.1\r\nHost: h\r\nContent-Length: 64\r\n\r\nshort"));

        var headers = await HttpRequestParser.ReadHeadersAsync(stream, Options, CancellationToken.None);
        var failure = await HttpRequestParser.ReadBodyAsync(stream, headers, CancellationToken.None);

        failure.Should().NotBeNull();
        failure!.Status.Should().Be(400);
    }

    [Fact]
    public async Task AQueryString_IsSplitOffThePath()
    {
        var result = await ReadAsync("GET /noire/v1/_ping?probe=1 HTTP/1.1\r\nHost: h\r\n\r\n");

        result.Request!.Path.Should().Be("/noire/v1/_ping");
        result.Request.Query.Should().Be("probe=1");
    }

    [Fact]
    public async Task ARepeatedHeader_IsJoinedRatherThanDropped()
    {
        var result = await ReadAsync("GET /noire/v1/_ping HTTP/1.1\r\nHost: h\r\nX-Probe: one\r\nX-Probe: two\r\n\r\n");

        result.Request!.Header("x-probe").Should().Be("one,two");
    }

    [Fact]
    public async Task HeaderNames_AreMatchedIgnoringCase()
    {
        var result = await ReadAsync("GET /noire/v1/_ping HTTP/1.1\r\nHOST: h\r\nAuThOrIzAtIoN: Bearer x\r\n\r\n");

        result.Request!.Header("host").Should().Be("h");
        result.Request.Header("Authorization").Should().Be("Bearer x");
    }

    private sealed class SlowStream(byte[] content, int chunk) : Stream
    {
        private int position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => content.Length;

        public override long Position { get => position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = Math.Min(Math.Min(chunk, count), content.Length - position);

            if (take <= 0)
                return 0;

            Array.Copy(content, position, buffer, offset, take);
            position += take;

            return take;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
