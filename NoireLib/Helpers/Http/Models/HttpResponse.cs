using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace NoireLib.Helpers;

/// <summary>
/// The outcome of one HTTP request. A request that never reached the server reports
/// <see cref="HttpStatusCode"/> zero and carries the reason in <see cref="Error"/>.
/// </summary>
public readonly record struct HttpResponse
{
    /// <summary>Whether the server answered with a success status.</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>The status the server answered with, or zero when the request never got one.</summary>
    public required HttpStatusCode StatusCode { get; init; }

    /// <summary>The response body, empty when it was not read or the request failed.</summary>
    public required byte[] Content { get; init; }

    /// <summary>The response and content headers, joined on commas when a header repeats.</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>Why the request failed, or null when it succeeded.</summary>
    public string? Error { get; init; }

    /// <summary>Whether the caller cancelled the request.</summary>
    public bool WasCancelled { get; init; }

    /// <summary>The body decoded as text.</summary>
    /// <param name="encoding">The encoding to decode with, or null for UTF-8.</param>
    /// <returns>The decoded body, empty when there is none.</returns>
    public string AsString(Encoding? encoding = null)
        => Content.Length == 0 ? string.Empty : (encoding ?? Encoding.UTF8).GetString(Content);

    /// <summary>The body deserialized from JSON.</summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <param name="settings">Serializer settings, or null for the defaults.</param>
    /// <returns>The deserialized value, or null when the body is empty or unreadable as <typeparamref name="T"/>.</returns>
    public T? AsJson<T>(JsonSerializerSettings? settings = null)
        => HttpHelper.DeserializeJson<T>(AsString(), null, settings);

    /// <summary>The header value under a name, ignoring case.</summary>
    /// <param name="name">The header name.</param>
    /// <returns>The value, or null when the response carries no such header.</returns>
    public string? Header(string name)
        => Headers.TryGetValue(name, out var value) ? value : null;
}
