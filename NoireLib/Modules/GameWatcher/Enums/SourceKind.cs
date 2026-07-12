namespace NoireLib.GameWatcher;

/// <summary>
/// Identifies the internal sources of the <see cref="NoireGameWatcher"/> module.<br/>
/// User code never interacts with sources directly — this enum only appears in
/// <see cref="GameWatcherOptions.Sources"/> and <see cref="GameWatcherOptions.PollCadences"/> overrides,
/// and in the diagnostics window.
/// </summary>
public enum SourceKind
{
    /// <summary>Login/logout, territory, map, instance, class/job, level, PvP state, content-finder pops and housing-interior transitions.</summary>
    Session,

    /// <summary>Raw <see cref="Dalamud.Game.ClientState.Conditions.ConditionFlag"/> changes and the derived enter/leave pairs (combat, mounted, crafting, …).</summary>
    Condition,

    /// <summary>Interest-masked polling over every character in the object table (vitals, casts, modes, death, targets, job/level, …).</summary>
    Characters,

    /// <summary>Generic object-table diffing for every <see cref="Dalamud.Game.ClientState.Objects.Enums.ObjectKind"/>, plus distance and region watchers.</summary>
    Objects,

    /// <summary>Party and alliance member diffing, leader changes, role composition and member territory changes.</summary>
    Party,

    /// <summary>Friend-list snapshots (online state, location) through the game's social data — remote presence beyond the object table.</summary>
    Friends,

    /// <summary>Target, focus target, soft target and mouse-over target changes.</summary>
    Targets,

    /// <summary>Duty started/wiped/recommenced/completed and duty-queue tracking.</summary>
    Duty,

    /// <summary>Chat messages with SeString payloads preserved and senders resolved.</summary>
    Chat,

    /// <summary>Parsed action effects (damage, healing, crits, …) received from the server, via hook.</summary>
    ActionEffect,

    /// <summary>The ActorControl packet family, via hook: one-shot emotes, authoritative cast interrupts and raw category taps.</summary>
    ActorControl,

    /// <summary>Local action cooldowns/charges/GCD (exact) and other characters' cooldown estimates (inferred).</summary>
    Cooldowns,

    /// <summary>Status effect gained/lost/stack changes for any scoped character.</summary>
    Statuses,

    /// <summary>Addon lifecycle events, shown/hidden transitions and node watchers.</summary>
    Addons,

    /// <summary>Granular inventory item events and item-count/currency conveniences.</summary>
    Inventory,

    /// <summary>Fate spawn/expiry/progress/state changes (slow cadence).</summary>
    Fate,

    /// <summary>Zone weather changes (slow cadence).</summary>
    Weather,

    /// <summary>Eorzea clock: hour changes and day/night transitions.</summary>
    EorzeaTime,

    /// <summary>Normal, quest and error toasts.</summary>
    Toast,
}
