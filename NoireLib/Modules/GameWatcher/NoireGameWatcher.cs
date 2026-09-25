using Dalamud.Plugin.Services;
using NoireLib.Core.Modules;
using NoireLib.Core.Subscriptions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameWatcher;

/// <summary>
/// Watches characters, objects, party, zones, duties, chat, combat, statuses, addons and inventory. Sources run on demand.
/// Everything runs on the framework thread: <b>never block on a watcher task there, await it.</b>
/// </summary>
public partial class NoireGameWatcher : NoireModuleWithWindowBase<NoireGameWatcher, GameWatcherDiagnosticsWindow>
{
    private readonly object gate = new();

    // Refcount changes and the activation decision share it: a source never activates twice.
    private readonly object sourceGate = new();

    private NoireSubscriptionRegistry<Type, object> registry = null!;
    private Dictionary<SourceKind, GameWatcherSource> sources = null!;

    // Built once: the tick reads it without locking.
    private GameWatcherSource[] orderedSources = Array.Empty<GameWatcherSource>();

    private GameWatcherOptions options = new();
    private GameWatcherOptions? activeOptions;
    private bool frameworkAttached;

    /// <summary>The default constructor. Configure <see cref="Options"/> before activating.</summary>
    public NoireGameWatcher() : base((string?)null, false, true) { }

    /// <summary>Creates a new game watcher.</summary>
    /// <param name="options">Optional settings; everything works with none.</param>
    /// <param name="moduleId">Optional module ID for multiple watcher instances.</param>
    /// <param name="active">Whether to activate on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    public NoireGameWatcher(
        GameWatcherOptions? options,
        string? moduleId = null,
        bool active = true,
        bool enableLogging = true) : base(moduleId, false, enableLogging)
    {
        if (options != null)
            this.options = options.Clone();

        if (active)
            SetActive(true);
    }

    internal NoireGameWatcher(ModuleId? moduleId, bool active = true, bool enableLogging = true) : base(moduleId, active, enableLogging) { }

    /// <inheritdoc/>
    protected override void InitializeModule(params object?[] args)
    {
        registry = new((ex, description) => NoireLogger.LogError(this, ex, $"Unhandled exception in {description}."));

        sources = new Dictionary<SourceKind, GameWatcherSource>
        {
            [SourceKind.Session] = new SessionSource(this),
            [SourceKind.Condition] = new ConditionSource(this),
            [SourceKind.Characters] = new CharacterSource(this),
            [SourceKind.Objects] = new ObjectSource(this),
            [SourceKind.Party] = new PartySource(this),
            [SourceKind.Friends] = new FriendSource(this),
            [SourceKind.Targets] = new TargetSource(this),
            [SourceKind.Duty] = new DutySource(this),
            [SourceKind.Chat] = new ChatSource(this),
            [SourceKind.ActionEffect] = new ActionEffectSource(this),
            [SourceKind.Cooldowns] = new CooldownSource(this),
            [SourceKind.Statuses] = new StatusSource(this),
            [SourceKind.Addons] = new AddonSource(this),
            [SourceKind.Inventory] = new InventorySource(this),
            [SourceKind.Fate] = new FateSource(this),
            [SourceKind.Weather] = new WeatherSource(this),
            [SourceKind.EorzeaTime] = new EorzeaTimeSource(this),
            [SourceKind.Toast] = new ToastSource(this),
        };

        orderedSources = sources.Values.ToArray();

        Characters = new CharacterWatcher(this);
        Objects = new ObjectWatcher(this);
        Party = new PartyWatcher(this);
        Friends = new FriendWatcher(this);
        Targets = new TargetWatcher(this);
        Zone = new ZoneWatcher(this);
        Duty = new DutyWatcher(this);
        Chat = new ChatWatcher(this);
        Combat = new CombatWatcher(this);
        Cooldowns = new CooldownWatcher(this);
        Statuses = new StatusWatcher(this);
        Addons = new AddonWatcher(this);
        Inventory = new InventoryWatcher(this);
        Conditions = new ConditionWatcher(this);
        Fates = new FateWatcher(this);
        Toasts = new ToastWatcher(this);

        // No window system without the game. ShowDiagnostics registers the window lazily.
        if (NoireService.NoireWindowSystem != null)
            RegisterWindow(new GameWatcherDiagnosticsWindow(this));
    }

    /// <summary>
    /// Opens (or focuses) the diagnostics window: per-source state, interest masks, event counters,
    /// live subscriptions, waits and the recent-event log.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireGameWatcher ShowDiagnostics()
    {
        if (!HasWindow && NoireService.NoireWindowSystem != null)
            RegisterWindow(new GameWatcherDiagnosticsWindow(this));

        ShowWindow();
        return this;
    }

    #region Public state & facades

    /// <summary>The options. A change applies once the watcher restarts.</summary>
    public GameWatcherOptions Options => options;

    internal GameWatcherOptions ActiveOptions => activeOptions ?? options;

    /// <summary>Character facts for anyone: vitals, casts, death, modes, emotes, job/level - scoped by <see cref="Scope"/>.</summary>
    public CharacterWatcher Characters { get; private set; } = null!;

    /// <summary>Kind-agnostic object facts: spawn/despawn/changed for anything, distance and region watchers.</summary>
    public ObjectWatcher Objects { get; private set; } = null!;

    /// <summary>Party and alliance facts: members, leader, roles, sizes, member territories.</summary>
    public PartyWatcher Party { get; private set; } = null!;

    /// <summary>Friend-list facts: online state and location - remote presence beyond the object table.</summary>
    public FriendWatcher Friends { get; private set; } = null!;

    /// <summary>Local player targeting facts: target, focus, soft and mouse-over targets.</summary>
    public TargetWatcher Targets { get; private set; } = null!;

    /// <summary>Zone facts: territory, map, instance, housing, weather and Eorzea time.</summary>
    public ZoneWatcher Zone { get; private set; } = null!;

    /// <summary>Duty facts: started/wiped/completed and queue tracking.</summary>
    public DutyWatcher Duty { get; private set; } = null!;

    /// <summary>Chat facts: messages with payloads preserved, rules, history.</summary>
    public ChatWatcher Chat { get; private set; } = null!;

    /// <summary>Combat facts: parsed action effects, scoped by source and target, rolling statistics.</summary>
    public CombatWatcher Combat { get; private set; } = null!;

    /// <summary>Cooldown facts: local recasts/charges/GCD (exact) and others' cooldowns (estimates).</summary>
    public CooldownWatcher Cooldowns { get; private set; } = null!;

    /// <summary>Status effect facts for any scoped character.</summary>
    public StatusWatcher Statuses { get; private set; } = null!;

    /// <summary>Addon facts: lifecycle, shown/hidden, node watchers.</summary>
    public AddonWatcher Addons { get; private set; } = null!;

    /// <summary>Inventory facts: granular item events, item counts, currency.</summary>
    public InventoryWatcher Inventory { get; private set; } = null!;

    /// <summary>Raw condition-flag facts and derived state pairs.</summary>
    public ConditionWatcher Conditions { get; private set; } = null!;

    /// <summary>Fate facts in the current zone.</summary>
    public FateWatcher Fates { get; private set; } = null!;

    /// <summary>Toast facts: normal, quest and error toasts.</summary>
    public ToastWatcher Toasts { get; private set; } = null!;

    /// <summary>Sets the options, restarting an active watcher.</summary>
    /// <param name="newOptions">The options.</param>
    /// <returns>This module.</returns>
    public NoireGameWatcher SetOptions(GameWatcherOptions newOptions)
    {
        ArgumentNullException.ThrowIfNull(newOptions);

        var wasActive = IsActive;

        if (wasActive)
            SetActive(false);

        options = newOptions.Clone();

        if (wasActive)
            SetActive(true);

        return this;
    }

    #endregion

    #region Module lifecycle

    /// <inheritdoc/>
    protected override void OnActivated()
    {
        activeOptions = options.Clone();

        LogSourceContradictions();

        if (NoireService.IsInitialized())
        {
            NoireService.Framework.Update += OnFrameworkUpdate;
            frameworkAttached = true;
        }

        lock (sourceGate)
        {
            foreach (var source in orderedSources)
                source.ResetFailure();
        }

        ReevaluateAllSources();

        if (EnableLogging)
            NoireLogger.LogDebug(this, "GameWatcher activated.");
    }

    /// <inheritdoc/>
    protected override void OnDeactivated()
    {
        if (frameworkAttached)
        {
            NoireService.Framework.Update -= OnFrameworkUpdate;
            frameworkAttached = false;
        }

        // Subscriptions survive: reactivation resumes them against a fresh baseline, with no change storm.
        lock (sourceGate)
        {
            foreach (var source in orderedSources)
                source.Deactivate();
        }

        if (EnableLogging)
            NoireLogger.LogDebug(this, "GameWatcher deactivated. Subscriptions are kept and resume on reactivation.");
    }

    /// <inheritdoc/>
    protected override void DisposeInternal()
    {
        SetActive(false);

        lock (gate)
        {
            foreach (var entry in ledger.ToArray())
                entry.OuterToken?.Invalidate();

            ledger.Clear();
            keyedEntries.Clear();
            ClearValueWatchers();
            ClearEventBusMirrors();
        }

        registry.ClearAll();

        foreach (var source in orderedSources)
            source.DisposeSource();

        if (EnableLogging)
            NoireLogger.LogDebug(this, "GameWatcher disposed.");
    }

    private void OnFrameworkUpdate(IFramework framework)
        => TickSources(DateTimeOffset.UtcNow);

    internal void TickSources(DateTimeOffset now)
    {
        // Sources run in declared order, events in detection order.
        foreach (var source in orderedSources)
            source.Tick(now);

        TickValueWatchers(now);
    }

    #endregion

    #region Demand-driven activation

    internal TimeSpan ResolvePollCadence(SourceKind kind, TimeSpan defaultCadence)
        => ActiveOptions.PollCadences.TryGetValue(kind, out var cadence) ? cadence : defaultCadence;

    internal void AddInterest(SourceKind kind)
    {
        lock (sourceGate)
            sources[kind].RefCount++;

        ReevaluateSource(kind);
    }

    internal void ReleaseInterest(SourceKind kind)
    {
        lock (sourceGate)
        {
            var source = sources[kind];
            source.RefCount = Math.Max(0, source.RefCount - 1);
        }

        ReevaluateSource(kind);
    }

    // Decision and toggle share one lock. In game, activation seeds baselines on the framework thread.
    internal void ReevaluateSource(SourceKind kind)
    {
        if (NoireService.IsInitialized() && !NoireService.Framework.IsInFrameworkUpdateThread)
        {
            MarshalReevaluation(kind);
            return;
        }

        lock (sourceGate)
        {
            var source = sources[kind];
            var desired = ComputeDesiredState(source);

            if (desired && !source.IsRunning)
            {
                if (NoireService.IsInitialized() || AllowSourceActivationWithoutGame)
                    source.Activate();
            }
            else if (!desired && source.IsRunning)
            {
                source.Deactivate();
            }
        }
    }

    // Apart: its closure is only allocated on this path.
    private void MarshalReevaluation(SourceKind kind)
        => _ = NoireService.Framework.RunOnFrameworkThread(() => ReevaluateSource(kind));

    private void ReevaluateAllSources()
    {
        foreach (var source in orderedSources)
            ReevaluateSource(source.Kind);
    }

    private bool ComputeDesiredState(GameWatcherSource source)
    {
        if (!IsActive || source.HasFailed)
            return false;

        var configured = GetSourceOverride(source.Kind);

        // Disabled beats everything, including the AlwaysOn implied by a configured history capacity.
        if (configured == SourceOverride.Disabled)
            return false;

        if (configured == SourceOverride.AlwaysOn || HasImpliedAlwaysOn(source.Kind))
            return true;

        return source.RefCount > 0;
    }

    private SourceOverride GetSourceOverride(SourceKind kind)
        => ActiveOptions.Sources.TryGetValue(kind, out var configured) ? configured : SourceOverride.Default;

    // A history only collects while its source runs.
    private bool HasImpliedAlwaysOn(SourceKind kind) => kind switch
    {
        SourceKind.Chat => ActiveOptions.Chat.HistoryCapacity > 0,
        SourceKind.ActionEffect => ActiveOptions.Combat.HistoryCapacity > 0,
        _ => false,
    };

    private void LogSourceContradictions()
    {
        foreach (var kind in new[] { SourceKind.Chat, SourceKind.ActionEffect })
        {
            if (GetSourceOverride(kind) == SourceOverride.Disabled && HasImpliedAlwaysOn(kind))
                NoireLogger.LogWarning(this, $"Source {kind} has a configured history capacity but is Disabled - Disabled wins; the history will stay empty.");
        }
    }

    // Not disabled does not mean running: the source may still have to start.
    internal bool IsSourceDisabled(SourceKind kind)
        => GetSourceOverride(kind) == SourceOverride.Disabled;

    internal TSource GetSource<TSource>(SourceKind kind) where TSource : GameWatcherSource
        => (TSource)sources[kind];

    internal IReadOnlyDictionary<SourceKind, GameWatcherSource> SourcesView => sources;

    // Test seam: lets a substituted source activate when no game is running.
    internal bool AllowSourceActivationWithoutGame { get; set; }

    // Test seam: call before anything subscribes.
    internal void ReplaceSource(GameWatcherSource source)
    {
        sources[source.Kind] = source;
        orderedSources[Array.FindIndex(orderedSources, existing => existing.Kind == source.Kind)] = source;
    }

    #endregion

    #region Thread guard

    // Queries read live game state. Hop with NoireService.Framework.RunOnFrameworkThread.
    internal static void EnsureFrameworkThread()
    {
        if (NoireService.IsInitialized() && !NoireService.Framework.IsInFrameworkUpdateThread)
            throw new InvalidOperationException(
                "GameWatcher queries read live game state and must be called from the framework thread. " +
                "Use NoireService.Framework.RunOnFrameworkThread(...) or run from an event handler / tick.");
    }

    #endregion
}
