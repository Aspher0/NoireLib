using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using NoireLib.Hooking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Tracks action effects received from the server by hooking <see cref="ActionEffectHandler.Delegates.Receive"/>
/// via <see cref="HookWrapper{TDelegate}"/>.<br/>
/// Provides a managed event surface, a bounded history of recent action effects,
/// parsed per-target effect data, subscription helpers, and rolling statistics.
/// </summary>
public sealed class ActionEffectTracker : GameStateSubTracker
{
    private readonly LinkedList<ActionEffectEntry> actionHistory = new();
    private readonly object historyLock = new();
    private readonly int historyCapacity;
    private HookWrapper<ActionEffectHandler.Delegates.Receive> receiveActionEffectHook;
    private long totalActionsObserved;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActionEffectTracker"/> class.
    /// </summary>
    /// <param name="owner">The owning <see cref="NoireGameStateWatcher"/> module.</param>
    /// <param name="active">Whether the tracker should start in an active state.</param>
    /// <param name="historyCapacity">The maximum number of recent action effect entries to retain.</param>
    internal unsafe ActionEffectTracker(NoireGameStateWatcher owner, bool active, int historyCapacity = 50) : base(owner, active)
    {
        this.historyCapacity = Math.Max(1, historyCapacity);
        receiveActionEffectHook = new(OnActionEffectReceivedDetour, name: $"{nameof(ActionEffectTracker)}.ReceiveActionEffect");
        Statistics = new ActionEffectStatistics();
    }

    /// <summary>
    /// Gets the total number of action effect packets observed since the last activation.
    /// </summary>
    public long TotalActionsObserved => totalActionsObserved;

    /// <summary>
    /// Gets the configured maximum history size.
    /// </summary>
    public int HistoryCapacity => historyCapacity;

    /// <summary>
    /// Gets the current number of entries in the history buffer.
    /// </summary>
    public int HistoryCount
    {
        get
        {
            lock (historyLock)
                return actionHistory.Count;
        }
    }

    /// <summary>
    /// Gets the rolling statistics computed from all observed action effects.
    /// </summary>
    public ActionEffectStatistics Statistics { get; }

    /// <summary>
    /// Raised when an action effect is received from the server.
    /// </summary>
    public event Action<ActionEffectReceivedEvent>? OnActionEffectReceived;

    /// <summary>
    /// Raised when a fully captured action-effect entry is received from the server.
    /// </summary>
    public event Action<ActionEffectObservedEvent>? OnActionObserved;

    /// <summary>
    /// Raised when an action effect targeting the local player is received (incoming).
    /// </summary>
    public event Action<LocalPlayerIncomingActionEvent>? OnLocalPlayerIncoming;

    /// <summary>
    /// Raised when an action effect originating from the local player is received (outgoing).
    /// </summary>
    public event Action<LocalPlayerOutgoingActionEvent>? OnLocalPlayerOutgoing;

    /// <summary>
    /// Returns a snapshot of all entries currently in the history buffer, newest first.
    /// </summary>
    /// <returns>An array of action effect entries.</returns>
    public ActionEffectEntry[] GetRecentActions()
    {
        lock (historyLock)
            return actionHistory.ToArray();
    }

    /// <summary>
    /// Returns the most recent action-effect entries currently in the history buffer, newest first.
    /// </summary>
    /// <param name="maxCount">The maximum number of entries to return.</param>
    /// <returns>An array containing up to <paramref name="maxCount"/> recent action-effect entries.</returns>
    public ActionEffectEntry[] GetRecentActions(int maxCount)
    {
        if (maxCount < 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount));

        lock (historyLock)
            return actionHistory.Take(maxCount).ToArray();
    }

    /// <summary>
    /// Returns all entries in the history buffer matching the specified action identifier.
    /// </summary>
    /// <param name="actionId">The action row identifier to filter by.</param>
    /// <returns>An array of matching action effect entries.</returns>
    public ActionEffectEntry[] GetActionsByActionId(uint actionId)
    {
        lock (historyLock)
            return actionHistory.Where(a => a.ActionId == actionId).ToArray();
    }

    /// <summary>
    /// Returns all entries in the history buffer where the specified entity was the source.
    /// </summary>
    /// <param name="sourceEntityId">The source entity identifier to filter by.</param>
    /// <returns>An array of matching action effect entries.</returns>
    public ActionEffectEntry[] GetActionsBySource(uint sourceEntityId)
    {
        lock (historyLock)
            return actionHistory.Where(a => a.SourceEntityId == sourceEntityId).ToArray();
    }

    /// <summary>
    /// Returns all entries in the history buffer where the specified entity was hit.
    /// </summary>
    /// <param name="targetEntityId">The target entity identifier to filter by.</param>
    /// <returns>An array of matching action-effect entries.</returns>
    public ActionEffectEntry[] GetActionsByTarget(ulong targetEntityId)
    {
        lock (historyLock)
            return actionHistory.Where(action => action.TargetEntityIds.Contains(targetEntityId)).ToArray();
    }

    /// <summary>
    /// Returns all entries in the history buffer matching the specified animation target identifier.
    /// </summary>
    /// <param name="animationTargetId">The animation target identifier to filter by.</param>
    /// <returns>An array of matching action-effect entries.</returns>
    public ActionEffectEntry[] GetActionsByAnimationTarget(ulong animationTargetId)
    {
        lock (historyLock)
            return actionHistory.Where(action => action.AnimationTargetId == animationTargetId).ToArray();
    }

    /// <summary>
    /// Returns all entries in the history buffer caused by the specified live player character.
    /// </summary>
    /// <param name="contentId">The content identifier of the player to filter by.</param>
    /// <returns>An array of matching action-effect entries.</returns>
    public ActionEffectEntry[] GetActionsFromPlayer(ulong contentId)
    {
        var playerCharacter = Owner.Objects.GetPlayerCharacterByContentId(contentId);
        return playerCharacter == null ? [] : GetActionsBySource(playerCharacter.EntityId);
    }

    /// <summary>
    /// Returns all entries in the history buffer that affected the specified live player character.
    /// </summary>
    /// <param name="contentId">The content identifier of the player to filter by.</param>
    /// <returns>An array of matching action-effect entries.</returns>
    public ActionEffectEntry[] GetActionsAffectingPlayer(ulong contentId)
    {
        var playerCharacter = Owner.Objects.GetPlayerCharacterByContentId(contentId);
        return playerCharacter == null ? [] : GetActionsByTarget(playerCharacter.EntityId);
    }

    /// <summary>
    /// Returns all entries in the history buffer that affected the local player.
    /// </summary>
    /// <returns>An array of matching action-effect entries.</returns>
    public ActionEffectEntry[] GetActionsAffectingLocalPlayer()
    {
        var localPlayer = NoireService.ObjectTable.LocalPlayer;
        return localPlayer == null ? [] : GetActionsByTarget(localPlayer.EntityId);
    }

    /// <summary>
    /// Returns all entries in the history buffer originating from the local player.
    /// </summary>
    /// <returns>An array of matching action-effect entries.</returns>
    public ActionEffectEntry[] GetActionsFromLocalPlayer()
    {
        var localPlayer = NoireService.ObjectTable.LocalPlayer;
        return localPlayer == null ? [] : GetActionsBySource(localPlayer.EntityId);
    }

    /// <summary>
    /// Returns all action-effect entries in the history buffer matching the provided predicate.
    /// </summary>
    /// <param name="predicate">The filter predicate.</param>
    /// <returns>An array of matching action-effect entries.</returns>
    public ActionEffectEntry[] GetActions(Func<ActionEffectEntry, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (historyLock)
            return actionHistory.Where(predicate).ToArray();
    }

    /// <summary>
    /// Returns the most recent action-effect entry matching the specified action identifier, or <see langword="null"/> if none match.
    /// </summary>
    /// <param name="actionId">The action row identifier to filter by.</param>
    /// <returns>The most recent matching action-effect entry, or <see langword="null"/> if none match.</returns>
    public ActionEffectEntry? GetLatestActionById(uint actionId)
    {
        lock (historyLock)
            return actionHistory.FirstOrDefault(action => action.ActionId == actionId);
    }

    /// <summary>
    /// Returns the most recent action-effect entry from the specified source, or <see langword="null"/> if none match.
    /// </summary>
    /// <param name="sourceEntityId">The source entity identifier to filter by.</param>
    /// <returns>The most recent matching action-effect entry, or <see langword="null"/> if none match.</returns>
    public ActionEffectEntry? GetLatestActionFromSource(uint sourceEntityId)
    {
        lock (historyLock)
            return actionHistory.FirstOrDefault(action => action.SourceEntityId == sourceEntityId);
    }

    /// <summary>
    /// Returns the most recent action-effect entry affecting the specified target, or <see langword="null"/> if none match.
    /// </summary>
    /// <param name="targetEntityId">The target entity identifier to filter by.</param>
    /// <returns>The most recent matching action-effect entry, or <see langword="null"/> if none match.</returns>
    public ActionEffectEntry? GetLatestActionByTarget(ulong targetEntityId)
    {
        lock (historyLock)
            return actionHistory.FirstOrDefault(action => action.TargetEntityIds.Contains(targetEntityId));
    }

    /// <summary>
    /// Returns the most recent action-effect entry matching the specified animation target identifier, or <see langword="null"/> if none match.
    /// </summary>
    /// <param name="animationTargetId">The animation target identifier to filter by.</param>
    /// <returns>The most recent matching action-effect entry, or <see langword="null"/> if none match.</returns>
    public ActionEffectEntry? GetLatestActionByAnimationTarget(ulong animationTargetId)
    {
        lock (historyLock)
            return actionHistory.FirstOrDefault(action => action.AnimationTargetId == animationTargetId);
    }

    /// <summary>
    /// Checks whether an action with the specified identifier has been observed in the current history buffer.
    /// </summary>
    /// <param name="actionId">The action row identifier to search for.</param>
    /// <returns><see langword="true"/> if a matching action exists in history; otherwise, <see langword="false"/>.</returns>
    public bool WasActionObserved(uint actionId)
    {
        lock (historyLock)
            return actionHistory.Any(action => action.ActionId == actionId);
    }

    /// <summary>
    /// Checks whether an action with the specified identifier has been observed from the specified source.
    /// </summary>
    /// <param name="actionId">The action row identifier to search for.</param>
    /// <param name="sourceEntityId">The source entity identifier to match.</param>
    /// <returns><see langword="true"/> if a matching action exists in history; otherwise, <see langword="false"/>.</returns>
    public bool WasActionObserved(uint actionId, uint sourceEntityId)
    {
        lock (historyLock)
            return actionHistory.Any(action => action.ActionId == actionId && action.SourceEntityId == sourceEntityId);
    }

    /// <summary>
    /// Checks whether an action affecting the specified target has been observed.
    /// </summary>
    /// <param name="targetEntityId">The target entity identifier to search for.</param>
    /// <param name="actionId">An optional action row identifier filter.</param>
    /// <returns><see langword="true"/> if a matching action exists in history; otherwise, <see langword="false"/>.</returns>
    public bool WasActionObservedOnTarget(ulong targetEntityId, uint? actionId = null)
    {
        lock (historyLock)
        {
            return actionHistory.Any(action => action.TargetEntityIds.Contains(targetEntityId)
                && (!actionId.HasValue || action.ActionId == actionId.Value));
        }
    }

    /// <summary>
    /// Resolves the live source character for a captured action-effect entry, if available.
    /// </summary>
    /// <param name="entry">The action-effect entry to inspect.</param>
    /// <returns>The matching live character, or <see langword="null"/> if it is no longer present.</returns>
    public ICharacter? ResolveSource(ActionEffectEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Owner.Objects.GetCharacter(entry.SourceEntityId);
    }

    /// <summary>
    /// Resolves every currently live target character for a captured action-effect entry.
    /// </summary>
    /// <param name="entry">The action-effect entry to inspect.</param>
    /// <returns>An array of currently live target characters.</returns>
    public ICharacter[] ResolveTargets(ActionEffectEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.TargetEntityIds
            .Where(targetId => targetId <= uint.MaxValue)
            .Select(targetId => Owner.Objects.GetCharacter((uint)targetId))
            .OfType<ICharacter>()
            .ToArray();
    }

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when an action with the specified identifier has been observed.
    /// </summary>
    /// <param name="actionId">The action row identifier to wait for.</param>
    /// <returns>A predicate returning <see langword="true"/> when a matching action exists in history.</returns>
    public Func<bool> WaitForAction(uint actionId) => () => WasActionObserved(actionId);

    /// <summary>
    /// Subscribes to action effects targeting a specific entity and returns a subscription token.
    /// </summary>
    /// <param name="targetEntityId">The entity identifier of the target to watch.</param>
    /// <param name="callback">The callback to invoke when a matching action is observed.</param>
    /// <param name="priority">The invocation priority. Lower values are invoked first.</param>
    /// <returns>A subscription token that unregisters the callback when disposed.</returns>
    public GameStateWatcherSubscriptionToken SubscribeForTarget(ulong targetEntityId, Action<ActionEffectObservedEvent> callback, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(callback);

        return Subscribe<ActionEffectObservedEvent>(
            evt => evt.Entry.TargetEntityIds.Contains(targetEntityId),
            callback,
            priority);
    }

    /// <summary>
    /// Subscribes to action effects originating from a specific entity and returns a subscription token.
    /// </summary>
    /// <param name="sourceEntityId">The entity identifier of the source to watch.</param>
    /// <param name="callback">The callback to invoke when a matching action is observed.</param>
    /// <param name="priority">The invocation priority. Lower values are invoked first.</param>
    /// <returns>A subscription token that unregisters the callback when disposed.</returns>
    public GameStateWatcherSubscriptionToken SubscribeForSource(uint sourceEntityId, Action<ActionEffectObservedEvent> callback, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(callback);

        return Subscribe<ActionEffectObservedEvent>(
            evt => evt.Entry.SourceEntityId == sourceEntityId,
            callback,
            priority);
    }

    /// <summary>
    /// Subscribes to action effects originating from a specific player character by content identifier.
    /// </summary>
    /// <param name="contentId">The content identifier of the player to watch.</param>
    /// <param name="callback">The callback to invoke when a matching action is observed.</param>
    /// <param name="priority">The invocation priority. Lower values are invoked first.</param>
    /// <returns>A subscription token that unregisters the callback when disposed.</returns>
    public GameStateWatcherSubscriptionToken SubscribeForPlayerByCID(ulong contentId, Action<ActionEffectObservedEvent> callback, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(callback);

        return Subscribe<ActionEffectObservedEvent>(
            evt =>
            {
                var player = Owner.Objects.GetPlayerCharacterByContentId(contentId);
                return player != null && evt.Entry.SourceEntityId == player.EntityId;
            },
            callback,
            priority);
    }

    /// <summary>
    /// Returns history entries grouped by source entity, with precomputed summaries per group.
    /// </summary>
    /// <returns>An array of summaries, one per distinct source entity.</returns>
    public ActionGroupSummary[] GetHistoryGroupedBySource()
    {
        ActionEffectEntry[] snapshot;
        lock (historyLock)
            snapshot = actionHistory.ToArray();

        return snapshot
            .GroupBy(e => (ulong)e.SourceEntityId)
            .Select(BuildGroupSummary)
            .ToArray();
    }

    /// <summary>
    /// Returns history entries grouped by action identifier, with precomputed summaries per group.
    /// </summary>
    /// <returns>An array of summaries, one per distinct action identifier.</returns>
    public ActionGroupSummary[] GetHistoryGroupedByAction()
    {
        ActionEffectEntry[] snapshot;
        lock (historyLock)
            snapshot = actionHistory.ToArray();

        return snapshot
            .GroupBy(e => (ulong)e.ActionId)
            .Select(BuildGroupSummary)
            .ToArray();
    }

    /// <summary>
    /// Returns history entries grouped by each distinct target entity, with precomputed summaries per group.
    /// </summary>
    /// <returns>An array of summaries, one per distinct target entity.</returns>
    public ActionGroupSummary[] GetHistoryGroupedByTarget()
    {
        ActionEffectEntry[] snapshot;
        lock (historyLock)
            snapshot = actionHistory.ToArray();

        return snapshot
            .SelectMany(e => e.TargetEntityIds, (entry, targetId) => (targetId, entry))
            .GroupBy(t => t.targetId)
            .Select(g => BuildGroupSummary(g.Select(t => t.entry), g.Key))
            .ToArray();
    }

    /// <summary>
    /// Clears the action history buffer and resets the counter.
    /// </summary>
    public void ClearHistory()
    {
        lock (historyLock)
        {
            actionHistory.Clear();
            totalActionsObserved = 0;
        }
    }

    /// <inheritdoc/>
    protected override unsafe void OnActivated()
    {
        totalActionsObserved = 0;
        Statistics.Reset();

        receiveActionEffectHook.Enable();

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(ActionEffectTracker)} activated.");
    }

    /// <inheritdoc/>
    protected override void OnDeactivated()
    {
        receiveActionEffectHook.Disable();

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(ActionEffectTracker)} deactivated.");
    }

    /// <inheritdoc/>
    protected override void DisposeCore()
    {
        receiveActionEffectHook.Dispose();
    }

    private unsafe void OnActionEffectReceivedDetour(
        uint casterEntityId,
        Character* casterPtr,
        Vector3* targetPos,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        receiveActionEffectHook.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);

        try
        {
            ProcessActionEffect(casterEntityId, header, effects, targetEntityIds);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(Owner, ex, "Failed to process action effect.");
        }
    }

    private unsafe void ProcessActionEffect(uint casterEntityId, ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds)
    {
        totalActionsObserved++;

        var actionId = header->ActionId;
        var animationTargetId = header->AnimationTargetId.ObjectId;

        var targetCount = header->NumTargets;
        var targets = new List<ulong>(targetCount);
        var perTargetEffects = new List<PerTargetActionEffect>(targetCount);

        for (uint i = 0; i < targetCount; i++)
        {
            var targetId = targetEntityIds[i].ObjectId;
            targets.Add(targetId);

            var parsedEffects = ParseTargetEffects(effects, i);
            perTargetEffects.Add(new PerTargetActionEffect(targetId, parsedEffects));
        }

        var entry = new ActionEffectEntry(
            casterEntityId,
            actionId,
            0,
            animationTargetId,
            targets.AsReadOnly(),
            perTargetEffects.AsReadOnly(),
            DateTimeOffset.UtcNow);

        Statistics.Record(entry);

        lock (historyLock)
        {
            actionHistory.AddFirst(entry);

            while (actionHistory.Count > historyCapacity)
                actionHistory.RemoveLast();
        }

        var evt = new ActionEffectReceivedEvent(casterEntityId, actionId, targets.AsReadOnly());
        var observedEvent = new ActionEffectObservedEvent(entry);

        PublishEvent(OnActionObserved, observedEvent);
        PublishEvent(OnActionEffectReceived, evt);

        PublishLocalPlayerEvents(entry);
    }

    private void PublishLocalPlayerEvents(ActionEffectEntry entry)
    {
        var localPlayer = NoireService.ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return;

        var localEntityId = localPlayer.EntityId;

        if (entry.SourceEntityId == localEntityId)
            PublishEvent(OnLocalPlayerOutgoing, new LocalPlayerOutgoingActionEvent(entry));

        if (entry.TargetEntityIds.Contains(localEntityId))
            PublishEvent(OnLocalPlayerIncoming, new LocalPlayerIncomingActionEvent(entry));
    }

    private static unsafe List<ParsedActionEffect> ParseTargetEffects(ActionEffectHandler.TargetEffects* allEffects, uint targetIndex)
    {
        var parsed = new List<ParsedActionEffect>();
        var effectsPtr = (ulong*)allEffects;

        for (int effectSlot = 0; effectSlot < 8; effectSlot++)
        {
            var raw = effectsPtr[targetIndex * 8 + effectSlot];

            var type = (byte)(raw & 0xFF);
            if (type == 0)
                continue;

            var param0 = (byte)((raw >> 8) & 0xFF);
            var param1 = (byte)((raw >> 16) & 0xFF);
            var param2 = (byte)((raw >> 24) & 0xFF);
            var flags1 = (byte)((raw >> 32) & 0xFF);
            var flags2 = (byte)((raw >> 40) & 0xFF);
            var value = (ushort)((raw >> 48) & 0xFFFF);

            uint fullValue = value;
            if ((flags2 & 0x40) != 0)
                fullValue += (uint)param2 << 16;

            var kind = ActionEffectKind.Unknown;

            if (Enum.IsDefined(typeof(ActionEffectKind), type))
                kind = (ActionEffectKind)type;

            var isCrit = (flags1 & 0x01) != 0;
            var isDirectHit = (flags1 & 0x02) != 0;

            parsed.Add(new ParsedActionEffect(kind, fullValue, isCrit, isDirectHit, param0, param1, param2, flags1, flags2));
        }

        return parsed;
    }

    private static ActionGroupSummary BuildGroupSummary(IGrouping<ulong, ActionEffectEntry> group)
        => BuildGroupSummary(group, group.Key);

    private static ActionGroupSummary BuildGroupSummary(IEnumerable<ActionEffectEntry> entries, ulong key)
    {
        int count = 0;
        long totalDamage = 0;
        long totalHealing = 0;
        int critCount = 0;
        int directHitCount = 0;

        foreach (var entry in entries)
        {
            count++;
            foreach (var target in entry.PerTargetEffects)
            {
                foreach (var effect in target.Effects)
                {
                    if (effect.IsDamage)
                        totalDamage += effect.Value;
                    if (effect.IsHeal)
                        totalHealing += effect.Value;
                    if (effect.IsCritical)
                        critCount++;
                    if (effect.IsDirectHit)
                        directHitCount++;
                }
            }
        }

        return new ActionGroupSummary(key, count, totalDamage, totalHealing, critCount, directHitCount);
    }
}
