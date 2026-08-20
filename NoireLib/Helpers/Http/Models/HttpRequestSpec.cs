using System;
using System.Collections.Generic;
using System.Net.Http;

namespace NoireLib.Helpers;

/// <summary>
/// Everything one HTTP request can be told to do. Only <see cref="Url"/> is required; every other member has a
/// working default, and <c>with { }</c> changes one of them without restating the rest.
/// </summary>
public sealed record HttpRequestSpec
{
    /// <summary>The absolute URL to request.</summary>
    public required string Url { get; init; }

    /// <summary>The HTTP verb, defaulting to GET.</summary>
    public HttpMethod Method { get; init; } = HttpMethod.Get;

    /// <summary>Headers added to this request, or null for none.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>The request body, or null for none. Consumed by the send, so a retry needs a fresh instance.</summary>
    public HttpContent? Content { get; init; }

    /// <summary>How long the request may take, or null for <see cref="HttpHelper.DefaultTimeout"/>.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Whether the response body is read into <see cref="HttpResponse.Content"/>.</summary>
    public bool ReadContent { get; init; } = true;

    /// <summary>Whether a non-success status still reads the body, so an error payload can be inspected.</summary>
    public bool ReadContentOnFailure { get; init; } = true;
}
