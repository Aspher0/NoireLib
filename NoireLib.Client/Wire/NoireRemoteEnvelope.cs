using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace NoireLib.Remote;

/// <summary>
/// The body a caller posts to a member route.
/// </summary>
public sealed class NoireRemoteRequest
{
    /// <summary>
    /// Gets or sets the arguments, either an object keyed by parameter name or an array in declaration order.
    /// </summary>
    public JToken? Args { get; set; }

    /// <summary>
    /// Gets or sets the deadline for this call in milliseconds. The listener clamps it to its own ceiling.
    /// </summary>
    public int? TimeoutMs { get; set; }

    /// <summary>
    /// Gets or sets the call mode, <c>sync</c> or <c>job</c>. Null takes the member's declared mode.
    /// </summary>
    public string? Mode { get; set; }

    /// <summary>
    /// Gets or sets the correlation id echoed back in the response. The listener generates one when absent.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the protocol version the caller speaks. A version the listener does not serve is refused.
    /// </summary>
    public int? Protocol { get; set; }
}

/// <summary>
/// The body every NoireRemote response carries, whether the call succeeded or not.
/// </summary>
public sealed class NoireRemoteEnvelope
{
    /// <summary>
    /// Gets or sets whether the call succeeded. The HTTP status always agrees with it.
    /// </summary>
    public bool Ok { get; set; }

    /// <summary>
    /// Gets or sets the protocol version the listener speaks.
    /// </summary>
    public int Protocol { get; set; } = NoireRemotePaths.Protocol;

    /// <summary>
    /// Gets or sets the instance id of the listener that answered.
    /// </summary>
    public Guid Instance { get; set; }

    /// <summary>
    /// Gets or sets the correlation id of the request.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets how long the call took in milliseconds.
    /// </summary>
    public long? ElapsedMs { get; set; }

    /// <summary>
    /// Gets or sets the member's return value. Absent when the member returns nothing.
    /// </summary>
    public JToken? Result { get; set; }

    /// <summary>
    /// Gets or sets the failure, present when <see cref="Ok"/> is false.
    /// </summary>
    public NoireRemoteError? Error { get; set; }

    /// <summary>
    /// Gets or sets the job this call started or polled.
    /// </summary>
    public NoireRemoteJobStatus? Job { get; set; }

    /// <summary>
    /// Materializes <see cref="Result"/> into a known type.
    /// </summary>
    /// <typeparam name="TResult">The type to materialize.</typeparam>
    /// <returns>The converted result, or the type's default when the envelope carries none.</returns>
    public TResult? ResultAs<TResult>()
        => NoireRemoteJson.FromToken<TResult>(Result);
}

/// <summary>
/// The failure carried by an envelope whose call did not succeed.
/// </summary>
public sealed class NoireRemoteError
{
    /// <summary>
    /// Gets or sets the error code, one of the values on <see cref="NoireRemoteErrorCodes"/>.
    /// </summary>
    public string Code { get; set; } = NoireRemoteErrorCodes.BadRequest;

    /// <summary>
    /// Gets or sets the message, written for a person reading a console.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the offending route, parameter or exception type name.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>
    /// Gets or sets the near names offered after an unknown endpoint or member.
    /// </summary>
    public IReadOnlyList<string>? Candidates { get; set; }

    /// <summary>
    /// Gets or sets how long to wait before retrying, in seconds.
    /// </summary>
    public double? RetryAfterSeconds { get; set; }

    /// <summary>
    /// Gets or sets the remote stack trace, present only when the listener was told to send traces.
    /// </summary>
    public string? StackTrace { get; set; }
}

/// <summary>
/// The state of a job, carried by the response that started it and by every poll.
/// </summary>
public sealed class NoireRemoteJobStatus
{
    /// <summary>
    /// Gets or sets the job id.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the job state.
    /// </summary>
    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public NoireRemoteJobState State { get; set; }

    /// <summary>
    /// Gets or sets how far along the member reported it is, between zero and one.
    /// </summary>
    public double? Progress { get; set; }

    /// <summary>
    /// Gets or sets how long to wait before polling again, in milliseconds.
    /// </summary>
    public int? PollAfterMs { get; set; }

    /// <summary>
    /// Gets or sets the route the job runs.
    /// </summary>
    public string? Route { get; set; }
}

/// <summary>
/// One event handed back by the event long poll.
/// </summary>
public sealed class NoireRemoteEvent
{
    /// <summary>
    /// Gets or sets the sequence number of this event in the instance's buffer.
    /// </summary>
    public long Seq { get; set; }

    /// <summary>
    /// Gets or sets the channel the event was published on.
    /// </summary>
    public string Channel { get; set; } = NoireRemoteChannels.Events;

    /// <summary>
    /// Gets or sets the topic, matched against a poll's topic list by prefix.
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the event was published, in UTC.
    /// </summary>
    public DateTimeOffset AtUtc { get; set; }

    /// <summary>
    /// Gets or sets the payload.
    /// </summary>
    public JToken? Data { get; set; }

    /// <summary>
    /// Materializes <see cref="Data"/> into a known type.
    /// </summary>
    /// <typeparam name="TData">The type to materialize.</typeparam>
    /// <returns>The converted payload, or the type's default when the event carries none.</returns>
    public TData? DataAs<TData>()
        => NoireRemoteJson.FromToken<TData>(Data);
}

/// <summary>
/// What one progress report carries. It is the payload of an event on the progress channel.
/// </summary>
public sealed class NoireRemoteProgressReport
{
    /// <summary>
    /// Gets or sets the route the member was called on.
    /// </summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the request id of the call reporting.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the job id, or null when the call is not a job.
    /// </summary>
    public string? Job { get; set; }

    /// <summary>
    /// Gets or sets how far along the member is, between zero and one. It is set only when the reporter carries a
    /// fractional number.
    /// </summary>
    public double? Value { get; set; }

    /// <summary>
    /// Gets or sets what the reporter carried, for a reporter of any other type.
    /// </summary>
    public JToken? Payload { get; set; }
}

/// <summary>
/// The body a caller posts to the event route.
/// </summary>
public sealed class NoireRemoteEventRequest
{
    /// <summary>
    /// Gets or sets the topics to watch. An empty list watches every topic.
    /// </summary>
    public IReadOnlyList<string>? Topics { get; set; }

    /// <summary>
    /// Gets or sets the cursor handed back by the previous poll. Zero starts at the oldest buffered event.
    /// </summary>
    public long Since { get; set; }

    /// <summary>
    /// Gets or sets how long the listener may hold the request waiting for an event, in milliseconds.
    /// </summary>
    public int? WaitMs { get; set; }

    /// <summary>
    /// Gets or sets the channels to watch. An empty list watches the events channel alone.
    /// </summary>
    public IReadOnlyList<string>? Channels { get; set; }

    /// <summary>
    /// Gets or sets the cursor to resume each channel from, keyed by channel name. A channel absent from it starts
    /// at the oldest buffered event.
    /// </summary>
    public IReadOnlyDictionary<string, long>? Cursors { get; set; }

    /// <summary>
    /// Gets or sets the clauses an event's payload has to satisfy, joined by and.
    /// </summary>
    public IReadOnlyList<NoireRemoteEventFilter>? Filters { get; set; }

    /// <summary>
    /// Gets or sets whether only the newest event per topic is returned.
    /// </summary>
    public bool Collapse { get; set; }
}

/// <summary>
/// One clause of a poll's filter, read against the event's payload.
/// </summary>
public sealed class NoireRemoteEventFilter
{
    /// <summary>
    /// Gets or sets the dotted path into the payload, as <c>position.x</c> or <c>items[0].name</c>.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the comparison, one of <c>eq ne gt ge lt le contains startsWith exists</c>.
    /// </summary>
    public string Op { get; set; } = "eq";

    /// <summary>
    /// Gets or sets the value compared against. For <c>exists</c> it reads as a boolean and defaults to true.
    /// </summary>
    public JToken? Value { get; set; }
}

/// <summary>
/// The answer to an event long poll.
/// </summary>
public sealed class NoireRemoteEventBatch
{
    /// <summary>
    /// Gets or sets whether the poll succeeded.
    /// </summary>
    public bool Ok { get; set; } = true;

    /// <summary>
    /// Gets or sets the protocol version the listener speaks.
    /// </summary>
    public int Protocol { get; set; } = NoireRemotePaths.Protocol;

    /// <summary>
    /// Gets or sets the instance id of the listener that answered.
    /// </summary>
    public Guid Instance { get; set; }

    /// <summary>
    /// Gets or sets the cursor to send back as the next poll's <c>since</c>.
    /// </summary>
    public long Cursor { get; set; }

    /// <summary>
    /// Gets or sets the cursor to resume each channel from, keyed by channel name.
    /// </summary>
    public IReadOnlyDictionary<string, long>? Cursors { get; set; }

    /// <summary>
    /// Gets or sets whether events were dropped from the buffer before this poll read it.
    /// </summary>
    public bool Missed { get; set; }

    /// <summary>
    /// Gets or sets which channels dropped events, or null when none did.
    /// </summary>
    public IReadOnlyList<string>? MissedChannels { get; set; }

    /// <summary>
    /// Gets or sets the events, oldest first. The list is empty when the wait elapsed with nothing published.
    /// </summary>
    public IReadOnlyList<NoireRemoteEvent> Events { get; set; } = [];


    /// <summary>
    /// Gets or sets the failure, present when <see cref="Ok"/> is false.
    /// </summary>
    public NoireRemoteError? Error { get; set; }
}

/// <summary>
/// The answer to the liveness route.
/// </summary>
public sealed class NoireRemotePing
{
    /// <summary>
    /// Gets or sets whether the listener is serving.
    /// </summary>
    public bool Ok { get; set; } = true;

    /// <summary>
    /// Gets or sets the protocol version the listener speaks.
    /// </summary>
    public int Protocol { get; set; } = NoireRemotePaths.Protocol;

    /// <summary>
    /// Gets or sets the instance id of the listener that answered.
    /// </summary>
    public Guid Instance { get; set; }

    /// <summary>
    /// Gets or sets how long the listener has been up, in milliseconds.
    /// </summary>
    public long UptimeMs { get; set; }

    /// <summary>
    /// Gets or sets the endpoint names published.
    /// </summary>
    public IReadOnlyList<string> Endpoints { get; set; } = [];

    /// <summary>
    /// Gets or sets the machine the listener runs on.
    /// </summary>
    public string Machine { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what the instance tags itself with. A caller on another machine aims the same way as one on
    /// this machine.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets or sets the label telling this host from another.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of whatever published the surface.
    /// </summary>
    public string Plugin { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version of whatever published the surface.
    /// </summary>
    public string PluginVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether another listener's console may drive this one in a fleet.
    /// </summary>
    public bool AllowFleetControl { get; set; }
}
