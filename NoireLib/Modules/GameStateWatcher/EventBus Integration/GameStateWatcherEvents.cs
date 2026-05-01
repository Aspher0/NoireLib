using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Game.Text;
using System;
using System.Collections.Generic;

namespace NoireLib.GameStateWatcher;

#region TerritoryTracker

/// <summary>
/// Published when the player changes territory.
/// </summary>
/// <param name="PreviousTerritoryId">The previous territory type identifier, or 0 on first observation.</param>
/// <param name="NewTerritoryId">The new territory type identifier.</param>
public sealed record TerritoryChangedEvent(uint PreviousTerritoryId, uint NewTerritoryId);

/// <summary>
/// Published when the map identifier changes.
/// </summary>
/// <param name="PreviousMapId">The previous map identifier, or 0 on first observation.</param>
/// <param name="NewMapId">The new map identifier.</param>
public sealed record MapChangedEvent(uint PreviousMapId, uint NewMapId);

/// <summary>
/// Published when the duty instance number changes.
/// </summary>
/// <param name="PreviousInstance">The previous instance number.</param>
/// <param name="NewInstance">The new instance number.</param>
public sealed record InstanceChangedEvent(uint PreviousInstance, uint NewInstance);

/// <summary>
/// Published when the player logs in.
/// </summary>
public sealed record PlayerLoginEvent;

/// <summary>
/// Published when the player logs out.
/// </summary>
/// <param name="Type">The logout type value provided by the client.</param>
/// <param name="Flags">The logout flags value provided by the client.</param>
public sealed record PlayerLogoutEvent(int Type, int Flags);

/// <summary>
/// Published when the local player enters PvP.
/// </summary>
/// <param name="TerritoryId">The current territory identifier when the transition was observed.</param>
public sealed record PlayerEnteredPvPEvent(uint TerritoryId);

/// <summary>
/// Published when the local player leaves PvP.
/// </summary>
/// <param name="TerritoryId">The current territory identifier when the transition was observed.</param>
public sealed record PlayerLeftPvPEvent(uint TerritoryId);

/// <summary>
/// Published when the local player enters a housing area.
/// </summary>
/// <param name="TerritoryId">The current territory identifier when the transition was observed.</param>
public sealed record PlayerEnteredHousingEvent(uint TerritoryId);

/// <summary>
/// Published when the local player leaves a housing area.
/// </summary>
/// <param name="TerritoryId">The current territory identifier when the transition was observed.</param>
public sealed record PlayerLeftHousingEvent(uint TerritoryId);

#endregion

#region PlayerStateTracker

/// <summary>
/// Published when the player's class or job changes.
/// </summary>
/// <param name="PreviousClassJobId">The previous class/job row identifier, or 0 on first observation.</param>
/// <param name="NewClassJobId">The new class/job row identifier.</param>
public sealed record ClassJobChangedEvent(uint PreviousClassJobId, uint NewClassJobId);

/// <summary>
/// Published when the player's level changes.
/// </summary>
/// <param name="ClassJobId">The class/job whose level changed.</param>
/// <param name="NewLevel">The new level value.</param>
public sealed record LevelChangedEvent(uint ClassJobId, uint NewLevel);

/// <summary>
/// Published when an observed player-character state changes.
/// </summary>
/// <param name="Previous">The previous player-character state snapshot.</param>
/// <param name="Current">The current player-character state snapshot.</param>
public sealed record CharacterStateChangedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when an observed player-character class/job changes.
/// </summary>
/// <param name="Previous">The previous player-character state snapshot.</param>
/// <param name="Current">The current player-character state snapshot.</param>
public sealed record CharacterClassJobChangedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when an observed player-character level changes.
/// </summary>
/// <param name="Previous">The previous player-character state snapshot.</param>
/// <param name="Current">The current player-character state snapshot.</param>
public sealed record CharacterLevelChangedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when an observed player-character HP changes.
/// </summary>
/// <param name="Previous">The previous player-character state snapshot.</param>
/// <param name="Current">The current player-character state snapshot.</param>
public sealed record CharacterHealthChangedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when the local player's HP changes.
/// </summary>
/// <param name="Previous">The previous local-player state snapshot.</param>
/// <param name="Current">The current local-player state snapshot.</param>
public sealed record PlayerHealthChangedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when an observed player-character MP/resource changes.
/// </summary>
/// <param name="Previous">The previous player-character state snapshot.</param>
/// <param name="Current">The current player-character state snapshot.</param>
public sealed record CharacterMpChangedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when the local player's MP/resource changes.
/// </summary>
/// <param name="Previous">The previous local-player state snapshot.</param>
/// <param name="Current">The current local-player state snapshot.</param>
public sealed record PlayerMpChangedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when an observed player-character's gathering points change (gatherer class only).
/// </summary>
/// <param name="Previous">The previous player-character state snapshot.</param>
/// <param name="Current">The current player-character state snapshot.</param>
public sealed record CharacterGpChangedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when the local player's gathering points change (gatherer class only).
/// </summary>
/// <param name="PreviousGp">The previous GP value.</param>
/// <param name="CurrentGp">The current GP value.</param>
/// <param name="MaxGp">The maximum GP value.</param>
public sealed record PlayerGpChangedEvent(uint PreviousGp, uint CurrentGp, uint MaxGp);

/// <summary>
/// Published when an observed player-character's crafting points change (crafter class only).
/// </summary>
/// <param name="Previous">The previous player-character state snapshot.</param>
/// <param name="Current">The current player-character state snapshot.</param>
public sealed record CharacterCpChangedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when the local player's crafting points change (crafter class only).
/// </summary>
/// <param name="PreviousCp">The previous CP value.</param>
/// <param name="CurrentCp">The current CP value.</param>
/// <param name="MaxCp">The maximum CP value.</param>
public sealed record PlayerCpChangedEvent(uint PreviousCp, uint CurrentCp, uint MaxCp);

/// <summary>
/// Published when an observed player-character shield percentage changes.
/// </summary>
/// <param name="Previous">The previous player-character state snapshot.</param>
/// <param name="Current">The current player-character state snapshot.</param>
public sealed record CharacterShieldChangedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when the local player's shield percentage changes.
/// </summary>
/// <param name="Previous">The previous local-player state snapshot.</param>
/// <param name="Current">The current local-player state snapshot.</param>
public sealed record PlayerShieldChangedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when an observed player-character begins casting an action.
/// </summary>
/// <param name="State">The player-character state snapshot captured when the cast started.</param>
public sealed record CharacterCastStartedEvent(CharacterStateSnapshot State);

/// <summary>
/// Published when an observed player-character stops casting (completed or interrupted).
/// </summary>
/// <param name="Previous">The player-character state snapshot captured while still casting.</param>
/// <param name="Current">The player-character state snapshot captured after the cast ended.</param>
public sealed record CharacterCastEndedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when the local player begins casting an action.
/// </summary>
/// <param name="State">The local-player state snapshot captured when the cast started.</param>
public sealed record PlayerCastStartedEvent(CharacterStateSnapshot State);

/// <summary>
/// Published when the local player stops casting (completed or interrupted).
/// </summary>
/// <param name="Previous">The local-player state snapshot captured while still casting.</param>
/// <param name="Current">The local-player state snapshot captured after the cast ended.</param>
public sealed record PlayerCastEndedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when an observed player-character enters combat.
/// </summary>
/// <param name="State">The player-character state snapshot captured upon entering combat.</param>
public sealed record CharacterEnteredCombatEvent(CharacterStateSnapshot State);

/// <summary>
/// Published when an observed player-character leaves combat.
/// </summary>
/// <param name="State">The player-character state snapshot captured upon leaving combat.</param>
public sealed record CharacterLeftCombatEvent(CharacterStateSnapshot State);

/// <summary>
/// Published when the player enters combat.
/// </summary>
public sealed record PlayerEnteredCombatEvent;

/// <summary>
/// Published when the player leaves combat.
/// </summary>
public sealed record PlayerLeftCombatEvent;

/// <summary>
/// Published when an observed player-character's targetable state changes.
/// </summary>
/// <param name="Previous">The previous player-character state snapshot.</param>
/// <param name="Current">The current player-character state snapshot.</param>
public sealed record CharacterTargetableChangedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when the local player's targetable state changes.
/// </summary>
/// <param name="Previous">The previous local-player state snapshot.</param>
/// <param name="Current">The current local-player state snapshot.</param>
public sealed record PlayerTargetableChangedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when an observed player-character's target changes.
/// </summary>
/// <param name="Previous">The previous player-character state snapshot.</param>
/// <param name="Current">The current player-character state snapshot.</param>
public sealed record CharacterTargetChangedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when the local player dies.
/// </summary>
/// <param name="State">The local-player state snapshot captured at death.</param>
public sealed record PlayerDeathEvent(CharacterStateSnapshot State);

/// <summary>
/// Published when an observed player character dies.
/// </summary>
/// <param name="State">The player-character state snapshot captured at death.</param>
public sealed record CharacterDeathEvent(CharacterStateSnapshot State);

/// <summary>
/// Published when the local player revives.
/// </summary>
/// <param name="State">The local-player state snapshot captured after revival.</param>
public sealed record PlayerRevivedEvent(CharacterStateSnapshot State);

/// <summary>
/// Published when an observed player character revives.
/// </summary>
/// <param name="State">The player-character state snapshot captured after revival.</param>
public sealed record CharacterRevivedEvent(CharacterStateSnapshot State);

/// <summary>
/// Published when a game condition flag changes.
/// </summary>
/// <param name="Flag">The condition flag that changed.</param>
/// <param name="Value">The new value of the condition flag.</param>
public sealed record ConditionChangedEvent(ConditionFlag Flag, bool Value);

/// <summary>
/// Published when the local player enters group pose.
/// </summary>
public sealed record PlayerEnteredGposeEvent;

/// <summary>
/// Published when the local player leaves group pose.
/// </summary>
public sealed record PlayerLeftGposeEvent;

/// <summary>
/// Published when the current target changes.
/// </summary>
/// <param name="PreviousTargetEntityId">The previous target entity identifier, or <see langword="null"/> if none existed.</param>
/// <param name="CurrentTargetEntityId">The current target entity identifier, or <see langword="null"/> if none exists.</param>
public sealed record TargetChangedEvent(uint? PreviousTargetEntityId, uint? CurrentTargetEntityId);

/// <summary>
/// Published when the focus target changes.
/// </summary>
/// <param name="PreviousTargetEntityId">The previous focus-target entity identifier, or <see langword="null"/> if none existed.</param>
/// <param name="CurrentTargetEntityId">The current focus-target entity identifier, or <see langword="null"/> if none exists.</param>
public sealed record FocusTargetChangedEvent(uint? PreviousTargetEntityId, uint? CurrentTargetEntityId);

/// <summary>
/// Published when the soft target changes.
/// </summary>
/// <param name="PreviousTargetEntityId">The previous soft-target entity identifier, or <see langword="null"/> if none existed.</param>
/// <param name="CurrentTargetEntityId">The current soft-target entity identifier, or <see langword="null"/> if none exists.</param>
public sealed record SoftTargetChangedEvent(uint? PreviousTargetEntityId, uint? CurrentTargetEntityId);

/// <summary>
/// Published when the player enters a cutscene.
/// </summary>
public sealed record PlayerEnteredCutsceneEvent;

/// <summary>
/// Published when the player leaves a cutscene.
/// </summary>
public sealed record PlayerLeftCutsceneEvent;

/// <summary>
/// Published when the player starts loading between areas.
/// </summary>
public sealed record PlayerStartedLoadingEvent;

/// <summary>
/// Published when the player finishes loading between areas.
/// </summary>
public sealed record PlayerFinishedLoadingEvent;

/// <summary>
/// Published when the player mounts.
/// </summary>
public sealed record PlayerMountedEvent;

/// <summary>
/// Published when the player dismounts.
/// </summary>
public sealed record PlayerUnmountedEvent;

/// <summary>
/// Published when the player starts flying.
/// </summary>
public sealed record PlayerStartedFlyingEvent;

/// <summary>
/// Published when the player stops flying.
/// </summary>
public sealed record PlayerStoppedFlyingEvent;

/// <summary>
/// Published when the player starts swimming.
/// </summary>
public sealed record PlayerStartedSwimmingEvent;

/// <summary>
/// Published when the player stops swimming.
/// </summary>
public sealed record PlayerStoppedSwimmingEvent;

/// <summary>
/// Published when an observed player-character's cast is interrupted before completion.
/// </summary>
/// <param name="Previous">The player-character state snapshot captured while still casting.</param>
/// <param name="Current">The player-character state snapshot captured after the cast was interrupted.</param>
public sealed record CharacterCastInterruptedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when an observed player-character's cast completes naturally.
/// </summary>
/// <param name="Previous">The player-character state snapshot captured while still casting.</param>
/// <param name="Current">The player-character state snapshot captured after the cast completed.</param>
public sealed record CharacterCastCompletedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when the local player's cast is interrupted before completion.
/// </summary>
/// <param name="Previous">The local-player state snapshot captured while still casting.</param>
/// <param name="Current">The local-player state snapshot captured after the cast was interrupted.</param>
public sealed record PlayerCastInterruptedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when the local player's cast completes naturally.
/// </summary>
/// <param name="Previous">The local-player state snapshot captured while still casting.</param>
/// <param name="Current">The local-player state snapshot captured after the cast completed.</param>
public sealed record PlayerCastCompletedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when an observed player-character begins a looping emote.
/// </summary>
/// <param name="State">The player-character state snapshot captured when the emote started.</param>
public sealed record CharacterEmoteStartedEvent(CharacterStateSnapshot State);

/// <summary>
/// Published when an observed player-character stops a looping emote.
/// </summary>
/// <param name="Previous">The player-character state snapshot captured while still emoting.</param>
/// <param name="Current">The player-character state snapshot captured after the emote ended.</param>
public sealed record CharacterEmoteEndedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when the local player begins a looping emote.
/// </summary>
/// <param name="State">The local-player state snapshot captured when the emote started.</param>
public sealed record PlayerEmoteStartedEvent(CharacterStateSnapshot State);

/// <summary>
/// Published when the local player stops a looping emote.
/// </summary>
/// <param name="Previous">The local-player state snapshot captured while still emoting.</param>
/// <param name="Current">The local-player state snapshot captured after the emote ended.</param>
public sealed record PlayerEmoteEndedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when an observed player-character mounts (as driver or pillion passenger).
/// </summary>
/// <param name="State">The player-character state snapshot captured after mounting.</param>
public sealed record CharacterMountedEvent(CharacterStateSnapshot State);

/// <summary>
/// Published when an observed player-character dismounts.
/// </summary>
/// <param name="Previous">The player-character state snapshot captured while still mounted.</param>
/// <param name="Current">The player-character state snapshot captured after dismounting.</param>
public sealed record CharacterUnmountedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when an observed player-character's online status changes (e.g. AFK, busy, looking for party).
/// </summary>
/// <param name="Previous">The previous player-character state snapshot.</param>
/// <param name="Current">The current player-character state snapshot.</param>
public sealed record CharacterOnlineStatusChangedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when the local player's online status changes (e.g. AFK, busy, looking for party).
/// </summary>
/// <param name="Previous">The previous local-player state snapshot.</param>
/// <param name="Current">The current local-player state snapshot.</param>
public sealed record PlayerOnlineStatusChangedEvent(CharacterStateSnapshot Previous, CharacterStateSnapshot Current);

/// <summary>
/// Published when the current target changes, including full snapshots of the previous and current target objects.
/// </summary>
/// <param name="PreviousTargetEntityId">The previous target entity identifier, or <see langword="null"/> if none existed.</param>
/// <param name="PreviousTargetSnapshot">A snapshot of the previous target object, or <see langword="null"/> if unavailable.</param>
/// <param name="CurrentTargetEntityId">The current target entity identifier, or <see langword="null"/> if none exists.</param>
/// <param name="CurrentTargetSnapshot">A snapshot of the current target object, or <see langword="null"/> if unavailable.</param>
public sealed record TargetSnapshotChangedEvent(uint? PreviousTargetEntityId, ObjectSnapshot? PreviousTargetSnapshot, uint? CurrentTargetEntityId, ObjectSnapshot? CurrentTargetSnapshot);

/// <summary>
/// Published when the focus target changes, including full snapshots of the previous and current target objects.
/// </summary>
/// <param name="PreviousTargetEntityId">The previous focus-target entity identifier, or <see langword="null"/> if none existed.</param>
/// <param name="PreviousTargetSnapshot">A snapshot of the previous focus-target object, or <see langword="null"/> if unavailable.</param>
/// <param name="CurrentTargetEntityId">The current focus-target entity identifier, or <see langword="null"/> if none exists.</param>
/// <param name="CurrentTargetSnapshot">A snapshot of the current focus-target object, or <see langword="null"/> if unavailable.</param>
public sealed record FocusTargetSnapshotChangedEvent(uint? PreviousTargetEntityId, ObjectSnapshot? PreviousTargetSnapshot, uint? CurrentTargetEntityId, ObjectSnapshot? CurrentTargetSnapshot);

/// <summary>
/// Published when the soft target changes, including full snapshots of the previous and current target objects.
/// </summary>
/// <param name="PreviousTargetEntityId">The previous soft-target entity identifier, or <see langword="null"/> if none existed.</param>
/// <param name="PreviousTargetSnapshot">A snapshot of the previous soft-target object, or <see langword="null"/> if unavailable.</param>
/// <param name="CurrentTargetEntityId">The current soft-target entity identifier, or <see langword="null"/> if none exists.</param>
/// <param name="CurrentTargetSnapshot">A snapshot of the current soft-target object, or <see langword="null"/> if unavailable.</param>
public sealed record SoftTargetSnapshotChangedEvent(uint? PreviousTargetEntityId, ObjectSnapshot? PreviousTargetSnapshot, uint? CurrentTargetEntityId, ObjectSnapshot? CurrentTargetSnapshot);

#endregion

#region ObjectTracker

/// <summary>
/// Published when a new game object appears in the object table.
/// </summary>
/// <param name="Object">A snapshot of the spawned object.</param>
public sealed record ObjectSpawnedEvent(ObjectSnapshot Object);

/// <summary>
/// Published when a game object disappears from the object table.
/// </summary>
/// <param name="Object">A snapshot of the despawned object captured before removal.</param>
public sealed record ObjectDespawnedEvent(ObjectSnapshot Object);

/// <summary>
/// Published when a tracked game object changes while remaining present in the object table.
/// </summary>
/// <param name="Previous">The previous object snapshot.</param>
/// <param name="Current">The current object snapshot.</param>
public sealed record ObjectChangedEvent(ObjectSnapshot Previous, ObjectSnapshot Current);

/// <summary>
/// Published when a tracked object enters the registered distance threshold of the local player.
/// </summary>
/// <param name="WatcherKey">The key of the distance watcher registration that fired.</param>
/// <param name="Threshold">The distance threshold that was crossed.</param>
/// <param name="Object">A snapshot of the object that entered the threshold.</param>
public sealed record ObjectEnteredDistanceEvent(string WatcherKey, float Threshold, ObjectSnapshot Object);

/// <summary>
/// Published when a tracked object leaves the registered distance threshold of the local player.
/// </summary>
/// <param name="WatcherKey">The key of the distance watcher registration that fired.</param>
/// <param name="Threshold">The distance threshold that was crossed.</param>
/// <param name="Object">A snapshot of the object that left the threshold.</param>
public sealed record ObjectLeftDistanceEvent(string WatcherKey, float Threshold, ObjectSnapshot Object);

#endregion

#region PartyTracker

/// <summary>
/// Published when the party composition changes (members join, leave, or the list is replaced).
/// </summary>
/// <param name="PreviousMembers">The party member snapshots before the change.</param>
/// <param name="CurrentMembers">The party member snapshots after the change.</param>
/// <param name="Joined">Members that were not in the previous list but are in the current list.</param>
/// <param name="Left">Members that were in the previous list but are no longer in the current list.</param>
public sealed record PartyChangedEvent(
    IReadOnlyList<PartyMemberSnapshot> PreviousMembers,
    IReadOnlyList<PartyMemberSnapshot> CurrentMembers,
    IReadOnlyList<PartyMemberSnapshot> Joined,
    IReadOnlyList<PartyMemberSnapshot> Left);

/// <summary>
/// Published when an existing party member changes while remaining in the party.
/// </summary>
/// <param name="Previous">The previous party-member snapshot.</param>
/// <param name="Current">The current party-member snapshot.</param>
public sealed record PartyMemberChangedEvent(PartyMemberSnapshot Previous, PartyMemberSnapshot Current);

#endregion

#region InventoryTracker

/// <summary>
/// Published when the game inventory changes.
/// </summary>
/// <param name="Changes">The raw inventory event arguments from Dalamud.</param>
public sealed record InventoryChangedEvent(IReadOnlyCollection<InventoryEventArgs> Changes);

#endregion

#region StatusEffectTracker

/// <summary>
/// Published when a status effect is gained on the local player.
/// </summary>
/// <param name="Status">A snapshot of the gained status effect.</param>
public sealed record StatusEffectGainedEvent(StatusEffectSnapshot Status);

/// <summary>
/// Published when a status effect is lost from the local player.
/// </summary>
/// <param name="Status">A snapshot of the lost status effect as it was before removal.</param>
public sealed record StatusEffectLostEvent(StatusEffectSnapshot Status);

/// <summary>
/// Published when an existing status effect on the local player changes (e.g. stacks or remaining time delta).
/// </summary>
/// <param name="Previous">The previous snapshot of the status effect.</param>
/// <param name="Current">The current snapshot of the status effect.</param>
public sealed record StatusEffectChangedEvent(StatusEffectSnapshot Previous, StatusEffectSnapshot Current);

/// <summary>
/// Published when a status effect is gained on a watched entity.
/// </summary>
/// <param name="EntityId">The watched entity identifier.</param>
/// <param name="Status">The gained status-effect snapshot.</param>
public sealed record TrackedStatusEffectGainedEvent(uint EntityId, StatusEffectSnapshot Status);

/// <summary>
/// Published when a status effect is lost from a watched entity.
/// </summary>
/// <param name="EntityId">The watched entity identifier.</param>
/// <param name="Status">The lost status-effect snapshot.</param>
public sealed record TrackedStatusEffectLostEvent(uint EntityId, StatusEffectSnapshot Status);

/// <summary>
/// Published when a watched entity's status effect changes.
/// </summary>
/// <param name="EntityId">The watched entity identifier.</param>
/// <param name="Previous">The previous status-effect snapshot.</param>
/// <param name="Current">The current status-effect snapshot.</param>
public sealed record TrackedStatusEffectChangedEvent(uint EntityId, StatusEffectSnapshot Previous, StatusEffectSnapshot Current);

#endregion

#region DutyTracker

/// <summary>
/// Published when a duty starts.
/// </summary>
/// <param name="TerritoryId">The territory type identifier of the duty.</param>
public sealed record DutyStartedEvent(uint TerritoryId);

/// <summary>
/// Published when the player enters the duty-finder queue.
/// </summary>
public sealed record DutyQueueEnteredEvent;

/// <summary>
/// Published when the player leaves the duty-finder queue (pop, cancel, or other).
/// </summary>
/// <param name="QueueDuration">The time spent waiting in the queue.</param>
public sealed record DutyQueueLeftEvent(TimeSpan QueueDuration);

/// <summary>
/// Published when a duty-finder match is found (duty pop / commence prompt).
/// </summary>
/// <param name="QueueDuration">The time spent waiting in the queue before the pop.</param>
public sealed record DutyCommenceEvent(TimeSpan QueueDuration);

/// <summary>
/// Published when the party wipes in a duty.
/// </summary>
/// <param name="TerritoryId">The territory type identifier of the duty.</param>
public sealed record DutyWipedEvent(uint TerritoryId);

/// <summary>
/// Published when a duty recommences after a wipe.
/// </summary>
/// <param name="TerritoryId">The territory type identifier of the duty.</param>
public sealed record DutyRecommencedEvent(uint TerritoryId);

/// <summary>
/// Published when a duty is completed.
/// </summary>
/// <param name="TerritoryId">The territory type identifier of the duty.</param>
public sealed record DutyCompletedEvent(uint TerritoryId);

#endregion

#region ChatTracker

/// <summary>
/// Published when a chat message is received.
/// </summary>
/// <param name="Type">The chat channel type.</param>
/// <param name="Timestamp">The message timestamp.</param>
/// <param name="SenderName">The sender display name.</param>
/// <param name="MessageText">The plain-text message content.</param>
public sealed record ChatMessageReceivedEvent(XivChatType Type, int Timestamp, string SenderName, string MessageText);

/// <summary>
/// Published when a chat message matches a registered <see cref="ChatRule"/>.
/// </summary>
/// <param name="Rule">The rule that matched.</param>
/// <param name="Entry">The chat message entry that triggered the rule.</param>
public sealed record ChatRuleMatchedEvent(ChatRule Rule, ChatMessageEntry Entry);

#endregion

#region ActionEffectTracker

/// <summary>
/// Published when an action effect is received from the server.
/// </summary>
/// <param name="SourceEntityId">The entity identifier of the action source.</param>
/// <param name="ActionId">The action row identifier.</param>
/// <param name="TargetEntityIds">The entity identifiers of the targets hit.</param>
public sealed record ActionEffectReceivedEvent(uint SourceEntityId, uint ActionId, IReadOnlyList<ulong> TargetEntityIds);

/// <summary>
/// Published when a fully captured action-effect entry is observed.
/// </summary>
/// <param name="Entry">The captured action-effect entry.</param>
public sealed record ActionEffectObservedEvent(ActionEffectEntry Entry);

/// <summary>
/// Published when an action effect targeting the local player is received from the server (incoming).
/// </summary>
/// <param name="Entry">The captured action-effect entry.</param>
public sealed record LocalPlayerIncomingActionEvent(ActionEffectEntry Entry);

/// <summary>
/// Published when an action effect originating from the local player is received from the server (outgoing).
/// </summary>
/// <param name="Entry">The captured action-effect entry.</param>
public sealed record LocalPlayerOutgoingActionEvent(ActionEffectEntry Entry);

#endregion

#region AddonTracker

/// <summary>
/// Published when a tracked addon changes state.
/// </summary>
/// <param name="Previous">The previous tracked addon snapshot.</param>
/// <param name="Current">The current tracked addon snapshot.</param>
public sealed record AddonStateChangedEvent(AddonStateSnapshot Previous, AddonStateSnapshot Current);

/// <summary>
/// Published when a tracked addon opens.
/// </summary>
/// <param name="Addon">The tracked addon snapshot captured after opening.</param>
public sealed record AddonOpenedEvent(AddonStateSnapshot Addon);

/// <summary>
/// Published when a tracked addon reaches its creation lifecycle stage.
/// </summary>
/// <param name="Addon">The tracked addon snapshot captured during creation.</param>
public sealed record AddonCreatedEvent(AddonStateSnapshot Addon);

/// <summary>
/// Published when a tracked addon completes setup.
/// </summary>
/// <param name="Addon">The tracked addon snapshot captured after setup.</param>
public sealed record AddonSetupEvent(AddonStateSnapshot Addon);

/// <summary>
/// Published when a tracked addon becomes available in memory.
/// </summary>
/// <param name="Addon">The tracked addon snapshot captured after becoming available.</param>
public sealed record AddonAvailableEvent(AddonStateSnapshot Addon);

/// <summary>
/// Published when a tracked addon becomes unavailable in memory.
/// </summary>
/// <param name="Addon">The tracked addon snapshot captured after becoming unavailable.</param>
public sealed record AddonUnavailableEvent(AddonStateSnapshot Addon);

/// <summary>
/// Published when a tracked addon becomes visible.
/// </summary>
/// <param name="Addon">The tracked addon snapshot captured after becoming visible.</param>
public sealed record AddonShownEvent(AddonStateSnapshot Addon);

/// <summary>
/// Published when a tracked addon becomes hidden.
/// </summary>
/// <param name="Addon">The tracked addon snapshot captured after becoming hidden.</param>
public sealed record AddonHiddenEvent(AddonStateSnapshot Addon);

/// <summary>
/// Published when a tracked addon becomes ready for interaction.
/// </summary>
/// <param name="Addon">The tracked addon snapshot captured after becoming ready.</param>
public sealed record AddonReadyEvent(AddonStateSnapshot Addon);

/// <summary>
/// Published when a tracked addon is no longer ready for interaction.
/// </summary>
/// <param name="Addon">The tracked addon snapshot captured after becoming unready.</param>
public sealed record AddonUnreadyEvent(AddonStateSnapshot Addon);

/// <summary>
/// Published when a tracked addon enters finalization.
/// </summary>
/// <param name="Addon">The tracked addon snapshot captured during finalization.</param>
public sealed record AddonFinalizedEvent(AddonStateSnapshot Addon);

/// <summary>
/// Published when a tracked addon closes.
/// </summary>
/// <param name="Addon">The tracked addon snapshot captured after closing.</param>
public sealed record AddonClosedEvent(AddonStateSnapshot Addon);

/// <summary>
/// Published when a watched addon text node changes.
/// </summary>
/// <param name="AddonName">The addon name owning the node.</param>
/// <param name="NodeIds">The node-id chain used to resolve the node.</param>
/// <param name="PreviousText">The previous resolved text value.</param>
/// <param name="CurrentText">The current resolved text value.</param>
public sealed record AddonNodeTextChangedEvent(string AddonName, IReadOnlyList<int> NodeIds, string PreviousText, string CurrentText);

/// <summary>
/// Published when a watched addon node visibility changes.
/// </summary>
/// <param name="AddonName">The addon name owning the node.</param>
/// <param name="NodeIds">The node-id chain used to resolve the node.</param>
/// <param name="PreviousVisible">The previous visibility state.</param>
/// <param name="CurrentVisible">The current visibility state.</param>
public sealed record AddonNodeVisibilityChangedEvent(string AddonName, IReadOnlyList<int> NodeIds, bool PreviousVisible, bool CurrentVisible);

/// <summary>
/// Published when a watched addon component node state changes.
/// </summary>
/// <param name="AddonName">The addon name owning the component node.</param>
/// <param name="NodeIds">The node-id chain used to resolve the node.</param>
/// <param name="PreviousExists">Whether the component previously existed.</param>
/// <param name="CurrentExists">Whether the component currently exists.</param>
/// <param name="PreviousVisible">The previous visibility state.</param>
/// <param name="CurrentVisible">The current visibility state.</param>
public sealed record AddonComponentNodeChangedEvent(string AddonName, IReadOnlyList<int> NodeIds, bool PreviousExists, bool CurrentExists, bool PreviousVisible, bool CurrentVisible);

#endregion
