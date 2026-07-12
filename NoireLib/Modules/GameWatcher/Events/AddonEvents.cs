using Dalamud.Game.Addon.Lifecycle;

namespace NoireLib.GameWatcher;

/// <summary>
/// Fired for every lifecycle transition of a watched addon (setup, refresh, requested update, finalize, …).
/// Lifecycle events are exact (pushed by Dalamud).
/// </summary>
/// <param name="AddonName">The addon's internal name.</param>
/// <param name="Event">The lifecycle event that occurred.</param>
public sealed record AddonLifecycleEvent(string AddonName, AddonEvent Event);

/// <summary>
/// Fired when a watched addon becomes visible.
/// </summary>
/// <param name="AddonName">The addon's internal name.</param>
public sealed record AddonShownEvent(string AddonName);

/// <summary>
/// Fired when a watched addon is hidden or finalized.
/// </summary>
/// <param name="AddonName">The addon's internal name.</param>
public sealed record AddonHiddenEvent(string AddonName);
