using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote.Internal;

// Every answer is JSON, nothing from the request is reflected, and no header lets a browser read it.
internal static class HttpResponseWriter
{
    private static readonly Dictionary<int, string> Reasons = new()
    {
        [200] = "OK",
        [202] = "Accepted",
        [400] = "Bad Request",
        [401] = "Unauthorized",
        [403] = "Forbidden",
        [404] = "Not Found",
        [405] = "Method Not Allowed",
        [408] = "Request Timeout",
        [409] = "Conflict",
        [413] = "Content Too Large",
        [415] = "Unsupported Media Type",
        [426] = "Upgrade Required",
        [500] = "Internal Server Error",
        [503] = "Service Unavailable",
    };

    public static Task WriteAsync(Stream stream, int status, object body, Guid instance, double? retryAfterSeconds, CancellationToken cancellationToken)
        => WriteAsync(stream, status, body, instance, retryAfterSeconds, false, null, cancellationToken);

    public static Task WriteAsync(Stream stream, int status, object body, Guid instance, double? retryAfterSeconds, bool idempotentReplay, CancellationToken cancellationToken)
        => WriteAsync(stream, status, body, instance, retryAfterSeconds, idempotentReplay, null, cancellationToken);

    public static async Task WriteAsync(
        Stream stream,
        int status,
        object body,
        Guid instance,
        double? retryAfterSeconds,
        bool idempotentReplay,
        IReadOnlyList<KeyValuePair<string, string>>? extraHeaders,
        CancellationToken cancellationToken)
    {
        var payload = NoireRemoteJson.WriteBytes(body);
        var header = new StringBuilder(256);

        header.Append("HTTP/1.1 ").Append(status.ToString(CultureInfo.InvariantCulture)).Append(' ');
        header.Append(Reasons.TryGetValue(status, out var reason) ? reason : "Status").Append("\r\n");
        header.Append("Content-Type: ").Append(NoireRemoteHeaders.JsonContentTypeWithCharset).Append("\r\n");
        header.Append("Content-Length: ").Append(payload.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        header.Append(NoireRemoteHeaders.Protocol).Append(": ").Append(NoireRemotePaths.Protocol.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        header.Append(NoireRemoteHeaders.Instance).Append(": ").Append(instance.ToString("D")).Append("\r\n");
        header.Append("X-Content-Type-Options: nosniff\r\n");
        header.Append("Cache-Control: no-store\r\n");

        if (retryAfterSeconds is > 0)
            header.Append("Retry-After: ").Append(((int)Math.Ceiling(retryAfterSeconds.Value)).ToString(CultureInfo.InvariantCulture)).Append("\r\n");

        if (idempotentReplay)
            header.Append(NoireRemoteHeaders.IdempotentReplay).Append(": true\r\n");

        if (extraHeaders != null)
        {
            foreach (var pair in extraHeaders)
                header.Append(pair.Key).Append(": ").Append(pair.Value).Append("\r\n");
        }

        header.Append("Connection: close\r\n\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(header.ToString());

        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    // Close-delimited: no Content-Length and no chunked encoder. Each line reaches the reader as written.
    public static async Task BeginStreamAsync(Stream stream, string contentType, Guid instance, CancellationToken cancellationToken)
    {
        var header = new StringBuilder(256);

        header.Append("HTTP/1.1 200 OK\r\n");
        header.Append("Content-Type: ").Append(contentType).Append("\r\n");
        header.Append(NoireRemoteHeaders.Protocol).Append(": ").Append(NoireRemotePaths.Protocol.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        header.Append(NoireRemoteHeaders.Instance).Append(": ").Append(instance.ToString("D")).Append("\r\n");
        header.Append("X-Content-Type-Options: nosniff\r\n");
        header.Append("Cache-Control: no-store\r\n");
        header.Append("Connection: close\r\n\r\n");

        var bytes = Encoding.ASCII.GetBytes(header.ToString());

        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    // The console page and its assets.
    public static async Task WriteBytesAsync(
        Stream stream,
        int status,
        string contentType,
        byte[] payload,
        Guid instance,
        IReadOnlyList<KeyValuePair<string, string>>? extraHeaders,
        CancellationToken cancellationToken)
    {
        var header = new StringBuilder(256);

        header.Append("HTTP/1.1 ").Append(status.ToString(CultureInfo.InvariantCulture)).Append(' ');
        header.Append(Reasons.TryGetValue(status, out var reason) ? reason : "Status").Append("\r\n");
        header.Append("Content-Type: ").Append(contentType).Append("\r\n");
        header.Append("Content-Length: ").Append(payload.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        header.Append(NoireRemoteHeaders.Protocol).Append(": ").Append(NoireRemotePaths.Protocol.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        header.Append(NoireRemoteHeaders.Instance).Append(": ").Append(instance.ToString("D")).Append("\r\n");
        header.Append("X-Content-Type-Options: nosniff\r\n");
        header.Append("Cache-Control: no-store\r\n");

        if (extraHeaders != null)
        {
            foreach (var pair in extraHeaders)
                header.Append(pair.Key).Append(": ").Append(pair.Value).Append("\r\n");
        }

        header.Append("Connection: close\r\n\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(header.ToString());

        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static Task WriteFailureAsync(Stream stream, HttpFailure failure, Guid instance, string? requestId, CancellationToken cancellationToken)
    {
        var envelope = new NoireRemoteEnvelope
        {
            Ok = false,
            Instance = instance,
            Id = requestId,
            Error = new NoireRemoteError
            {
                Code = failure.Code,
                Message = failure.Message,
                Detail = failure.Detail,
                RetryAfterSeconds = failure.RetryAfterSeconds,
                Candidates = failure.Candidates,
            },
        };

        return WriteAsync(stream, failure.Status, envelope, instance, failure.RetryAfterSeconds, false, failure.ExtraHeaders, cancellationToken);
    }

    // The one answer that is not JSON and does not close. Everything after it is frames.
    public static async Task WriteUpgradeAsync(Stream stream, string accept, string? subProtocol, Guid instance, CancellationToken cancellationToken)
    {
        var header = new StringBuilder(192);

        header.Append("HTTP/1.1 101 Switching Protocols\r\n");
        header.Append("Upgrade: websocket\r\n");
        header.Append("Connection: Upgrade\r\n");
        header.Append("Sec-WebSocket-Accept: ").Append(accept).Append("\r\n");

        if (!string.IsNullOrEmpty(subProtocol))
            header.Append("Sec-WebSocket-Protocol: ").Append(subProtocol).Append("\r\n");

        header.Append(NoireRemoteHeaders.Protocol).Append(": ").Append(NoireRemotePaths.Protocol.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        header.Append(NoireRemoteHeaders.Instance).Append(": ").Append(instance.ToString("D")).Append("\r\n\r\n");

        var bytes = Encoding.ASCII.GetBytes(header.ToString());

        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
