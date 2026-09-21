using Newtonsoft.Json;
using System;

namespace NoireLib.Remote;

/// <summary>
/// One finished call, as the traffic channel carries it.
/// </summary>
public sealed class NoireRemoteTrafficCall
{
    /// <summary>
    /// Gets or sets which wire served it: <c>http</c> or <c>ws</c>.
    /// </summary>
    [JsonProperty("wire")]
    public string Wire { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the endpoint called, or an empty string for a meta route.
    /// </summary>
    [JsonProperty("endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the member called, or the meta route name.
    /// </summary>
    [JsonProperty("member")]
    public string Member { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the correlation id the answer carried.
    /// </summary>
    [JsonProperty("requestId")]
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the call succeeded.
    /// </summary>
    [JsonProperty("ok")]
    public bool Ok { get; set; }

    /// <summary>
    /// Gets or sets why it failed, or null when it did not.
    /// </summary>
    [JsonProperty("errorCode", NullValueHandling = NullValueHandling.Ignore)]
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Gets or sets how long it took.
    /// </summary>
    [JsonProperty("elapsedMs")]
    public double ElapsedMs { get; set; }

    /// <summary>
    /// Gets or sets whether the caller connected over loopback.
    /// </summary>
    [JsonProperty("isLoopback")]
    public bool IsLoopback { get; set; }
}

/// <summary>
/// A connection opened or closed on a published socket.
/// </summary>
public sealed class NoireRemoteTrafficSocket
{
    /// <summary>
    /// Gets or sets the socket's name.
    /// </summary>
    [JsonProperty("socket")]
    public string Socket { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what happened: <c>open</c> or <c>closed</c>.
    /// </summary>
    [JsonProperty("state")]
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the connection this is about.
    /// </summary>
    [JsonProperty("connection")]
    public Guid Connection { get; set; }

    /// <summary>
    /// Gets or sets the address the connection came from.
    /// </summary>
    [JsonProperty("address")]
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the close code, on a close.
    /// </summary>
    [JsonProperty("code", NullValueHandling = NullValueHandling.Ignore)]
    public int? Code { get; set; }

    /// <summary>
    /// Gets or sets the close reason, when there was one.
    /// </summary>
    [JsonProperty("reason", NullValueHandling = NullValueHandling.Ignore)]
    public string? Reason { get; set; }
}

/// <summary>
/// One frame in or out of a published socket. Only published while
/// <see cref="NoireRemoteOptions.PublishTrafficFrames"/> is set.
/// </summary>
public sealed class NoireRemoteTrafficFrame
{
    /// <summary>
    /// Gets or sets the socket's name.
    /// </summary>
    [JsonProperty("socket")]
    public string Socket { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets which way it went: <c>in</c> or <c>out</c>.
    /// </summary>
    [JsonProperty("direction")]
    public string Direction { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the connection it crossed.
    /// </summary>
    [JsonProperty("connection")]
    public Guid Connection { get; set; }

    /// <summary>
    /// Gets or sets how many bytes it carried.
    /// </summary>
    [JsonProperty("bytes")]
    public int Bytes { get; set; }

    /// <summary>
    /// Gets or sets as much of the frame as <see cref="NoireRemoteOptions.MaxTrafficPreview"/> allows, or null for
    /// a binary frame.
    /// </summary>
    [JsonProperty("preview", NullValueHandling = NullValueHandling.Ignore)]
    public string? Preview { get; set; }
}
