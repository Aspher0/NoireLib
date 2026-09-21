using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote.Internal;

// Header keys are lowercase.
internal sealed class HttpRequestData
{
    public string Method { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Query { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public Dictionary<string, string> Headers { get; } = new(StringComparer.Ordinal);

    // Forwarded by the listener's own console. A member's wire restriction does not apply.
    public bool FromConsole { get; set; }

    public byte[] Body { get; set; } = [];

    public int DeclaredBodyLength { get; set; }

    public bool IsWebsocketUpgrade { get; set; }

    public string? Header(string name)
        => Headers.TryGetValue(name.ToLowerInvariant(), out var value) ? value : null;

    public bool HasHeader(string name)
        => Headers.ContainsKey(name.ToLowerInvariant());

    // A batch entry routes as its own request with the carrier's headers. The idempotency key stays behind.
    public void CopyHeadersFrom(HttpRequestData source)
    {
        foreach (var pair in source.Headers)
            Headers[pair.Key] = pair.Value;
    }
}

// A refusal decided before a member could run.
internal sealed class HttpFailure(int status, string code, string message, string? detail = null)
{
    public int Status { get; } = status;

    public string Code { get; } = code;

    public string Message { get; } = message;

    public string? Detail { get; } = detail;

    public double? RetryAfterSeconds { get; set; }

    public IReadOnlyList<string>? Candidates { get; set; }

    // Such as the version a 426 names. Nothing from the request reaches them.
    public IReadOnlyList<KeyValuePair<string, string>>? ExtraHeaders { get; set; }
}

// The parsed request or a refusal, plus the body bytes that came with the headers.
internal sealed class HttpHeaderReadResult
{
    public HttpRequestData? Request { get; set; }

    public HttpFailure? Failure { get; set; }

    public byte[] Buffer { get; set; } = [];

    public int BodyStart { get; set; }

    public int Filled { get; set; }

    // For a bodyless upgrade these are the peer's first frame, and nothing else reads them.
    public ReadOnlyMemory<byte> Carried => Buffer.AsMemory(BodyStart, Math.Max(0, Filled - BodyStart));

    public static HttpHeaderReadResult Fail(int status, string code, string message)
        => new() { Failure = new HttpFailure(status, code, message) };
}

// A request line, a header block and a body of declared length. No chunked bodies, no pipelining. The one upgrade is a bodyless GET to WebSocket.
// Every cap is checked before the allocation it bounds.
internal static class HttpRequestParser
{
    private const int ReadChunk = 4096;

    public static async Task<HttpHeaderReadResult> ReadHeadersAsync(Stream stream, NoireRemoteOptions options, CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Max(1024, Math.Min(options.MaxHeaderBytes, 64 * 1024))];
        var filled = 0;
        var blockEnd = -1;

        while (blockEnd < 0)
        {
            if (filled == buffer.Length)
                return HttpHeaderReadResult.Fail(413, NoireRemoteErrorCodes.TooLarge, "The header block is larger than this listener accepts.");

            var read = await stream.ReadAsync(buffer.AsMemory(filled, Math.Min(ReadChunk, buffer.Length - filled)), cancellationToken).ConfigureAwait(false);

            if (read <= 0)
                return HttpHeaderReadResult.Fail(400, NoireRemoteErrorCodes.BadRequest, "The connection closed before a complete request arrived.");

            var searchFrom = Math.Max(0, filled - 3);
            filled += read;
            blockEnd = FindHeaderEnd(buffer, searchFrom, filled);
        }

        var parsed = ParseHeaderBlock(buffer, blockEnd, options, out var failure);

        if (failure != null)
            return new HttpHeaderReadResult { Failure = failure };

        var lengthHeader = parsed!.Header("content-length");
        var length = 0;

        if (!string.IsNullOrEmpty(lengthHeader))
        {
            if (!int.TryParse(lengthHeader, NumberStyles.Integer, CultureInfo.InvariantCulture, out length) || length < 0)
                return HttpHeaderReadResult.Fail(400, NoireRemoteErrorCodes.BadRequest, "The content length is not a number.");
        }

        if (length > options.MaxRequestBytes)
            return HttpHeaderReadResult.Fail(413, NoireRemoteErrorCodes.TooLarge, "The body is larger than this listener accepts.");

        parsed.DeclaredBodyLength = length;

        return new HttpHeaderReadResult
        {
            Request = parsed,
            Buffer = buffer,
            BodyStart = blockEnd + 4,
            Filled = filled,
        };
    }

    public static async Task<HttpFailure?> ReadBodyAsync(Stream stream, HttpHeaderReadResult headers, CancellationToken cancellationToken)
    {
        var request = headers.Request!;
        var length = request.DeclaredBodyLength;

        if (length == 0)
        {
            request.Body = [];
            return null;
        }

        var body = new byte[length];
        var carried = Math.Min(headers.Filled - headers.BodyStart, length);

        if (carried > 0)
            Array.Copy(headers.Buffer, headers.BodyStart, body, 0, carried);

        var copied = carried;

        while (copied < length)
        {
            var read = await stream.ReadAsync(body.AsMemory(copied, length - copied), cancellationToken).ConfigureAwait(false);

            if (read <= 0)
                return new HttpFailure(400, NoireRemoteErrorCodes.BadRequest, "The connection closed before the whole body arrived.");

            copied += read;
        }

        request.Body = body;
        return null;
    }

    private static int FindHeaderEnd(byte[] buffer, int from, int filled)
    {
        for (var index = from; index + 3 < filled; index++)
        {
            if (buffer[index] == (byte)'\r' && buffer[index + 1] == (byte)'\n'
                && buffer[index + 2] == (byte)'\r' && buffer[index + 3] == (byte)'\n')
                return index;
        }

        return -1;
    }

    private static HttpRequestData? ParseHeaderBlock(byte[] buffer, int blockEnd, NoireRemoteOptions options, out HttpFailure? failure)
    {
        failure = null;

        var text = Encoding.ASCII.GetString(buffer, 0, blockEnd);
        var lines = text.Split(["\r\n"], StringSplitOptions.None);

        if (lines.Length == 0 || lines[0].Length == 0)
        {
            failure = new HttpFailure(400, NoireRemoteErrorCodes.BadRequest, "The request line is empty.");
            return null;
        }

        if (lines[0].Length > options.MaxRequestLineBytes)
        {
            failure = new HttpFailure(413, NoireRemoteErrorCodes.TooLarge, "The request line is longer than this listener accepts.");
            return null;
        }

        var parts = lines[0].Split(' ');

        if (parts.Length != 3)
        {
            failure = new HttpFailure(400, NoireRemoteErrorCodes.BadRequest, "The request line does not read as a method, a target and a version.");
            return null;
        }

        var request = new HttpRequestData
        {
            Method = parts[0].ToUpperInvariant(),
            Target = parts[1],
            Version = parts[2],
        };

        if (!request.Version.StartsWith("HTTP/1.", StringComparison.Ordinal))
        {
            failure = new HttpFailure(400, NoireRemoteErrorCodes.BadRequest, "This listener speaks HTTP/1.1 only.");
            return null;
        }

        var queryStart = request.Target.IndexOf('?');

        if (queryStart >= 0)
        {
            request.Path = request.Target.Substring(0, queryStart);
            request.Query = request.Target.Substring(queryStart + 1);
        }
        else
        {
            request.Path = request.Target;
        }

        if (lines.Length - 1 > options.MaxHeaderCount)
        {
            failure = new HttpFailure(413, NoireRemoteErrorCodes.TooLarge, "The request carries more headers than this listener accepts.");
            return null;
        }

        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];

            if (line.Length == 0)
                continue;

            if (line[0] == ' ' || line[0] == '\t')
            {
                failure = new HttpFailure(400, NoireRemoteErrorCodes.BadRequest, "A folded header line is not accepted.");
                return null;
            }

            var separator = line.IndexOf(':');

            if (separator <= 0)
            {
                failure = new HttpFailure(400, NoireRemoteErrorCodes.BadRequest, "A header line carries no name.");
                return null;
            }

            var name = line.Substring(0, separator).Trim().ToLowerInvariant();
            var value = line.Substring(separator + 1).Trim();

            // A repeated header is joined like HTTP defines.
            request.Headers[name] = request.Headers.TryGetValue(name, out var existing) ? existing + "," + value : value;
        }

        if (request.HasHeader("transfer-encoding"))
        {
            failure = new HttpFailure(400, NoireRemoteErrorCodes.BadRequest, "A chunked body is not accepted. Send a content length.");
            return null;
        }

        if (request.HasHeader("upgrade"))
        {
            if (!IsWebsocketUpgradeShape(request))
            {
                failure = new HttpFailure(400, NoireRemoteErrorCodes.BadRequest, "The only upgrade this listener accepts is a bodyless GET to WebSocket.");
                return null;
            }

            request.IsWebsocketUpgrade = true;
        }

        return request;
    }

    // A second protocol beside websocket, a body, or anything but GET is refused before the codec.
    private static bool IsWebsocketUpgradeShape(HttpRequestData request)
    {
        if (!string.Equals(request.Method, "GET", StringComparison.Ordinal))
            return false;

        if (!IsSoleToken(request.Header("upgrade"), "websocket"))
            return false;

        if (!ListsToken(request.Header("connection"), "upgrade"))
            return false;

        var length = request.Header("content-length");

        return string.IsNullOrEmpty(length)
            || (int.TryParse(length, NumberStyles.Integer, CultureInfo.InvariantCulture, out var declared) && declared == 0);
    }

    private static bool IsSoleToken(string? value, string token)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();

        return trimmed.IndexOf(',') < 0 && string.Equals(trimmed, token, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ListsToken(string? value, string token)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (var part in value.Split(','))
        {
            if (string.Equals(part.Trim(), token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
