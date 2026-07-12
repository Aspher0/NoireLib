namespace NoireLib.GameWatcher;

/// <summary>
/// Fired when an action effect is received from the server (an action resolving on its targets),
/// with fully parsed per-target effects.
/// </summary>
/// <param name="Entry">The captured action-effect entry.</param>
public sealed record ActionEffectEvent(ActionEffectEntry Entry);

/// <summary>
/// Fired for every ActorControl packet the client processes — the raw tier-5 tap on the hook source.<br/>
/// <b>Advanced and unstable</b>: categories and argument meanings are reverse-engineered territory and can
/// change with game patches. Prefer the modeled events (emotes, cast interrupts, death) when they exist.
/// </summary>
/// <param name="EntityId">The entity the control packet applies to.</param>
/// <param name="Category">The raw ActorControl category.</param>
/// <param name="Arg0">The first argument.</param>
/// <param name="Arg1">The second argument.</param>
/// <param name="Arg2">The third argument.</param>
/// <param name="Arg3">The fourth argument.</param>
/// <param name="Arg4">The fifth argument.</param>
/// <param name="Arg5">The sixth argument.</param>
/// <param name="TargetId">The target id carried by the packet.</param>
public sealed record RawActorControlEvent(
    uint EntityId,
    uint Category,
    uint Arg0,
    uint Arg1,
    uint Arg2,
    uint Arg3,
    uint Arg4,
    uint Arg5,
    ulong TargetId);
