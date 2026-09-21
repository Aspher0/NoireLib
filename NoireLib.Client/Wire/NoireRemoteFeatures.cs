namespace NoireLib.Remote;

/// <summary>
/// The feature names a manifest lists. A caller negotiates by asking for one of these by name. A feature that
/// is turned off is absent from the list.
/// </summary>
public static class NoireRemoteFeatures
{
    /// <summary>Events are buffered on named channels.</summary>
    public const string Channels = "channels";

    /// <summary>The listener holds a connection open and writes newline-delimited events onto it.</summary>
    public const string Stream = "stream";


    /// <summary>Files cross in chunks outside the call body.</summary>
    public const string Files = "files";


    /// <summary>The listener serves an OpenAPI document built from its manifest.</summary>
    public const string OpenApi = "openapi";


    /// <summary>A member taking a progress reporter publishes onto the progress channel.</summary>
    public const string Progress = "progress";

    /// <summary>A published type's events reach any caller watching them.</summary>
    public const string Events = "events";

    /// <summary>The listener answers a signed discovery probe on the local network.</summary>
    public const string Fleet = "fleet";

    /// <summary>The listener serves a browser console on a socket of its own.</summary>
    public const string Console = "console";

    /// <summary>The listener publishes lines of its host's log on the log channel.</summary>
    public const string Log = "log";

    /// <summary>Gauges are sampled onto the metrics channel.</summary>
    public const string Metrics = "metrics";
}

/// <summary>
/// The channels a listener buffers events on. A plugin declares its own beside these.
/// </summary>
public static class NoireRemoteChannels
{
    /// <summary>What the host publishes itself, and what a declared event lands on.</summary>
    public const string Events = "events";

    /// <summary>Progress reports from a member that takes a reporter.</summary>
    public const string Progress = "progress";

    /// <summary>The host's own log.</summary>
    public const string Log = "log";

    /// <summary>One sample of every registered gauge per interval.</summary>
    public const string Metrics = "metrics";

    /// <summary>What the listener is serving as it serves it: calls answered, sockets opened and closed, frames.</summary>
    public const string Traffic = "traffic";
}

/// <summary>
/// The topics <see cref="NoireRemoteChannels.Traffic"/> carries.
/// </summary>
public static class NoireRemoteTrafficTopics
{
    /// <summary>One finished call, over either wire.</summary>
    public const string Call = "traffic.call";

    /// <summary>A connection opened or closed on a published socket.</summary>
    public const string Socket = "traffic.socket";

    /// <summary>One frame in or out on a published socket.</summary>
    public const string Frame = "traffic.frame";
}
