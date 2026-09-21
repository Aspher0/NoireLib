using System;

namespace NoireLib.Websocket;

/// <summary>
/// The reconnection schedule a Socket.IO connection runs under. The protocol package owns the loop and these values
/// are mapped onto its own, because a second loop over it opens a second socket.<br/>
/// Reconfigure a copy with <c>with</c>: <c>NoireSocketIOReconnect.Default with { MaxAttempts = 5 }</c>.
/// </summary>
public sealed record NoireSocketIOReconnect
{
    /// <summary>Gets the default schedule, matching the protocol package's own defaults.</summary>
    public static NoireSocketIOReconnect Default { get; } = new();

    /// <summary>
    /// Gets the schedule that never reconnects. A dropped connection stays dropped.
    /// </summary>
    public static NoireSocketIOReconnect None { get; } = new() { Enabled = false };

    /// <summary>
    /// Gets whether a dropped connection is reopened at all.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets how many attempts are made before the connection is left <see cref="NoireSocketState.Faulted"/>.
    /// </summary>
    public int MaxAttempts { get; init; } = int.MaxValue;

    /// <summary>
    /// Gets the delay before the first retry.
    /// </summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the ceiling no delay passes.
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets how far a delay may be moved either side of its computed value, as a fraction of it.
    /// </summary>
    public double Jitter { get; init; } = 0.5;
}
