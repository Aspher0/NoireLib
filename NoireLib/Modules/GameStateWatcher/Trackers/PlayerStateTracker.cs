using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel.Sheets;
using NoireLib.Events;
using NoireLib.Helpers;
using NoireLib.TaskQueue;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Tracks local-player state changes including class/job, level, GPose, targeting, game conditions,
/// and per-frame observation of HP, MP, death, combat, cast, and shield changes for visible player characters.
/// </summary>
public sealed class PlayerStateTracker : GameStateSubTracker
{
    private readonly object stateLock = new();
    private readonly Dictionary<uint, CharacterStateSnapshot> previousPlayerStates = new();
    private CharacterStateSnapshot? previousLocalPlayerState;

    private readonly EventWrapper classJobChangedEvent;
    private readonly EventWrapper levelChangedEvent;
    private readonly EventWrapper conditionChangeEvent;

    private uint lastClassJobId;
    private bool lastIsGPosing;
    private bool lastIsInCutscene;
    private bool lastIsLoading;
    private bool lastIsMounted;
    private bool lastIsInCombat;
    private bool lastIsFlying;
    private bool lastIsSwimming;
    private uint? lastTargetEntityId;
    private uint? lastFocusTargetEntityId;
    private uint? lastSoftTargetEntityId;
    private ObjectSnapshot? lastTargetSnapshot;
    private ObjectSnapshot? lastFocusTargetSnapshot;
    private ObjectSnapshot? lastSoftTargetSnapshot;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerStateTracker"/> class.
    /// </summary>
    /// <param name="owner">The owning <see cref="NoireGameStateWatcher"/> module.</param>
    /// <param name="active">Whether the tracker should start in an active state.</param>
    internal PlayerStateTracker(NoireGameStateWatcher owner, bool active) : base(owner, active)
    {
        classJobChangedEvent = new(NoireService.ClientState, nameof(IClientState.ClassJobChanged), name: $"{nameof(PlayerStateTracker)}.ClassJobChanged");
        levelChangedEvent = new(NoireService.ClientState, nameof(IClientState.LevelChanged), name: $"{nameof(PlayerStateTracker)}.LevelChanged");
        conditionChangeEvent = new(NoireService.Condition, nameof(ICondition.ConditionChange), name: $"{nameof(PlayerStateTracker)}.ConditionChange");

        classJobChangedEvent.AddCallback("handler", HandleClassJobChanged);
        levelChangedEvent.AddCallback("handler", HandleLevelChanged);
        conditionChangeEvent.AddCallback("handler", HandleConditionChange);
    }

    /// <inheritdoc/>
    protected override void OnActivated()
    {
        lock (stateLock)
        {
            previousPlayerStates.Clear();

            foreach (var playerState in CaptureVisiblePlayerStates())
                previousPlayerStates[playerState.EntityId] = playerState;

            previousLocalPlayerState = CaptureState(NoireService.ObjectTable.LocalPlayer);
        }

        lastClassJobId = NoireService.PlayerState.IsLoaded ? NoireService.PlayerState.ClassJob.RowId : 0;
        lastIsGPosing = NoireService.ClientState.IsGPosing;
        lastIsInCutscene = HasAnyCondition(ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78, ConditionFlag.OccupiedInCutSceneEvent);
        lastIsLoading = HasAnyCondition(ConditionFlag.BetweenAreas, ConditionFlag.BetweenAreas51);
        lastIsMounted = HasAnyCondition(ConditionFlag.Mounted, ConditionFlag.RidingPillion, ConditionFlag.MountImmobile);
        lastIsInCombat = HasAnyCondition(ConditionFlag.InCombat);
        lastIsFlying = HasAnyCondition(ConditionFlag.InFlight);
        lastIsSwimming = HasAnyCondition(ConditionFlag.Swimming);

        classJobChangedEvent.Enable();
        levelChangedEvent.Enable();
        conditionChangeEvent.Enable();

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(PlayerStateTracker)} activated.");
    }

    /// <inheritdoc/>
    protected override void OnDeactivated()
    {
        classJobChangedEvent.Disable();
        levelChangedEvent.Disable();
        conditionChangeEvent.Disable();

        lock (stateLock)
        {
            previousPlayerStates.Clear();
            previousLocalPlayerState = null;
        }

        lastTargetSnapshot = null;
        lastFocusTargetSnapshot = null;
        lastSoftTargetSnapshot = null;

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(PlayerStateTracker)} deactivated.");
    }

    /// <inheritdoc/>
    internal override void Update()
    {
        var currentPlayerStates = CaptureVisiblePlayerStates().ToDictionary(player => player.EntityId);
        var localPlayer = NoireService.ObjectTable.LocalPlayer;
        var currentLocalState = CaptureState(localPlayer);
        CharacterStateSnapshot[] previousStates;

        lock (stateLock)
        {
            previousStates = previousPlayerStates.Values.ToArray();

            previousPlayerStates.Clear();
            foreach (var (entityId, state) in currentPlayerStates)
                previousPlayerStates[entityId] = state;

            previousLocalPlayerState = currentLocalState;
        }

        foreach (var previousState in previousStates)
        {
            if (!currentPlayerStates.TryGetValue(previousState.EntityId, out var currentState))
                continue;

            if (previousState.HasSameObservedState(currentState))
                continue;

            PublishEvent(OnCharacterStateChanged, new CharacterStateChangedEvent(previousState, currentState));

            if (previousState.CurrentHp != currentState.CurrentHp || previousState.MaxHp != currentState.MaxHp)
                PublishEvent(OnCharacterHealthChanged, new CharacterHealthChangedEvent(previousState, currentState));

            if (previousState.CurrentMp != currentState.CurrentMp || previousState.MaxMp != currentState.MaxMp)
            {
                PublishEvent(OnCharacterMpChanged, new CharacterMpChangedEvent(previousState, currentState));

                if (IsGatherer(currentState.ClassJobId))
                    PublishEvent(OnCharacterGpChanged, new CharacterGpChangedEvent(previousState, currentState));
                else if (IsCrafter(currentState.ClassJobId))
                    PublishEvent(OnCharacterCpChanged, new CharacterCpChangedEvent(previousState, currentState));
            }

            if (previousState.ShieldPercentage != currentState.ShieldPercentage)
                PublishEvent(OnCharacterShieldChanged, new CharacterShieldChangedEvent(previousState, currentState));

            if (!previousState.IsCasting && currentState.IsCasting)
                PublishEvent(OnCharacterCastStarted, new CharacterCastStartedEvent(currentState));
            else if (previousState.IsCasting && !currentState.IsCasting)
            {
                PublishEvent(OnCharacterCastEnded, new CharacterCastEndedEvent(previousState, currentState));

                if (WasCastCompleted(previousState))
                    PublishEvent(OnCharacterCastCompleted, new CharacterCastCompletedEvent(previousState, currentState));
                else
                    PublishEvent(OnCharacterCastInterrupted, new CharacterCastInterruptedEvent(previousState, currentState));
            }

            if (!previousState.IsInCombat && currentState.IsInCombat)
                PublishEvent(OnCharacterEnteredCombat, new CharacterEnteredCombatEvent(currentState));
            else if (previousState.IsInCombat && !currentState.IsInCombat)
                PublishEvent(OnCharacterLeftCombat, new CharacterLeftCombatEvent(currentState));

            if (previousState.IsTargetable != currentState.IsTargetable)
                PublishEvent(OnCharacterTargetableChanged, new CharacterTargetableChangedEvent(previousState, currentState));

            if (previousState.TargetEntityId != currentState.TargetEntityId)
                PublishEvent(OnCharacterTargetChanged, new CharacterTargetChangedEvent(previousState, currentState));

            if (previousState.ClassJobId != currentState.ClassJobId)
                PublishEvent(OnCharacterClassJobChanged, new CharacterClassJobChangedEvent(previousState, currentState));

            if (previousState.Level != currentState.Level)
                PublishEvent(OnCharacterLevelChanged, new CharacterLevelChangedEvent(previousState, currentState));

            if (!previousState.IsDead && currentState.IsDead)
                PublishEvent(OnCharacterDeath, new CharacterDeathEvent(currentState));
            else if (previousState.IsDead && !currentState.IsDead)
                PublishEvent(OnCharacterRevived, new CharacterRevivedEvent(currentState));

            if (!previousState.IsEmoting && currentState.IsEmoting)
                PublishEvent(OnCharacterEmoteStarted, new CharacterEmoteStartedEvent(currentState));
            else if (previousState.IsEmoting && !currentState.IsEmoting)
                PublishEvent(OnCharacterEmoteEnded, new CharacterEmoteEndedEvent(previousState, currentState));

            if (!previousState.IsMounted && currentState.IsMounted)
                PublishEvent(OnCharacterMounted, new CharacterMountedEvent(currentState));
            else if (previousState.IsMounted && !currentState.IsMounted)
                PublishEvent(OnCharacterUnmounted, new CharacterUnmountedEvent(previousState, currentState));

            if (previousState.OnlineStatusId != currentState.OnlineStatusId)
                PublishEvent(OnCharacterOnlineStatusChanged, new CharacterOnlineStatusChangedEvent(previousState, currentState));

            if (currentLocalState == null || previousState.EntityId != currentLocalState.EntityId)
                continue;

            if (previousState.CurrentHp != currentState.CurrentHp || previousState.MaxHp != currentState.MaxHp)
                PublishEvent(OnHealthChanged, new PlayerHealthChangedEvent(previousState, currentState));

            if (previousState.CurrentMp != currentState.CurrentMp || previousState.MaxMp != currentState.MaxMp)
            {
                PublishEvent(OnPlayerMpChanged, new PlayerMpChangedEvent(previousState, currentState));

                if (IsGatherer(currentState.ClassJobId))
                    PublishEvent(OnPlayerGpChanged, new PlayerGpChangedEvent(previousState.CurrentMp, currentState.CurrentMp, currentState.MaxMp));
                else if (IsCrafter(currentState.ClassJobId))
                    PublishEvent(OnPlayerCpChanged, new PlayerCpChangedEvent(previousState.CurrentMp, currentState.CurrentMp, currentState.MaxMp));
            }

            if (previousState.ShieldPercentage != currentState.ShieldPercentage)
                PublishEvent(OnPlayerShieldChanged, new PlayerShieldChangedEvent(previousState, currentState));

            if (!previousState.IsCasting && currentState.IsCasting)
                PublishEvent(OnPlayerCastStarted, new PlayerCastStartedEvent(currentState));
            else if (previousState.IsCasting && !currentState.IsCasting)
            {
                PublishEvent(OnPlayerCastEnded, new PlayerCastEndedEvent(previousState, currentState));

                if (WasCastCompleted(previousState))
                    PublishEvent(OnPlayerCastCompleted, new PlayerCastCompletedEvent(previousState, currentState));
                else
                    PublishEvent(OnPlayerCastInterrupted, new PlayerCastInterruptedEvent(previousState, currentState));
            }

            if (previousState.IsTargetable != currentState.IsTargetable)
                PublishEvent(OnPlayerTargetableChanged, new PlayerTargetableChangedEvent(previousState, currentState));

            if (!previousState.IsDead && currentState.IsDead)
                PublishEvent(OnDeath, new PlayerDeathEvent(currentState));
            else if (previousState.IsDead && !currentState.IsDead)
                PublishEvent(OnRevived, new PlayerRevivedEvent(currentState));

            if (!previousState.IsEmoting && currentState.IsEmoting)
                PublishEvent(OnPlayerEmoteStarted, new PlayerEmoteStartedEvent(currentState));
            else if (previousState.IsEmoting && !currentState.IsEmoting)
                PublishEvent(OnPlayerEmoteEnded, new PlayerEmoteEndedEvent(previousState, currentState));

            if (previousState.OnlineStatusId != currentState.OnlineStatusId)
                PublishEvent(OnPlayerOnlineStatusChanged, new PlayerOnlineStatusChangedEvent(previousState, currentState));
        }

        PublishTargetChanges();

        var isGPosing = NoireService.ClientState.IsGPosing;
        if (isGPosing != lastIsGPosing)
        {
            lastIsGPosing = isGPosing;

            if (isGPosing)
                PublishEvent(OnEnteredGpose, new PlayerEnteredGposeEvent());
            else
                PublishEvent(OnLeftGpose, new PlayerLeftGposeEvent());
        }

        PublishConditionTransitions();
    }

    /// <inheritdoc/>
    protected override void DisposeCore()
    {
        classJobChangedEvent.Dispose();
        levelChangedEvent.Dispose();
        conditionChangeEvent.Dispose();
    }

    /// <summary>
    /// Gets the last captured local-player state snapshot, or <see langword="null"/> if unavailable.
    /// </summary>
    public CharacterStateSnapshot? CurrentPlayerState
    {
        get
        {
            lock (stateLock)
                return previousLocalPlayerState;
        }
    }

    /// <summary>
    /// Gets a snapshot of every currently tracked visible player state.
    /// </summary>
    public IReadOnlyList<CharacterStateSnapshot> CurrentPlayerStates
    {
        get
        {
            lock (stateLock)
                return previousPlayerStates.Values.ToArray();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the player is GPosing.
    /// </summary>
    public bool IsGPosing => NoireService.ClientState.IsGPosing;

    /// <summary>
    /// Gets the last known class/job row identifier.
    /// </summary>
    public uint CurrentClassJobId => lastClassJobId;

    /// <summary>
    /// Gets the current player level, or 0 if the player state is not loaded.
    /// </summary>
    public uint CurrentLevel => NoireService.PlayerState.IsLoaded ? (uint)NoireService.PlayerState.Level : 0;

    /// <summary>
    /// Gets the current local-player object, or <see langword="null"/> if unavailable.
    /// </summary>
    public IPlayerCharacter? LocalPlayer => NoireService.ObjectTable.LocalPlayer;

    /// <summary>
    /// Gets the local player's display name, or <see langword="null"/> if unavailable.
    /// </summary>
    public string? LocalPlayerName => LocalPlayer?.Name.TextValue;

    /// <summary>
    /// Raised when an observed player-character state changes.
    /// </summary>
    public event Action<CharacterStateChangedEvent>? OnCharacterStateChanged;

    /// <summary>
    /// Raised when an observed player-character HP changes.
    /// </summary>
    public event Action<CharacterHealthChangedEvent>? OnCharacterHealthChanged;

    /// <summary>
    /// Raised when an observed player-character class/job changes.
    /// </summary>
    public event Action<CharacterClassJobChangedEvent>? OnCharacterClassJobChanged;

    /// <summary>
    /// Raised when an observed player-character level changes.
    /// </summary>
    public event Action<CharacterLevelChangedEvent>? OnCharacterLevelChanged;

    /// <summary>
    /// Raised when an observed player-character dies.
    /// </summary>
    public event Action<CharacterDeathEvent>? OnCharacterDeath;

    /// <summary>
    /// Raised when an observed player-character revives.
    /// </summary>
    public event Action<CharacterRevivedEvent>? OnCharacterRevived;

    /// <summary>
    /// Raised when the local player's HP changes.
    /// </summary>
    public event Action<PlayerHealthChangedEvent>? OnHealthChanged;

    /// <summary>
    /// Raised when the local player dies.
    /// </summary>
    public event Action<PlayerDeathEvent>? OnDeath;

    /// <summary>
    /// Raised when the local player revives.
    /// </summary>
    public event Action<PlayerRevivedEvent>? OnRevived;

    /// <summary>
    /// Raised when an observed player-character MP/resource changes.
    /// </summary>
    public event Action<CharacterMpChangedEvent>? OnCharacterMpChanged;

    /// <summary>
    /// Raised when the local player's MP/resource changes.
    /// </summary>
    public event Action<PlayerMpChangedEvent>? OnPlayerMpChanged;

    /// <summary>
    /// Raised when an observed player-character shield percentage changes.
    /// </summary>
    public event Action<CharacterShieldChangedEvent>? OnCharacterShieldChanged;

    /// <summary>
    /// Raised when the local player's shield percentage changes.
    /// </summary>
    public event Action<PlayerShieldChangedEvent>? OnPlayerShieldChanged;

    /// <summary>
    /// Raised when an observed player-character begins casting.
    /// </summary>
    public event Action<CharacterCastStartedEvent>? OnCharacterCastStarted;

    /// <summary>
    /// Raised when an observed player-character stops casting.
    /// </summary>
    public event Action<CharacterCastEndedEvent>? OnCharacterCastEnded;

    /// <summary>
    /// Raised when the local player begins casting.
    /// </summary>
    public event Action<PlayerCastStartedEvent>? OnPlayerCastStarted;

    /// <summary>
    /// Raised when the local player stops casting.
    /// </summary>
    public event Action<PlayerCastEndedEvent>? OnPlayerCastEnded;

    /// <summary>
    /// Raised when an observed player-character enters combat.
    /// </summary>
    public event Action<CharacterEnteredCombatEvent>? OnCharacterEnteredCombat;

    /// <summary>
    /// Raised when an observed player-character leaves combat.
    /// </summary>
    public event Action<CharacterLeftCombatEvent>? OnCharacterLeftCombat;

    /// <summary>
    /// Raised when an observed player-character targetable state changes.
    /// </summary>
    public event Action<CharacterTargetableChangedEvent>? OnCharacterTargetableChanged;

    /// <summary>
    /// Raised when an observed player-character's target changes.
    /// </summary>
    public event Action<CharacterTargetChangedEvent>? OnCharacterTargetChanged;

    /// <summary>
    /// Raised when the local player's targetable state changes.
    /// </summary>
    public event Action<PlayerTargetableChangedEvent>? OnPlayerTargetableChanged;

    /// <summary>
    /// Raised when an observed player-character's gathering points change.
    /// </summary>
    public event Action<CharacterGpChangedEvent>? OnCharacterGpChanged;

    /// <summary>
    /// Raised when an observed player-character's crafting points change.
    /// </summary>
    public event Action<CharacterCpChangedEvent>? OnCharacterCpChanged;

    /// <summary>
    /// Raised when the local player's gathering points change.
    /// </summary>
    public event Action<PlayerGpChangedEvent>? OnPlayerGpChanged;

    /// <summary>
    /// Raised when the local player's crafting points change.
    /// </summary>
    public event Action<PlayerCpChangedEvent>? OnPlayerCpChanged;

    /// <summary>
    /// Raised when the player's class or job changes.
    /// </summary>
    public event Action<ClassJobChangedEvent>? OnClassJobChanged;

    /// <summary>
    /// Raised when the player's level changes.
    /// </summary>
    public event Action<LevelChangedEvent>? OnLevelChanged;

    /// <summary>
    /// Raised when a game condition flag changes.
    /// </summary>
    public event Action<ConditionChangedEvent>? OnConditionChanged;

    /// <summary>
    /// Raised when the local player enters group pose.
    /// </summary>
    public event Action<PlayerEnteredGposeEvent>? OnEnteredGpose;

    /// <summary>
    /// Raised when the local player leaves group pose.
    /// </summary>
    public event Action<PlayerLeftGposeEvent>? OnLeftGpose;

    /// <summary>
    /// Raised when the current target changes.
    /// </summary>
    public event Action<TargetChangedEvent>? OnTargetChanged;

    /// <summary>
    /// Raised when the focus target changes.
    /// </summary>
    public event Action<FocusTargetChangedEvent>? OnFocusTargetChanged;

    /// <summary>
    /// Raised when the soft target changes.
    /// </summary>
    public event Action<SoftTargetChangedEvent>? OnSoftTargetChanged;

    /// <summary>
    /// Raised when the player enters a cutscene.
    /// </summary>
    public event Action<PlayerEnteredCutsceneEvent>? OnEnteredCutscene;

    /// <summary>
    /// Raised when the player leaves a cutscene.
    /// </summary>
    public event Action<PlayerLeftCutsceneEvent>? OnLeftCutscene;

    /// <summary>
    /// Raised when the player starts loading between areas.
    /// </summary>
    public event Action<PlayerStartedLoadingEvent>? OnStartedLoading;

    /// <summary>
    /// Raised when the player finishes loading between areas.
    /// </summary>
    public event Action<PlayerFinishedLoadingEvent>? OnFinishedLoading;

    /// <summary>
    /// Raised when the player mounts.
    /// </summary>
    public event Action<PlayerMountedEvent>? OnMounted;

    /// <summary>
    /// Raised when the player dismounts.
    /// </summary>
    public event Action<PlayerUnmountedEvent>? OnUnmounted;

    /// <summary>
    /// Raised when the player enters combat.
    /// </summary>
    public event Action<PlayerEnteredCombatEvent>? OnEnteredCombat;

    /// <summary>
    /// Raised when the player leaves combat.
    /// </summary>
    public event Action<PlayerLeftCombatEvent>? OnLeftCombat;

    /// <summary>
    /// Raised when the player starts flying.
    /// </summary>
    public event Action<PlayerStartedFlyingEvent>? OnStartedFlying;

    /// <summary>
    /// Raised when the player stops flying.
    /// </summary>
    public event Action<PlayerStoppedFlyingEvent>? OnStoppedFlying;

    /// <summary>
    /// Raised when the player starts swimming.
    /// </summary>
    public event Action<PlayerStartedSwimmingEvent>? OnStartedSwimming;

    /// <summary>
    /// Raised when the player stops swimming.
    /// </summary>
    public event Action<PlayerStoppedSwimmingEvent>? OnStoppedSwimming;

    /// <summary>
    /// Raised when an observed player-character's cast is interrupted.
    /// </summary>
    public event Action<CharacterCastInterruptedEvent>? OnCharacterCastInterrupted;

    /// <summary>
    /// Raised when an observed player-character's cast completes naturally.
    /// </summary>
    public event Action<CharacterCastCompletedEvent>? OnCharacterCastCompleted;

    /// <summary>
    /// Raised when the local player's cast is interrupted.
    /// </summary>
    public event Action<PlayerCastInterruptedEvent>? OnPlayerCastInterrupted;

    /// <summary>
    /// Raised when the local player's cast completes naturally.
    /// </summary>
    public event Action<PlayerCastCompletedEvent>? OnPlayerCastCompleted;

    /// <summary>
    /// Raised when an observed player-character begins a looping emote.
    /// </summary>
    public event Action<CharacterEmoteStartedEvent>? OnCharacterEmoteStarted;

    /// <summary>
    /// Raised when an observed player-character stops a looping emote.
    /// </summary>
    public event Action<CharacterEmoteEndedEvent>? OnCharacterEmoteEnded;

    /// <summary>
    /// Raised when the local player begins a looping emote.
    /// </summary>
    public event Action<PlayerEmoteStartedEvent>? OnPlayerEmoteStarted;

    /// <summary>
    /// Raised when the local player stops a looping emote.
    /// </summary>
    public event Action<PlayerEmoteEndedEvent>? OnPlayerEmoteEnded;

    /// <summary>
    /// Raised when an observed player-character mounts.
    /// </summary>
    public event Action<CharacterMountedEvent>? OnCharacterMounted;

    /// <summary>
    /// Raised when an observed player-character dismounts.
    /// </summary>
    public event Action<CharacterUnmountedEvent>? OnCharacterUnmounted;

    /// <summary>
    /// Raised when an observed player-character's online status changes.
    /// </summary>
    public event Action<CharacterOnlineStatusChangedEvent>? OnCharacterOnlineStatusChanged;

    /// <summary>
    /// Raised when the local player's online status changes.
    /// </summary>
    public event Action<PlayerOnlineStatusChangedEvent>? OnPlayerOnlineStatusChanged;

    /// <summary>
    /// Raised when the current target changes, with full snapshots of both the previous and current target objects.
    /// </summary>
    public event Action<TargetSnapshotChangedEvent>? OnTargetSnapshotChanged;

    /// <summary>
    /// Raised when the focus target changes, with full snapshots of both the previous and current target objects.
    /// </summary>
    public event Action<FocusTargetSnapshotChangedEvent>? OnFocusTargetSnapshotChanged;

    /// <summary>
    /// Raised when the soft target changes, with full snapshots of both the previous and current target objects.
    /// </summary>
    public event Action<SoftTargetSnapshotChangedEvent>? OnSoftTargetSnapshotChanged;

    /// <summary>
    /// Retrieves snapshots for every live player character currently present in the object table.
    /// </summary>
    /// <returns>An array of current player-state snapshots.</returns>
    public CharacterStateSnapshot[] GetPlayerStates()
    {
        lock (stateLock)
            return previousPlayerStates.Values.ToArray();
    }

    /// <summary>
    /// Retrieves the current state snapshot for the supplied live player character.
    /// </summary>
    /// <param name="playerCharacter">The live player character to inspect.</param>
    /// <returns>The current player-state snapshot.</returns>
    public CharacterStateSnapshot GetPlayerState(IPlayerCharacter playerCharacter)
    {
        ArgumentNullException.ThrowIfNull(playerCharacter);
        return CaptureState(playerCharacter) ?? throw new InvalidOperationException("The supplied player character could not be captured.");
    }

    /// <summary>
    /// Retrieves the current state snapshot for the player with the supplied entity identifier.
    /// </summary>
    /// <param name="entityId">The entity identifier of the player to inspect.</param>
    /// <returns>The current player-state snapshot, or <see langword="null"/> if the player is not present.</returns>
    public CharacterStateSnapshot? GetPlayerState(uint entityId)
        => Owner.Objects.GetCharacter(entityId) is IPlayerCharacter playerCharacter ? CaptureState(playerCharacter) : null;

    /// <summary>
    /// Retrieves the current state snapshot for the player with the supplied content identifier.
    /// </summary>
    /// <param name="contentId">The content identifier of the player to inspect.</param>
    /// <returns>The current player-state snapshot, or <see langword="null"/> if the player is not present.</returns>
    public CharacterStateSnapshot? GetPlayerState(ulong contentId)
        => Owner.Objects.GetPlayerCharacterByContentId(contentId) is { } playerCharacter ? CaptureState(playerCharacter) : null;

    /// <summary>
    /// Gets the current HP for the player with the supplied entity identifier.
    /// </summary>
    /// <param name="entityId">The entity identifier of the player to inspect.</param>
    /// <returns>The current HP value, or 0 if the player is not present.</returns>
    public uint GetCurrentHp(uint entityId) => GetPlayerState(entityId)?.CurrentHp ?? 0;

    /// <summary>
    /// Gets the maximum HP for the player with the supplied entity identifier.
    /// </summary>
    /// <param name="entityId">The entity identifier of the player to inspect.</param>
    /// <returns>The maximum HP value, or 0 if the player is not present.</returns>
    public uint GetMaxHp(uint entityId) => GetPlayerState(entityId)?.MaxHp ?? 0;

    /// <summary>
    /// Gets the level for the player with the supplied entity identifier.
    /// </summary>
    /// <param name="entityId">The entity identifier of the player to inspect.</param>
    /// <returns>The level value, or 0 if the player is not present.</returns>
    public uint GetLevel(uint entityId) => GetPlayerState(entityId)?.Level ?? 0;

    /// <summary>
    /// Gets the class/job identifier for the player with the supplied entity identifier.
    /// </summary>
    /// <param name="entityId">The entity identifier of the player to inspect.</param>
    /// <returns>The class/job identifier, or 0 if the player is not present.</returns>
    public uint GetClassJobId(uint entityId) => GetPlayerState(entityId)?.ClassJobId ?? 0;

    /// <summary>
    /// Determines whether the player with the supplied entity identifier is currently dead.
    /// </summary>
    /// <param name="entityId">The entity identifier of the player to inspect.</param>
    /// <returns><see langword="true"/> if the player is dead; otherwise, <see langword="false"/>.</returns>
    public bool IsDead(uint entityId) => GetPlayerState(entityId)?.IsDead ?? false;

    /// <summary>
    /// Gets the current MP/resource value for the player with the supplied entity identifier.
    /// </summary>
    /// <param name="entityId">The entity identifier of the player to inspect.</param>
    /// <returns>The current MP value, or 0 if the player is not present.</returns>
    public uint GetCurrentMp(uint entityId) => GetPlayerState(entityId)?.CurrentMp ?? 0;

    /// <summary>
    /// Gets the maximum MP/resource value for the player with the supplied entity identifier.
    /// </summary>
    /// <param name="entityId">The entity identifier of the player to inspect.</param>
    /// <returns>The maximum MP value, or 0 if the player is not present.</returns>
    public uint GetMaxMp(uint entityId) => GetPlayerState(entityId)?.MaxMp ?? 0;

    /// <summary>
    /// Gets the shield percentage for the player with the supplied entity identifier.
    /// </summary>
    /// <param name="entityId">The entity identifier of the player to inspect.</param>
    /// <returns>The shield percentage (0–100), or 0 if the player is not present.</returns>
    public byte GetShieldPercentage(uint entityId) => GetPlayerState(entityId)?.ShieldPercentage ?? 0;

    /// <summary>
    /// Determines whether the player with the supplied entity identifier is currently casting.
    /// </summary>
    /// <param name="entityId">The entity identifier of the player to inspect.</param>
    /// <returns><see langword="true"/> if the player is casting; otherwise, <see langword="false"/>.</returns>
    public bool IsCasting(uint entityId) => GetPlayerState(entityId)?.IsCasting ?? false;

    /// <summary>
    /// Determines whether the player with the supplied entity identifier is currently in combat.
    /// </summary>
    /// <param name="entityId">The entity identifier of the player to inspect.</param>
    /// <returns><see langword="true"/> if the player is in combat; otherwise, <see langword="false"/>.</returns>
    public bool IsInCombat(uint entityId) => GetPlayerState(entityId)?.IsInCombat ?? false;

    /// <summary>
    /// Determines whether the player with the supplied entity identifier is currently targetable.
    /// </summary>
    /// <param name="entityId">The entity identifier of the player to inspect.</param>
    /// <returns><see langword="true"/> if the player is targetable; otherwise, <see langword="false"/>.</returns>
    public bool IsTargetable(uint entityId) => GetPlayerState(entityId)?.IsTargetable ?? true;

    /// <summary>
    /// Gets the target entity identifier for the player with the supplied entity identifier.
    /// </summary>
    /// <param name="entityId">The entity identifier of the player to inspect.</param>
    /// <returns>The target entity identifier, or <see langword="null"/> if the player has no target or is not present.</returns>
    public uint? GetTargetEntityId(uint entityId) => GetPlayerState(entityId)?.TargetEntityId;

    /// <summary>
    /// Returns all currently tracked dead player characters.
    /// </summary>
    /// <returns>An array of dead player-character state snapshots.</returns>
    public CharacterStateSnapshot[] GetDeadPlayers()
    {
        lock (stateLock)
            return previousPlayerStates.Values.Where(player => player.IsDead).ToArray();
    }

    /// <summary>
    /// Returns all currently tracked player characters using the specified class/job.
    /// </summary>
    /// <param name="classJobId">The class/job row identifier to filter by.</param>
    /// <returns>An array of matching player-character state snapshots.</returns>
    public CharacterStateSnapshot[] GetPlayersByClassJobId(uint classJobId)
    {
        lock (stateLock)
            return previousPlayerStates.Values.Where(player => player.ClassJobId == classJobId).ToArray();
    }

    /// <summary>
    /// Returns all currently tracked player characters that are currently in combat.
    /// </summary>
    /// <returns>An array of in-combat player-character state snapshots.</returns>
    public CharacterStateSnapshot[] GetPlayersInCombat()
    {
        lock (stateLock)
            return previousPlayerStates.Values.Where(player => player.IsInCombat).ToArray();
    }

    /// <summary>
    /// Returns all currently tracked player characters that are currently casting.
    /// </summary>
    /// <returns>An array of casting player-character state snapshots.</returns>
    public CharacterStateSnapshot[] GetCastingPlayers()
    {
        lock (stateLock)
            return previousPlayerStates.Values.Where(player => player.IsCasting).ToArray();
    }

    /// <summary>
    /// Finds the first currently tracked player character matching the provided predicate.
    /// </summary>
    /// <param name="predicate">The filter predicate.</param>
    /// <returns>The matching player-character state snapshot, or <see langword="null"/> if none matched.</returns>
    public CharacterStateSnapshot? FindPlayer(Func<CharacterStateSnapshot, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (stateLock)
            return previousPlayerStates.Values.FirstOrDefault(predicate);
    }

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the local player is dead.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when the local player is dead.</returns>
    public Func<bool> WaitForDeath() => () => CurrentPlayerState?.IsDead == true;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the local player is alive.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when the local player is alive.</returns>
    public Func<bool> WaitForRevive() => () => CurrentPlayerState is { IsDead: false };

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the local player's HP is at or below the supplied threshold.
    /// </summary>
    /// <param name="hpThreshold">The HP threshold to compare against.</param>
    /// <returns>A predicate returning <see langword="true"/> when the HP threshold is met.</returns>
    public Func<bool> WaitForHpBelowOrEqual(uint hpThreshold) => () => (CurrentPlayerState?.CurrentHp ?? uint.MaxValue) <= hpThreshold;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the local player's MP/resource is at or below the supplied threshold.
    /// </summary>
    /// <param name="mpThreshold">The MP threshold to compare against.</param>
    /// <returns>A predicate returning <see langword="true"/> when the MP threshold is met.</returns>
    public Func<bool> WaitForMpBelowOrEqual(uint mpThreshold) => () => (CurrentPlayerState?.CurrentMp ?? uint.MaxValue) <= mpThreshold;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the local player is currently casting.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when the local player is casting.</returns>
    public Func<bool> WaitForCasting() => () => CurrentPlayerState?.IsCasting == true;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the local player is not casting.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when the local player is not casting.</returns>
    public Func<bool> WaitForNotCasting() => () => CurrentPlayerState is { IsCasting: false };

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the local player is in combat.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when the local player is in combat.</returns>
    public Func<bool> WaitForCombat() => () => CurrentPlayerState?.IsInCombat == true;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the local player is out of combat.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when the local player is out of combat.</returns>
    public Func<bool> WaitForOutOfCombat() => () => CurrentPlayerState is { IsInCombat: false };

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the local player is in group pose.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when group pose is active.</returns>
    public Func<bool> WaitForGpose() => () => NoireService.ClientState.IsGPosing;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the specified condition flag matches the desired value.<br/>
    /// Useful as a wait condition for <see cref="NoireTaskQueue"/>.
    /// </summary>
    /// <param name="flag">The condition flag to watch.</param>
    /// <param name="value">The desired value of the condition flag.</param>
    /// <returns>A predicate returning <see langword="true"/> when the condition matches.</returns>
    public Func<bool> WaitForCondition(ConditionFlag flag, bool value = true) => () => NoireService.Condition[flag] == value;

    /// <summary>
    /// Checks the state of a game condition flag.
    /// </summary>
    /// <param name="flag">The condition flag to check.</param>
    /// <returns><see langword="true"/> if the condition is active; otherwise, <see langword="false"/>.</returns>
    public bool CheckCondition(ConditionFlag flag) => NoireService.Condition[flag];

    /// <summary>
    /// Checks the state of multiple game condition flags with either "all" or "any" logic.
    /// </summary>
    /// <param name="requireAll">If <see langword="true"/>, requires all flags to be active; if <see langword="false"/>, requires at least one flag to be active.</param>
    /// <param name="flags">The condition flags to check.</param>
    /// <returns><see langword="true"/> if the specified condition logic is satisfied; otherwise, <see langword="false"/>.</returns>
    public bool CheckConditions(bool requireAll, params ConditionFlag[] flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        return requireAll ? HasAllConditions(flags) : HasAnyCondition(flags);
    }

    /// <summary>
    /// Checks whether all specified condition flags are currently active.
    /// </summary>
    /// <param name="flags">The condition flags to check.</param>
    /// <returns><see langword="true"/> if all condition flags are active; otherwise, <see langword="false"/>.</returns>
    public bool HasAllConditions(params ConditionFlag[] flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        return flags.All(CheckCondition);
    }

    /// <summary>
    /// Checks whether any of the specified condition flags are currently active.
    /// </summary>
    /// <param name="flags">The condition flags to check.</param>
    /// <returns><see langword="true"/> if any condition flag is active; otherwise, <see langword="false"/>.</returns>
    public bool HasAnyCondition(params ConditionFlag[] flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        return flags.Any(CheckCondition);
    }

    /// <summary>
    /// Retrieves all currently active game condition flags.
    /// </summary>
    /// <returns>An array containing every active condition flag.</returns>
    public ConditionFlag[] GetActiveConditions() => NoireService.Condition.AsReadOnlySet().ToArray();

    private CharacterStateSnapshot[] CaptureVisiblePlayerStates()
        => Owner.Objects.GetPlayerCharacters()
            .Select(CaptureState)
            .OfType<CharacterStateSnapshot>()
            .ToArray();

    private static unsafe CharacterStateSnapshot? CaptureState(IPlayerCharacter? playerCharacter)
    {
        if (playerCharacter is not IBattleChara battleChara)
            return null;

        var native = (Character*)playerCharacter.Address;

        return new CharacterStateSnapshot(
            playerCharacter.EntityId,
            playerCharacter.Name.TextValue,
            playerCharacter.ClassJob.Value.RowId,
            playerCharacter.Level,
            battleChara.CurrentHp,
            battleChara.MaxHp,
            battleChara.CurrentMp,
            battleChara.MaxMp,
            battleChara.ShieldPercentage,
            battleChara.IsCasting,
            battleChara.CastActionId,
            (uint)battleChara.CastTargetObjectId,
            battleChara.TotalCastTime,
            battleChara.CurrentCastTime,
            (battleChara.StatusFlags & StatusFlags.InCombat) != 0,
            playerCharacter.IsTargetable,
            ResolveTargetEntityId(playerCharacter.TargetObjectId),
            battleChara.CurrentHp == 0 && battleChara.MaxHp > 0,
            (byte)native->Mode,
            native->ModeParam,
            (uint)native->OnlineStatus,
            DateTimeOffset.UtcNow);
    }

    private static bool IsGatherer(uint classJobId)
        => ExcelSheetHelper.TryGetRow<ClassJob>(classJobId, out var classJob)
        && classJob?.ClassJobCategory.RowId == 32;

    private static bool IsCrafter(uint classJobId)
        => ExcelSheetHelper.TryGetRow<ClassJob>(classJobId, out var classJob)
        && classJob?.ClassJobCategory.RowId == 33;

    /// <summary>
    /// Converts a raw <see cref="IGameObject.TargetObjectId"/> value
    /// to a nullable entity identifier, returning <see langword="null"/> for the no-target sentinel.
    /// </summary>
    /// <param name="targetObjectId">The raw target object identifier.</param>
    /// <returns>The entity identifier of the target, or <see langword="null"/> if no target exists.</returns>
    private static uint? ResolveTargetEntityId(ulong targetObjectId)
        => targetObjectId is 0 or 0xE0000000 ? null : (uint)targetObjectId;

    private void HandleClassJobChanged(uint newClassJobId)
    {
        var previous = lastClassJobId;
        lastClassJobId = newClassJobId;

        var evt = new ClassJobChangedEvent(previous, newClassJobId);

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"ClassJob changed: {previous} -> {newClassJobId}.");

        PublishEvent(OnClassJobChanged, evt);
    }

    private void HandleLevelChanged(uint classJobId, uint level)
    {
        var evt = new LevelChangedEvent(classJobId, level);

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"Level changed: ClassJob {classJobId} -> Level {level}.");

        PublishEvent(OnLevelChanged, evt);
    }

    private void HandleConditionChange(ConditionFlag flag, bool value)
    {
        var evt = new ConditionChangedEvent(flag, value);
        PublishEvent(OnConditionChanged, evt);
    }

    private void PublishTargetChanges()
    {
        var currentTargetEntityId = GetLocalPlayerTargetEntityId(NoireService.TargetManager.Target);
        if (currentTargetEntityId != lastTargetEntityId)
        {
            var currentTargetSnapshot = ResolveObjectSnapshot(currentTargetEntityId);
            PublishEvent(OnTargetChanged, new TargetChangedEvent(lastTargetEntityId, currentTargetEntityId));
            PublishEvent(OnTargetSnapshotChanged, new TargetSnapshotChangedEvent(lastTargetEntityId, lastTargetSnapshot, currentTargetEntityId, currentTargetSnapshot));
            lastTargetEntityId = currentTargetEntityId;
            lastTargetSnapshot = currentTargetSnapshot;
        }

        var currentFocusTargetEntityId = GetLocalPlayerTargetEntityId(NoireService.TargetManager.FocusTarget);
        if (currentFocusTargetEntityId != lastFocusTargetEntityId)
        {
            var currentFocusTargetSnapshot = ResolveObjectSnapshot(currentFocusTargetEntityId);
            PublishEvent(OnFocusTargetChanged, new FocusTargetChangedEvent(lastFocusTargetEntityId, currentFocusTargetEntityId));
            PublishEvent(OnFocusTargetSnapshotChanged, new FocusTargetSnapshotChangedEvent(lastFocusTargetEntityId, lastFocusTargetSnapshot, currentFocusTargetEntityId, currentFocusTargetSnapshot));
            lastFocusTargetEntityId = currentFocusTargetEntityId;
            lastFocusTargetSnapshot = currentFocusTargetSnapshot;
        }

        var currentSoftTargetEntityId = GetLocalPlayerTargetEntityId(NoireService.TargetManager.SoftTarget);
        if (currentSoftTargetEntityId != lastSoftTargetEntityId)
        {
            var currentSoftTargetSnapshot = ResolveObjectSnapshot(currentSoftTargetEntityId);
            PublishEvent(OnSoftTargetChanged, new SoftTargetChangedEvent(lastSoftTargetEntityId, currentSoftTargetEntityId));
            PublishEvent(OnSoftTargetSnapshotChanged, new SoftTargetSnapshotChangedEvent(lastSoftTargetEntityId, lastSoftTargetSnapshot, currentSoftTargetEntityId, currentSoftTargetSnapshot));
            lastSoftTargetEntityId = currentSoftTargetEntityId;
            lastSoftTargetSnapshot = currentSoftTargetSnapshot;
        }
    }

    private void PublishConditionTransitions()
    {
        PublishConditionTransition(HasAnyCondition(ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78, ConditionFlag.OccupiedInCutSceneEvent), ref lastIsInCutscene, OnEnteredCutscene, OnLeftCutscene, new PlayerEnteredCutsceneEvent(), new PlayerLeftCutsceneEvent());
        PublishConditionTransition(HasAnyCondition(ConditionFlag.BetweenAreas, ConditionFlag.BetweenAreas51), ref lastIsLoading, OnStartedLoading, OnFinishedLoading, new PlayerStartedLoadingEvent(), new PlayerFinishedLoadingEvent());
        PublishConditionTransition(HasAnyCondition(ConditionFlag.Mounted, ConditionFlag.RidingPillion, ConditionFlag.MountImmobile), ref lastIsMounted, OnMounted, OnUnmounted, new PlayerMountedEvent(), new PlayerUnmountedEvent());
        PublishConditionTransition(HasAnyCondition(ConditionFlag.InCombat), ref lastIsInCombat, OnEnteredCombat, OnLeftCombat, new PlayerEnteredCombatEvent(), new PlayerLeftCombatEvent());
        PublishConditionTransition(HasAnyCondition(ConditionFlag.InFlight), ref lastIsFlying, OnStartedFlying, OnStoppedFlying, new PlayerStartedFlyingEvent(), new PlayerStoppedFlyingEvent());
        PublishConditionTransition(HasAnyCondition(ConditionFlag.Swimming), ref lastIsSwimming, OnStartedSwimming, OnStoppedSwimming, new PlayerStartedSwimmingEvent(), new PlayerStoppedSwimmingEvent());
    }

    private void PublishConditionTransition<TEnteredEvent, TLeftEvent>(bool currentValue, ref bool previousValue, Action<TEnteredEvent>? enteredHandler, Action<TLeftEvent>? leftHandler, TEnteredEvent enteredEvent, TLeftEvent leftEvent)
    {
        if (currentValue == previousValue)
            return;

        previousValue = currentValue;

        if (currentValue)
            PublishEvent(enteredHandler, enteredEvent);
        else
            PublishEvent(leftHandler, leftEvent);
    }

    private static uint? GetLocalPlayerTargetEntityId(IGameObject? target)
        => target?.EntityId;

    /// <summary>
    /// Determines whether a cast was completed naturally based on the casting snapshot's progress.
    /// A cast is considered completed if it reached within 100 ms of the total cast time.
    /// </summary>
    /// <param name="castingState">The snapshot captured while the character was still casting.</param>
    /// <returns><see langword="true"/> if the cast was completed; <see langword="false"/> if it was interrupted.</returns>
    private static bool WasCastCompleted(CharacterStateSnapshot castingState)
    {
        if (castingState.TotalCastTime <= 0f)
            return true;

        return castingState.CurrentCastTime >= castingState.TotalCastTime - 0.1f;
    }

    /// <summary>
    /// Resolves a nullable entity identifier to an <see cref="ObjectSnapshot"/> by looking up the object table.
    /// </summary>
    /// <param name="entityId">The entity identifier to resolve, or <see langword="null"/>.</param>
    /// <returns>A snapshot of the resolved game object, or <see langword="null"/> if the entity was not found.</returns>
    private static ObjectSnapshot? ResolveObjectSnapshot(uint? entityId)
    {
        if (entityId is not { } id)
            return null;

        var gameObject = NoireService.ObjectTable.FirstOrDefault(o => o != null && o.EntityId == id);
        if (gameObject == null)
            return null;

        return new ObjectSnapshot(
            gameObject.EntityId,
            gameObject.BaseId,
            gameObject.Name.TextValue,
            gameObject.ObjectKind,
            gameObject.SubKind,
            gameObject.Position);
    }

    /// <summary>
    /// Returns all currently tracked player characters that are performing a looping emote.
    /// </summary>
    /// <returns>An array of emoting player-character state snapshots.</returns>
    public CharacterStateSnapshot[] GetEmotingPlayers()
    {
        lock (stateLock)
            return previousPlayerStates.Values.Where(player => player.IsEmoting).ToArray();
    }

    /// <summary>
    /// Returns all currently tracked player characters that are mounted.
    /// </summary>
    /// <returns>An array of mounted player-character state snapshots.</returns>
    public CharacterStateSnapshot[] GetMountedPlayers()
    {
        lock (stateLock)
            return previousPlayerStates.Values.Where(player => player.IsMounted).ToArray();
    }

    /// <summary>
    /// Determines whether the player with the supplied entity identifier is currently performing a looping emote.
    /// </summary>
    /// <param name="entityId">The entity identifier of the player to inspect.</param>
    /// <returns><see langword="true"/> if the player is emoting; otherwise, <see langword="false"/>.</returns>
    public bool IsEmoting(uint entityId) => GetPlayerState(entityId)?.IsEmoting ?? false;

    /// <summary>
    /// Determines whether the player with the supplied entity identifier is currently mounted.
    /// </summary>
    /// <param name="entityId">The entity identifier of the player to inspect.</param>
    /// <returns><see langword="true"/> if the player is mounted; otherwise, <see langword="false"/>.</returns>
    public bool IsMounted(uint entityId) => GetPlayerState(entityId)?.IsMounted ?? false;

    /// <summary>
    /// Gets the online-status row identifier for the player with the supplied entity identifier.
    /// </summary>
    /// <param name="entityId">The entity identifier of the player to inspect.</param>
    /// <returns>The online-status row identifier, or 0 if the player is not present.</returns>
    public uint GetOnlineStatusId(uint entityId) => GetPlayerState(entityId)?.OnlineStatusId ?? 0;

    /// <summary>
    /// Gets the raw character mode for the player with the supplied entity identifier.
    /// </summary>
    /// <param name="entityId">The entity identifier of the player to inspect.</param>
    /// <returns>The character mode byte value, or 0 if the player is not present.</returns>
    public byte GetCharacterMode(uint entityId) => GetPlayerState(entityId)?.CharacterMode ?? 0;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the local player is performing a looping emote.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when the local player is emoting.</returns>
    public Func<bool> WaitForEmoting() => () => CurrentPlayerState?.IsEmoting == true;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the local player is not performing a looping emote.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when the local player is not emoting.</returns>
    public Func<bool> WaitForNotEmoting() => () => CurrentPlayerState is { IsEmoting: false };

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the local player is mounted.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when the local player is mounted.</returns>
    public Func<bool> WaitForMounted() => () => CurrentPlayerState?.IsMounted == true;

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the local player is not mounted.
    /// </summary>
    /// <returns>A predicate returning <see langword="true"/> when the local player is not mounted.</returns>
    public Func<bool> WaitForNotMounted() => () => CurrentPlayerState is { IsMounted: false };
}
