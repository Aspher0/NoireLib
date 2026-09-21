using System;
using System.Threading.Tasks;

namespace NoireLib.Remote;

/// <summary>
/// What a host supplies that HTTP cannot work out for itself: who it is, where a call belongs and whether the host is
/// in a state to take one.
/// </summary>
public interface INoireRemoteHost
{
    /// <summary>Gets the names the instance record and the manifest carry.</summary>
    NoireRemoteIdentity Identity { get; }

    /// <summary>Gets the thread a member runs on when neither it, its endpoint nor the options name one.</summary>
    NoireRemoteThread DefaultThread { get; }

    /// <summary>Gets whether the host has a thread of its own. Read once per call.</summary>
    bool HasHostThread { get; }

    /// <summary>Runs work on the host's own thread and returns its result.</summary>
    /// <param name="work">The work. Only called when <see cref="HasHostThread"/> is true.</param>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <returns>The result the work produced.</returns>
    Task<TResult> RunOnHostThreadAsync<TResult>(Func<Task<TResult>> work);

    /// <summary>Checks whether the host is in the state a member asked for. Called on the host thread when there is one.</summary>
    /// <param name="requires">The state the member declared.</param>
    /// <param name="route">The route asking, for the message a refusal carries.</param>
    /// <returns>Ready, or the reason it is not with how long to wait.</returns>
    NoireRemoteReadinessResult CheckReadiness(NoireRemoteReadiness requires, string route);

    /// <summary>Asks for the label that tells this host from another. The callback may run later, on another thread.</summary>
    /// <param name="setLabel">Called with the label once it is known.</param>
    void RefreshLabel(Action<string> setLabel);

    /// <summary>Writes one line to the host's log.</summary>
    /// <param name="level">How severe the line is.</param>
    /// <param name="message">The line.</param>
    /// <param name="exception">The exception behind it, or null.</param>
    void Log(NoireRemoteLogLevel level, string message, Exception? exception);
}

/// <summary>
/// The names a host answers with, carried by the instance record and by the manifest.
/// </summary>
/// <param name="Name">The host's name. It is the plugin field of the record and of the manifest.</param>
/// <param name="Version">The host's version.</param>
/// <param name="LibraryVersion">The NoireLib version the host runs.</param>
public readonly record struct NoireRemoteIdentity(string Name, string Version, string LibraryVersion);

/// <summary>
/// How severe a line the listener writes is.
/// </summary>
public enum NoireRemoteLogLevel
{
    /// <summary>Detail a reader asks for.</summary>
    Debug,

    /// <summary>Something the listener did.</summary>
    Info,

    /// <summary>Something a reader has to know about.</summary>
    Warning,

    /// <summary>Something failed.</summary>
    Error,
}

/// <summary>
/// The answer to a readiness check: ready, or the reason it is not with how long to wait.
/// </summary>
public readonly struct NoireRemoteReadinessResult
{
    private NoireRemoteReadinessResult(bool isReady, string reason, double retryAfterSeconds)
    {
        IsReady = isReady;
        Reason = reason;
        RetryAfterSeconds = retryAfterSeconds;
    }

    /// <summary>
    /// Gets whether the host is in the state the member asked for.
    /// </summary>
    public bool IsReady { get; }

    /// <summary>
    /// Gets why the host is not ready, written for a caller reading a console. Empty when it is ready.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets how long to wait before retrying, in seconds. Zero when the host is ready.
    /// </summary>
    public double RetryAfterSeconds { get; }

    /// <summary>
    /// Gets the answer a host in the right state gives.
    /// </summary>
    public static NoireRemoteReadinessResult Ready { get; } = new(true, string.Empty, 0);

    /// <summary>
    /// Builds the answer a host that is not in the right state gives.
    /// </summary>
    /// <param name="reason">Why the call cannot run, as a full sentence.</param>
    /// <param name="retryAfterSeconds">How long the caller waits before retrying.</param>
    /// <returns>The refusal.</returns>
    public static NoireRemoteReadinessResult NotReady(string reason, double retryAfterSeconds = 2)
        => new(false, reason ?? string.Empty, retryAfterSeconds);
}
