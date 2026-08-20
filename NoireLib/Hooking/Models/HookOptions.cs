using Dalamud.Plugin.Services;
using System;

namespace NoireLib.Hooking;

using HookBackend = IGameInteropProvider.HookBackend;

/// <summary>
/// Configures a <see cref="NoireHook{TDelegate}"/>, every property carrying a usable default.
/// </summary>
public sealed class HookOptions
{
    /// <summary>
    /// Gets or sets the friendly name used in logs and diagnostics, defaulting to the delegate type name.
    /// </summary>
    public string? Name { get; set; } = null;

    /// <summary>
    /// Gets or sets the group this hook belongs to, for bulk enable and disable.
    /// </summary>
    public string? Group { get; set; } = null;

    /// <summary>
    /// Gets or sets a value indicating whether the hook is enabled as soon as it installs.
    /// </summary>
    public bool AutoEnable { get; set; } = false;

    /// <summary>
    /// Gets or sets what happens when the delegate does not match the function at the resolved address.
    /// </summary>
    public HookVerificationPolicy Verification { get; set; } = HookVerificationPolicy.Throw;

    /// <summary>
    /// Gets or sets a value indicating whether verification requires exact types rather than only a matching calling-convention shape, which when off accepts a pointer written as <c>nint</c>.
    /// </summary>
    public bool StrictVerification { get; set; } = false;

    /// <summary>
    /// Gets or sets what happens when the detour throws.
    /// </summary>
    public HookGuardMode Guard { get; set; } = HookGuardMode.CallOriginal;

    /// <summary>
    /// Gets or sets the number of consecutive faults after which the hook disables itself, or zero to never disable.
    /// </summary>
    public int FaultLimit { get; set; } = 0;

    /// <summary>
    /// Gets or sets the shortest interval between two fault log entries for the same hook.
    /// </summary>
    public TimeSpan FaultLogInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets a value indicating whether the hook counts calls and measures time spent in the detour.
    /// </summary>
    public bool CollectStats { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether creation and disposal are logged at debug level.
    /// </summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// Gets or sets the Dalamud hook backend.
    /// </summary>
    public HookBackend Backend { get; set; } = HookBackend.Automatic;

    /// <summary>
    /// Gets or sets how long a deferred target keeps retrying before the hook is reported as failed.
    /// </summary>
    public TimeSpan ResolveTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a value indicating whether the hook disposes itself when NoireLib shuts down.
    /// </summary>
    public bool AutoDispose { get; set; } = true;

    /// <summary>
    /// Creates an independent copy of these options.
    /// </summary>
    /// <returns>The copy.</returns>
    public HookOptions Clone() => (HookOptions)MemberwiseClone();
}
