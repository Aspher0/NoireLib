namespace NoireLib.Remote;

/// <summary>
/// The header names the protocol reads and writes.
/// </summary>
public static class NoireRemoteHeaders
{
    /// <summary>Carries the credential. <c>Bearer</c> on loopback, <c>Noire</c> for a signed non-loopback call.</summary>
    public const string Authorization = "Authorization";

    /// <summary>The loopback credential scheme.</summary>
    public const string BearerScheme = "Bearer";

    /// <summary>The signed non-loopback credential scheme.</summary>
    public const string SignedScheme = "Noire";

    /// <summary>The protocol version on every response.</summary>
    public const string Protocol = "X-Noire-Protocol";

    /// <summary>The instance id on every response, and the pin a request may send.</summary>
    public const string Instance = "X-Noire-Instance";

    /// <summary>The header spelling of the request correlation id.</summary>
    public const string RequestId = "X-Noire-Request-Id";

    /// <summary>The media type every request body and every response body carries.</summary>
    public const string JsonContentType = "application/json";

    /// <summary>The media type written on every response.</summary>
    public const string JsonContentTypeWithCharset = "application/json; charset=utf-8";

    /// <summary>The media type a held event stream is written as, one JSON document per line.</summary>
    public const string NdJsonContentType = "application/x-ndjson";

    /// <summary>What the held event stream answers with: server-sent events.</summary>
    public const string EventStreamContentType = "text/event-stream";


    /// <summary>The header marking an answer as the stored replay of an earlier idempotent call.</summary>
    public const string IdempotentReplay = "X-Noire-Idempotent-Replay";

    /// <summary>The console session credential scheme.</summary>
    public const string ConsoleScheme = "Noire-Console";

    /// <summary>
    /// The request headers that mean a browser sent the request. Any one of them is refused.
    /// </summary>
    public static readonly string[] BrowserHeaders =
    [
        "origin",
        "referer",
        "sec-fetch-site",
        "sec-fetch-mode",
        "sec-fetch-dest",
        "sec-fetch-user",
    ];
}
