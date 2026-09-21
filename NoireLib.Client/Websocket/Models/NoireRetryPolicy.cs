using System;

namespace NoireLib.Websocket;

/// <summary>
/// The reconnection schedule, shared by the raw WebSocket, SSE and long-polling clients. Reconfigure a copy with
/// <c>with</c>: <c>NoireRetryPolicy.Default with { MaxDelay = TimeSpan.FromMinutes(2) }</c>.
/// </summary>
public sealed record NoireRetryPolicy
{
    /// <summary>
    /// Gets the schedule used when nothing else is set: one second, doubling, capped at thirty, with a quarter of
    /// jitter and no attempt limit.
    /// </summary>
    public static NoireRetryPolicy Default { get; } = new();

    /// <summary>
    /// Gets the schedule that never reconnects.
    /// </summary>
    public static NoireRetryPolicy None { get; } = new() { MaxAttempts = 0 };

    /// <summary>
    /// Gets the delay before the first retry.
    /// </summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets what each delay is multiplied by for the next attempt.
    /// </summary>
    public double Multiplier { get; init; } = 2.0;

    /// <summary>
    /// Gets the ceiling no delay passes.
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets how far a delay may be moved either side of its computed value, as a fraction of it. Zero retries on
    /// exactly the schedule.
    /// </summary>
    public double Jitter { get; init; } = 0.25;

    /// <summary>
    /// Gets how many attempts are made before the connection is left <see cref="NoireSocketState.Faulted"/>. Null,
    /// the default, never stops trying.
    /// </summary>
    public int? MaxAttempts { get; init; } = null;

    /// <summary>
    /// Gets whether a close the peer asked for cleanly is reconnected. False, the default, treats it as the end.
    /// </summary>
    public bool ReconnectOnCleanClose { get; init; } = false;

    /// <summary>
    /// Says whether another attempt is allowed.
    /// </summary>
    /// <param name="attempt">How many attempts have already been made, starting at zero.</param>
    /// <returns>True when the policy permits one more.</returns>
    public bool ShouldRetry(int attempt)
        => MaxAttempts == null || attempt < MaxAttempts.Value;

    /// <summary>
    /// Computes the delay before an attempt, with the jitter band centred.
    /// </summary>
    /// <param name="attempt">How many attempts have already been made, starting at zero.</param>
    /// <returns>The delay to wait.</returns>
    public TimeSpan DelayFor(int attempt)
        => DelayFor(attempt, 0.5);

    /// <summary>
    /// Computes the delay before an attempt from a given point in the jitter band, for testing without a random source.
    /// </summary>
    /// <param name="attempt">How many attempts have already been made, starting at zero.</param>
    /// <param name="jitterSample">A value from zero to one placing the result in the band. Half is the centre.</param>
    /// <returns>The delay to wait.</returns>
    public TimeSpan DelayFor(int attempt, double jitterSample)
    {
        if (attempt < 0)
            attempt = 0;

        var baseDelay = InitialDelay.TotalMilliseconds;

        for (var i = 0; i < attempt; i++)
        {
            baseDelay *= Multiplier;

            if (baseDelay >= MaxDelay.TotalMilliseconds)
                break;
        }

        baseDelay = Math.Min(baseDelay, MaxDelay.TotalMilliseconds);

        if (Jitter > 0)
        {
            var sample = Math.Clamp(jitterSample, 0.0, 1.0);
            baseDelay *= 1.0 - Jitter + (2.0 * Jitter * sample);
        }

        return TimeSpan.FromMilliseconds(Math.Max(0, baseDelay));
    }
}
