namespace NoireLib.GameWatcher;

/// <summary>
/// Implemented by every event whose subject is a character, so scoped subscriptions can filter uniformly.
/// </summary>
public interface ICharacterScopedEvent
{
    /// <summary>The subject the event is about (the most recent snapshot available).</summary>
    CharacterSnapshot Subject { get; }
}

/// <summary>
/// Fired when a character appears in the object table.<br/>
/// The object table is the client's entire view of the area, so with a scope this <i>is</i> the presence
/// event: someone entering the place you are in.
/// </summary>
/// <param name="Current">The subject's first snapshot.</param>
/// <param name="DuringZoneChange">True when the spawn happened while the client was loading between areas — the whole table respawns on zone transitions.</param>
public sealed record CharacterSpawnedEvent(CharacterSnapshot Current, bool DuringZoneChange) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character disappears from the object table.
/// </summary>
/// <param name="Last">The subject's last known snapshot.</param>
/// <param name="DuringZoneChange">True when the despawn happened while the client was loading between areas ("we left", not "they left").</param>
public sealed record CharacterDespawnedEvent(CharacterSnapshot Last, bool DuringZoneChange) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Last;
}

/// <summary>
/// Fired when a character's HP changes.
/// </summary>
/// <param name="Previous">The snapshot before the change.</param>
/// <param name="Current">The snapshot after the change.</param>
public sealed record CharacterHpChangedEvent(CharacterSnapshot Previous, CharacterSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character's MP changes (or GP/CP for the local player).
/// </summary>
/// <param name="Previous">The snapshot before the change.</param>
/// <param name="Current">The snapshot after the change.</param>
public sealed record CharacterMpChangedEvent(CharacterSnapshot Previous, CharacterSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character's shield percentage changes.
/// </summary>
/// <param name="Previous">The snapshot before the change.</param>
/// <param name="Current">The snapshot after the change.</param>
public sealed record CharacterShieldChangedEvent(CharacterSnapshot Previous, CharacterSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character dies.
/// </summary>
/// <param name="Previous">The snapshot before death.</param>
/// <param name="Current">The snapshot after death.</param>
public sealed record CharacterDiedEvent(CharacterSnapshot Previous, CharacterSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character is revived.
/// </summary>
/// <param name="Previous">The snapshot before revival.</param>
/// <param name="Current">The snapshot after revival.</param>
public sealed record CharacterRevivedEvent(CharacterSnapshot Previous, CharacterSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character starts casting.
/// </summary>
/// <param name="Previous">The snapshot before the cast.</param>
/// <param name="Current">The snapshot with the cast in progress (see <see cref="CharacterSnapshot.CastActionId"/>).</param>
public sealed record CharacterCastStartedEvent(CharacterSnapshot Previous, CharacterSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character's cast completes.
/// </summary>
/// <param name="Previous">The snapshot with the cast in progress.</param>
/// <param name="Current">The snapshot after the cast ended.</param>
/// <param name="CastActionId">The action row id that was being cast.</param>
public sealed record CharacterCastCompletedEvent(CharacterSnapshot Previous, CharacterSnapshot Current, uint CastActionId) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character's cast is interrupted or cancelled.
/// </summary>
/// <param name="Previous">The snapshot with the cast in progress.</param>
/// <param name="Current">The snapshot after the cast ended.</param>
/// <param name="CastActionId">The action row id that was being cast.</param>
/// <param name="IsAuthoritative">
/// True when the interrupt was confirmed by the server's ActorControl packet;
/// false when it was inferred from polling (the cast disappeared well before its total time).
/// </param>
public sealed record CharacterCastInterruptedEvent(CharacterSnapshot Previous, CharacterSnapshot Current, uint CastActionId, bool IsAuthoritative) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character enters combat.
/// </summary>
/// <param name="Previous">The snapshot before the change.</param>
/// <param name="Current">The snapshot after the change.</param>
public sealed record CharacterCombatEnteredEvent(CharacterSnapshot Previous, CharacterSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character leaves combat.
/// </summary>
/// <param name="Previous">The snapshot before the change.</param>
/// <param name="Current">The snapshot after the change.</param>
public sealed record CharacterCombatLeftEvent(CharacterSnapshot Previous, CharacterSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character's own target changes.
/// </summary>
/// <param name="Previous">The snapshot before the change (see <see cref="CharacterSnapshot.TargetEntityId"/>).</param>
/// <param name="Current">The snapshot after the change.</param>
public sealed record CharacterTargetChangedEvent(CharacterSnapshot Previous, CharacterSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character's targetability changes.
/// </summary>
/// <param name="Previous">The snapshot before the change.</param>
/// <param name="Current">The snapshot after the change.</param>
public sealed record CharacterTargetableChangedEvent(CharacterSnapshot Previous, CharacterSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character's mode changes (mount, crafting stance, looping emote, …).
/// </summary>
/// <param name="Previous">The snapshot before the change (see <see cref="CharacterSnapshot.Mode"/>).</param>
/// <param name="Current">The snapshot after the change.</param>
public sealed record CharacterModeChangedEvent(CharacterSnapshot Previous, CharacterSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character starts a looping emote (detected by mode polling).<br/>
/// For the exact emote id of any emote — including one-shot emotes — subscribe to <see cref="CharacterEmotePlayedEvent"/>.
/// </summary>
/// <param name="Previous">The snapshot before the emote.</param>
/// <param name="Current">The snapshot in the emote loop.</param>
public sealed record CharacterEmoteLoopStartedEvent(CharacterSnapshot Previous, CharacterSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character ends a looping emote.
/// </summary>
/// <param name="Previous">The snapshot in the emote loop.</param>
/// <param name="Current">The snapshot after the emote ended.</param>
public sealed record CharacterEmoteLoopEndedEvent(CharacterSnapshot Previous, CharacterSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character plays an emote — one-shot and looping alike, with the exact emote id.<br/>
/// Produced by the ActorControl hook. A one-shot emote is a fired animation, not a state: it produces this
/// single event and has no end signal. Start/end pairs exist only for looping emotes
/// (<see cref="CharacterEmoteLoopStartedEvent"/>/<see cref="CharacterEmoteLoopEndedEvent"/>).
/// </summary>
/// <param name="Character">The snapshot of the character playing the emote.</param>
/// <param name="EmoteId">The emote row id.</param>
public sealed record CharacterEmotePlayedEvent(CharacterSnapshot Character, uint EmoteId) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Character;
}

/// <summary>
/// Fired when a character's online status changes (AFK, busy, looking for party, …).
/// </summary>
/// <param name="Previous">The snapshot before the change.</param>
/// <param name="Current">The snapshot after the change.</param>
public sealed record CharacterOnlineStatusChangedEvent(CharacterSnapshot Previous, CharacterSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character's class/job changes.
/// </summary>
/// <param name="Previous">The snapshot before the change.</param>
/// <param name="Current">The snapshot after the change.</param>
public sealed record CharacterJobChangedEvent(CharacterSnapshot Previous, CharacterSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character's level changes.
/// </summary>
/// <param name="Previous">The snapshot before the change.</param>
/// <param name="Current">The snapshot after the change.</param>
public sealed record CharacterLevelChangedEvent(CharacterSnapshot Previous, CharacterSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}

/// <summary>
/// Fired when a character's identity data changes on the same entity slot (name, home world, content id) —
/// usually a sign the game reused the entity id for a different character.
/// </summary>
/// <param name="Previous">The snapshot before the change.</param>
/// <param name="Current">The snapshot after the change.</param>
public sealed record CharacterIdentityChangedEvent(CharacterSnapshot Previous, CharacterSnapshot Current) : ICharacterScopedEvent
{
    /// <inheritdoc/>
    public CharacterSnapshot Subject => Current;
}
