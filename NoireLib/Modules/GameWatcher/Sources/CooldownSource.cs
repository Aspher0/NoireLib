using FFXIVClientStructs.FFXIV.Client.Game;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameWatcher;

/// <summary>
/// Cooldown facts. Local player: exact recast/charge/GCD state read from the game's action manager, diffed
/// per watched action id. Other characters: <b>estimates</b> inferred from observed action usage (via the
/// ActionEffect source) plus sheet recast data - doctrine tier 4, always <see cref="CooldownSnapshot.IsEstimate"/>.
/// </summary>
internal sealed class CooldownSource : GameWatcherSource
{
    private sealed class WatchedAction
    {
        public bool WasReady = true;
        public uint LastCharges;
        public bool Seeded;
    }

    private readonly Dictionary<uint, WatchedAction> watchedActions = new();
    private readonly Dictionary<(uint EntityId, uint ActionId), (DateTimeOffset ReadyAt, float TotalRecast)> estimates = new();
    private int estimateInterest;
    private bool lastGcdReady = true;
    private const int GcdRecastGroupIndex = 57;
    private const int MaxEstimateEntries = 512;

    public CooldownSource(NoireGameWatcher owner) : base(owner, SourceKind.Cooldowns) { }

    /// <summary>Registers a local action id to watch and returns the removal action.</summary>
    internal Action AddWatchedAction(uint actionId)
    {
        lock (watchedActions)
        {
            if (!watchedActions.ContainsKey(actionId))
                watchedActions[actionId] = new WatchedAction();
        }

        return () =>
        {
            lock (watchedActions)
                watchedActions.Remove(actionId);
        };
    }

    /// <summary>Registers estimate interest (other characters' cooldowns) and returns the removal action.</summary>
    internal Action AddEstimateInterest()
    {
        System.Threading.Interlocked.Increment(ref estimateInterest);
        return () => System.Threading.Interlocked.Decrement(ref estimateInterest);
    }

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        lock (watchedActions)
        {
            foreach (var watched in watchedActions.Values)
                watched.Seeded = false;
        }

        estimates.Clear();
        lastGcdReady = ReadGcdReady(out _);
    }

    /// <inheritdoc/>
    protected override void OnDeactivate()
    {
        estimates.Clear();
    }

    /// <inheritdoc/>
    protected override void OnTick(DateTimeOffset now)
    {
        if (!NoireService.ClientState.IsLoggedIn)
            return;

        TickLocalCooldowns(now);
        TickGcd();
        TickEstimates(now);
    }

    private void TickLocalCooldowns(DateTimeOffset now)
    {
        (uint ActionId, WatchedAction State)[] snapshot;

        lock (watchedActions)
        {
            if (watchedActions.Count == 0)
                return;

            snapshot = watchedActions.Select(pair => (pair.Key, pair.Value)).ToArray();
        }

        foreach (var (actionId, state) in snapshot)
        {
            var cooldown = ReadLocalCooldown(actionId, now);

            if (cooldown == null)
                continue;

            if (!state.Seeded)
            {
                // Baseline seeding without events.
                state.Seeded = true;
                state.WasReady = cooldown.IsReady;
                state.LastCharges = cooldown.CurrentCharges;
                continue;
            }

            if (cooldown.CurrentCharges != state.LastCharges)
            {
                var previousCharges = state.LastCharges;
                state.LastCharges = cooldown.CurrentCharges;
                Owner.DispatchEvent(new ChargesChangedEvent(previousCharges, cooldown));
            }

            if (cooldown.IsReady != state.WasReady)
            {
                state.WasReady = cooldown.IsReady;

                if (cooldown.IsReady)
                    Owner.DispatchEvent(new CooldownEndedEvent(cooldown));
                else
                    Owner.DispatchEvent(new CooldownStartedEvent(cooldown));
            }
        }
    }

    private void TickGcd()
    {
        var isReady = ReadGcdReady(out var remaining);

        if (isReady == lastGcdReady)
            return;

        lastGcdReady = isReady;
        Owner.DispatchEvent(new GcdStateChangedEvent(isReady, remaining));
    }

    private void TickEstimates(DateTimeOffset now)
    {
        if (estimates.Count == 0)
            return;

        List<(uint EntityId, uint ActionId)>? elapsed = null;

        foreach (var (key, value) in estimates)
        {
            if (now >= value.ReadyAt)
                (elapsed ??= new List<(uint, uint)>()).Add(key);
        }

        if (elapsed == null)
            return;

        foreach (var key in elapsed)
        {
            var (readyAt, total) = estimates[key];
            estimates.Remove(key);

            Owner.DispatchEvent(new EstimatedCooldownEndedEvent(new CooldownSnapshot
            {
                ActionId = key.ActionId,
                EntityId = key.EntityId,
                IsReady = true,
                Remaining = 0,
                Total = total,
                CurrentCharges = 0,
                MaxCharges = 0,
                IsEstimate = true,
                CapturedAt = readyAt,
            }));
        }
    }

    /// <summary>
    /// Feeds an observed action into the estimation store (called by the ActionEffect source).
    /// No-ops unless this source runs and estimate interest exists.
    /// </summary>
    internal void ObserveAction(ActionEffectEntry entry)
    {
        if (!IsRunning || System.Threading.Volatile.Read(ref estimateInterest) <= 0)
            return;

        if (entry.SourceEntityId == 0 || entry.SourceEntityId == CharacterCapture.LocalEntityId())
            return;

        var recastSeconds = ReadSheetRecastSeconds(entry.ActionId);

        if (recastSeconds <= 0)
            return;

        var now = DateTimeOffset.UtcNow;
        var key = (entry.SourceEntityId, entry.ActionId);

        if (estimates.Count >= MaxEstimateEntries && !estimates.ContainsKey(key))
        {
            // Bound the store: drop the estimate closest to expiry.
            var oldest = estimates.OrderBy(pair => pair.Value.ReadyAt).First().Key;
            estimates.Remove(oldest);
        }

        estimates[key] = (now.AddSeconds(recastSeconds), recastSeconds);

        Owner.DispatchEvent(new EstimatedCooldownStartedEvent(new CooldownSnapshot
        {
            ActionId = entry.ActionId,
            EntityId = entry.SourceEntityId,
            IsReady = false,
            Remaining = recastSeconds,
            Total = recastSeconds,
            CurrentCharges = 0,
            MaxCharges = 0,
            IsEstimate = true,
            CapturedAt = now,
        }));
    }

    /// <summary>The current estimate for another character's action, or null when never observed / elapsed.</summary>
    internal CooldownSnapshot? GetEstimate(uint entityId, uint actionId, DateTimeOffset now)
    {
        if (!estimates.TryGetValue((entityId, actionId), out var estimate))
            return null;

        var remaining = (float)(estimate.ReadyAt - now).TotalSeconds;

        return new CooldownSnapshot
        {
            ActionId = actionId,
            EntityId = entityId,
            IsReady = remaining <= 0,
            Remaining = Math.Max(0, remaining),
            Total = estimate.TotalRecast,
            CurrentCharges = 0,
            MaxCharges = 0,
            IsEstimate = true,
            CapturedAt = now,
        };
    }

    /// <summary>Reads the exact local recast state for an action, or null when unavailable.</summary>
    internal static unsafe CooldownSnapshot? ReadLocalCooldown(uint actionId, DateTimeOffset now)
    {
        var manager = ActionManager.Instance();

        if (manager == null)
            return null;

        var adjustedId = manager->GetAdjustedActionId(actionId);
        var recastActive = manager->IsRecastTimerActive(ActionType.Action, adjustedId);
        var recastTotal = manager->GetRecastTime(ActionType.Action, adjustedId);
        var recastElapsed = manager->GetRecastTimeElapsed(ActionType.Action, adjustedId);
        var maxCharges = ActionManager.GetMaxCharges(adjustedId, 0);
        var currentCharges = maxCharges > 1 ? manager->GetCurrentCharges(adjustedId) : (recastActive ? 0u : 1u);
        var remaining = recastActive ? Math.Max(0, recastTotal - recastElapsed) : 0;

        return new CooldownSnapshot
        {
            ActionId = adjustedId,
            EntityId = CharacterCapture.LocalEntityId(),
            IsReady = !recastActive || currentCharges > 0,
            Remaining = remaining,
            Total = recastTotal,
            CurrentCharges = currentCharges,
            MaxCharges = Math.Max(1u, maxCharges),
            IsEstimate = false,
            CapturedAt = now,
        };
    }

    /// <summary>Reads the local GCD state (recast group 58).</summary>
    internal static unsafe bool ReadGcdReady(out float remaining)
    {
        remaining = 0;

        var manager = ActionManager.Instance();

        if (manager == null)
            return true;

        var detail = manager->GetRecastGroupDetail(GcdRecastGroupIndex);

        if (detail == null || !detail->IsActive)
            return true;

        remaining = Math.Max(0, detail->Total - detail->Elapsed);
        return remaining <= 0;
    }

    private static float ReadSheetRecastSeconds(uint actionId)
        => ExcelSheetHelper.TryGetRow<Lumina.Excel.Sheets.Action>(actionId, out var action)
            ? (action?.Recast100ms ?? 0) / 10f
            : 0f;
}
