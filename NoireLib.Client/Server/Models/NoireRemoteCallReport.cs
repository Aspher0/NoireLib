using System;

namespace NoireLib.Remote;

/// <summary>
/// What one finished call did, published to <see cref="NoireRemote.CallCompleted"/>.
/// </summary>
public sealed class NoireRemoteCallReport
{
    internal NoireRemoteCallReport(string endpoint, string member, string requestId, bool ok, string? errorCode, TimeSpan elapsed, bool isLoopback)
    {
        Endpoint = endpoint;
        Member = member;
        RequestId = requestId;
        Ok = ok;
        ErrorCode = errorCode;
        Elapsed = elapsed;
        IsLoopback = isLoopback;
    }

    /// <summary>
    /// Gets the endpoint called, or an empty string for a meta route.
    /// </summary>
    public string Endpoint { get; }

    /// <summary>
    /// Gets the member called, or the meta route name.
    /// </summary>
    public string Member { get; }

    /// <summary>
    /// Gets the correlation id the answer carried.
    /// </summary>
    public string RequestId { get; }

    /// <summary>
    /// Gets whether the call succeeded.
    /// </summary>
    public bool Ok { get; }

    /// <summary>
    /// Gets the error code, or null when the call succeeded.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets how long the call took, from the request being routed to the answer being written.
    /// </summary>
    public TimeSpan Elapsed { get; }

    /// <summary>
    /// Gets whether the caller connected over loopback.
    /// </summary>
    public bool IsLoopback { get; }
}
