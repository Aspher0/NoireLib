using NoireLib.Remote;
using NoireLib.Remote.Internal;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace NoireLib.Websocket.Internal;

internal readonly struct WebsocketHandshakeResult
{
    private WebsocketHandshakeResult(HttpFailure? failure, string accept, string? subProtocol)
    {
        Failure = failure;
        Accept = accept;
        SubProtocol = subProtocol;
    }

    public HttpFailure? Failure { get; }

    public string Accept { get; }

    public string? SubProtocol { get; }

    public static WebsocketHandshakeResult Accepted(string accept, string? subProtocol)
        => new(null, accept, subProtocol);

    public static WebsocketHandshakeResult Refused(HttpFailure failure)
        => new(failure, string.Empty, null);
}

// RFC 6455 section 4.2, server side. Pure over a parsed request.
internal static class WebsocketHandshake
{
    // The RFC's own constant. A cache replaying an ordinary response cannot produce a valid accept value.
    private const string Magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private const string Version = "13";

    private const int KeyCharacters = 24;

    private const int KeyBytes = 16;

    private static readonly KeyValuePair<string, string>[] VersionHeader = [new("Sec-WebSocket-Version", Version)];

    public static WebsocketHandshakeResult Negotiate(HttpRequestData request, IReadOnlyList<string>? declaredSubProtocols, bool requireSubProtocol)
    {
        if (!request.IsWebsocketUpgrade)
        {
            return WebsocketHandshakeResult.Refused(new HttpFailure(400, NoireRemoteErrorCodes.BadRequest,
                "This route answers a WebSocket upgrade only."));
        }

        if (!ListsToken(request.Header("sec-websocket-version"), Version))
        {
            return WebsocketHandshakeResult.Refused(new HttpFailure(426, NoireRemoteErrorCodes.ProtocolMismatch,
                "This listener speaks WebSocket version " + Version + " only.")
            {
                ExtraHeaders = VersionHeader,
            });
        }

        var key = request.Header("sec-websocket-key")?.Trim();

        if (string.IsNullOrEmpty(key) || key!.Length != KeyCharacters || !DecodesToKeyBytes(key))
        {
            return WebsocketHandshakeResult.Refused(new HttpFailure(400, NoireRemoteErrorCodes.BadRequest,
                "The handshake key is not sixteen base64-encoded bytes."));
        }

        var selected = SelectSubProtocol(request.Header("sec-websocket-protocol"), declaredSubProtocols);

        if (selected == null && requireSubProtocol && declaredSubProtocols is { Count: > 0 })
        {
            return WebsocketHandshakeResult.Refused(new HttpFailure(400, NoireRemoteErrorCodes.BadRequest,
                "This socket speaks none of the sub-protocols the handshake offered.",
                string.Join(", ", declaredSubProtocols)));
        }

        // Omitting Sec-WebSocket-Extensions declines every offer. A reserved bit is then a protocol error.
        return WebsocketHandshakeResult.Accepted(ComputeAccept(key), selected);
    }

    public static string ComputeAccept(string key)
        => Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + Magic)));

    // The first declared entry the peer also offered, spelled like the endpoint.
    private static string? SelectSubProtocol(string? offered, IReadOnlyList<string>? declared)
    {
        if (declared == null || declared.Count == 0 || string.IsNullOrWhiteSpace(offered))
            return null;

        var tokens = offered!.Split(',');

        foreach (var candidate in declared)
        {
            foreach (var token in tokens)
            {
                if (string.Equals(token.Trim(), candidate, StringComparison.Ordinal))
                    return candidate;
            }
        }

        return null;
    }

    private static bool DecodesToKeyBytes(string key)
    {
        Span<byte> decoded = stackalloc byte[KeyCharacters];

        return Convert.TryFromBase64String(key, decoded, out var written) && written == KeyBytes;
    }

    private static bool ListsToken(string? value, string token)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (var part in value!.Split(','))
        {
            if (string.Equals(part.Trim(), token, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
