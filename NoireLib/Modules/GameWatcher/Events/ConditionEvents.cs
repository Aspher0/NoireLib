using Dalamud.Game.ClientState.Conditions;

namespace NoireLib.GameWatcher;

/// <summary>
/// Fired when any raw game condition flag changes.
/// </summary>
/// <param name="Flag">The condition flag that changed.</param>
/// <param name="Active">Whether the flag is now set.</param>
public sealed record ConditionChangedEvent(ConditionFlag Flag, bool Active);

/// <summary>Fired when the local player enters combat (condition-derived).</summary>
public sealed record CombatEnteredEvent;

/// <summary>Fired when the local player leaves combat (condition-derived).</summary>
public sealed record CombatLeftEvent;

/// <summary>Fired when the local player mounts up (condition-derived).</summary>
public sealed record MountedEvent;

/// <summary>Fired when the local player dismounts (condition-derived).</summary>
public sealed record DismountedEvent;

/// <summary>Fired when the local player starts flying (condition-derived).</summary>
public sealed record FlightStartedEvent;

/// <summary>Fired when the local player stops flying (condition-derived).</summary>
public sealed record FlightEndedEvent;

/// <summary>Fired when the local player starts swimming (condition-derived).</summary>
public sealed record SwimmingStartedEvent;

/// <summary>Fired when the local player stops swimming (condition-derived).</summary>
public sealed record SwimmingEndedEvent;

/// <summary>Fired when the local player starts diving (condition-derived).</summary>
public sealed record DivingStartedEvent;

/// <summary>Fired when the local player stops diving (condition-derived).</summary>
public sealed record DivingEndedEvent;

/// <summary>Fired when the local player starts crafting (condition-derived).</summary>
public sealed record CraftingStartedEvent;

/// <summary>Fired when the local player stops crafting (condition-derived).</summary>
public sealed record CraftingEndedEvent;

/// <summary>Fired when the local player starts gathering (condition-derived).</summary>
public sealed record GatheringStartedEvent;

/// <summary>Fired when the local player stops gathering (condition-derived).</summary>
public sealed record GatheringEndedEvent;

/// <summary>Fired when the local player starts fishing (condition-derived).</summary>
public sealed record FishingStartedEvent;

/// <summary>Fired when the local player stops fishing (condition-derived).</summary>
public sealed record FishingEndedEvent;

/// <summary>Fired when the local player starts a bard performance (condition-derived).</summary>
public sealed record PerformanceStartedEvent;

/// <summary>Fired when the local player ends a bard performance (condition-derived).</summary>
public sealed record PerformanceEndedEvent;

/// <summary>Fired when the local player becomes occupied (talking to an NPC, using an event object, …).</summary>
public sealed record OccupiedStartedEvent;

/// <summary>Fired when the local player stops being occupied.</summary>
public sealed record OccupiedEndedEvent;

/// <summary>Fired when a cutscene starts (condition-derived).</summary>
public sealed record CutsceneStartedEvent;

/// <summary>Fired when a cutscene ends (condition-derived).</summary>
public sealed record CutsceneEndedEvent;

/// <summary>Fired when a zone loading transition starts (between-areas, condition-derived).</summary>
public sealed record LoadingStartedEvent;

/// <summary>Fired when a zone loading transition ends (condition-derived).</summary>
public sealed record LoadingEndedEvent;

/// <summary>Fired when the local player becomes bound by a duty (condition-derived).</summary>
public sealed record DutyEnteredEvent;

/// <summary>Fired when the local player stops being bound by a duty (condition-derived).</summary>
public sealed record DutyLeftEvent;
