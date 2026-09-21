using Newtonsoft.Json;
using NoireLib.Remote;
using System.Collections.Generic;
using System.IO;

namespace NoireLib.Websocket;

/// <summary>
/// One long-poll answer, exactly as it arrived.
/// </summary>
public sealed class NoireLongPollResponse
{
    internal NoireLongPollResponse(int status, IReadOnlyDictionary<string, string> headers, string body, string? cursor)
    {
        Status = status;
        Headers = headers;
        Body = body;
        Cursor = cursor;
    }

    /// <summary>
    /// Gets the status code the server answered with.
    /// </summary>
    public int Status { get; }

    /// <summary>
    /// Gets the response headers, keyed ignoring case. A header sent more than once is joined with commas.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>
    /// Gets the body as text.
    /// </summary>
    public string Body { get; }

    /// <summary>
    /// Gets the cursor this poll was issued with, or null for the first poll of a run.
    /// </summary>
    public string? Cursor { get; }

    /// <summary>
    /// Gets whether the server answered with nothing to report, meaning a 204 or a blank body. The loop waits
    /// <see cref="NoireLongPollOptions.EmptyPollDelay"/> before the next poll when it is true.
    /// </summary>
    public bool IsEmpty => Status == 204 || string.IsNullOrWhiteSpace(Body);

    /// <summary>
    /// Reads the body as JSON.
    /// </summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <returns>The deserialized value, or null when the body is an empty document.</returns>
    public T? Json<T>()
    {
        using var reader = new JsonTextReader(new StringReader(Body));
        return NoireRemoteJson.Serializer.Deserialize<T>(reader);
    }
}
