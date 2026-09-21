using System;

namespace NoireLib.Remote;

/// <summary>
/// The per-member settings the attributes carry, for the delegate overload that has no attribute to read.
/// </summary>
public sealed class NoireRemoteMemberOptions
{
    /// <summary>
    /// Gets or sets the thread the member runs on.
    /// </summary>
    public NoireRemoteThread Thread { get; set; } = NoireRemoteThread.Inherit;

    /// <summary>
    /// Gets or sets the readiness gate the member passes.
    /// </summary>
    public NoireRemoteReadiness Requires { get; set; } = NoireRemoteReadiness.Inherit;

    /// <summary>
    /// Gets or sets whether a call answers with the result or with a job to poll.
    /// </summary>
    public NoireRemoteCallMode Mode { get; set; } = NoireRemoteCallMode.Inherit;

    /// <summary>
    /// Gets or sets whether the member is reachable from another machine.
    /// </summary>
    public NoireRemoteAccess Access { get; set; } = NoireRemoteAccess.Inherit;

    /// <summary>
    /// Gets or sets the deadline a call gets when it asks for none. Zero takes the listener default.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Creates a shallow copy of these options.
    /// </summary>
    /// <returns>The copied options.</returns>
    public NoireRemoteMemberOptions Clone()
        => (NoireRemoteMemberOptions)MemberwiseClone();
}
