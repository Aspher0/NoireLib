using Dalamud.Plugin.Services;
using NoireLib.Core.Modules;
using NoireLib.Core.Subscriptions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameWatcher;

/// <summary>
/// Watches <b>anything and anyone</b>: every character (local player and others), every object, party and
/// alliance, zones, duties, conditions, chat, combat, cooldowns, statuses, UI addons and inventory - through
/// one subscription model, one token type, one cost model, and one waiting primitive that plugs directly
/// into <c>NoireTaskQueue</c>.<br/><br/>
/// Subscribe through the domain facades (<see cref="Characters"/>, <see cref="Party"/>, <see cref="Zone"/>, …),
/// query current state through the same facades, and await game state with <see cref="GameCondition"/> /
/// <see cref="WaitFor{TEvent}"/>. Sources activate on demand: the first subscription touching a source spins
/// it up, disposing the last token shuts it down - there is nothing to enable manually.<br/><br/>
/// Every handler, filter, sampler and wait continuation runs <b>inline on the framework thread</b>.
/// <b>Never sync-block (<c>.Wait()</c> / <c>.Result</c>) on a watcher task from the framework thread - always await.</b>
/// </summary>
public partial class NoireGameWatcher : NoireModuleWithWindowBase<NoireGameWatcher, GameWatcherDiagnosticsWindow>
{
    private readonly object gate = new();

    private NoireSubscriptionRegistry<Type, object> registry = null!;
    private Dictionary<SourceKind, GameWatcherSource> sources = null!;
    private GameWatcherOptions options = new();
    private GameWatcherOptions? activeOptions;
    private bool frameworkAttached;

    /// <summary>
    /// The default constructor needed for internal purposes.<br/>
    /// Configure through <see cref="Options"/> before activating.
    /// </summary>
    public NoireGameWatcher() : base((string?)null, false, true) { }

    /// <summary>
    /// Creates a new game watcher.
    /// </summary>
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

    /// <summary>
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>.<br/>
    /// Only used for internal module management.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
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

        // The diagnostics window needs the NoireLib window system; skip it when running without the game
        // (unit tests). ShowDiagnostics() registers it lazily in that case.
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

    /// <summary>
    /// The options of this watcher. Changes made while the watcher is active require a restart
    /// (deactivate/activate) to apply.
    /// </summary>
    public GameWatcherOptions Options => options;

    /// <summary>The options snapshot in effect since the last activation.</summary>
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

    /// <summary>
    /// Sets the watcher options. When the watcher is active, it is restarted so the new options apply.
    /// </summary>
    /// <param name="newOptions">The new options.</param>
    /// <returns>The module instance for chaining.</returns>
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

        lock (gate)
        {
            foreach (var source in sources.Values)
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

        // Deactivating suspends all sources but keeps subscriptions: reactivation resumes them
        // against a fresh baseline (no synthetic change storm for state that moved while suspended).
        foreach (var source in sources.Values)
            source.Deactivate();

        if (EnableLogging)
            NoireLogger.LogDebug(this, "GameWatcher deactivated. Subscriptions are kept and resume on reactivation.");
    }

    /// <summary>
    /// Disposes the module completely. Overridden so a watcher created without the game (no window system,
    /// e.g. in tests) can still be disposed - the base unconditionally unregisters its window.
    /// </summary>
    public override void Dispose()
    {
        if (HasWindow)
        {
            base.Dispose();
            return;
        }

        DisposeInternal();
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
            valueWatchers.Clear();
            eventBusMirrors.Clear();
        }

        registry.ClearAll();

        foreach (var source in sources.Values)
            source.DisposeSource();

        if (EnableLogging)
            NoireLogger.LogDebug(this, "GameWatcher disposed.");
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = DateTimeOffset.UtcNow;

        GameWatcherSource[] snapshot;

        lock (gate)
            snapshot = sources.Values.ToArray();

        // Within one tick, sources run in declared order and events dispatch in detection order.
        foreach (var source in snapshot)
            source.Tick(now);

        TickValueWatchers(now);
    }

    #endregion

    #region Demand-driven activation

    /// <summary>
    /// Resolves the effective poll cadence for a source: the configured override, or the source default.
    /// </summary>
    internal TimeSpan ResolvePollCadence(SourceKind kind, TimeSpan defaultCadence)
        => ActiveOptions.PollCadences.TryGetValue(kind, out var cadence) ? cadence : defaultCadence;

    /// <summary>
    /// Registers interest in a source (refcount up) and starts it when it becomes needed.
    /// </summary>
    internal void AddInterest(SourceKind kind)
    {
        lock (gate)
            sources[kind].RefCount++;

        ReevaluateSource(kind);
    }

    /// <summary>
    /// Releases interest in a source (refcount down) and stops it when nothing needs it anymore.
    /// </summary>
    internal void ReleaseInterest(SourceKind kind)
    {
        lock (gate)
        {
            var source = sources[kind];
            source.RefCount = Math.Max(0, source.RefCount - 1);
        }

        ReevaluateSource(kind);
    }

    /// <summary>
    /// Starts or stops one source according to module state, refcount and configuration overrides.
    /// </summary>
    internal void ReevaluateSource(SourceKind kind)
    {
        var source = sources[kind];
        var desired = ComputeDesiredState(source);

        if (desired && !source.IsRunning)
        {
            if (NoireService.IsInitialized())
                source.Activate();
        }
        else if (!desired && source.IsRunning)
        {
            source.Deactivate();
        }
    }

    private void ReevaluateAllSources()
    {
        foreach (var kind in sources.Keys.ToArray())
            ReevaluateSource(kind);
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

    /// <summary>
    /// Histories only collect while their source runs, so a configured history capacity implies AlwaysOn -
    /// a capacity that silently collected nothing would be a footgun.
    /// </summary>
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

    /// <summary>
    /// Whether a source is currently running. Warns once per subscription when subscribing to a disabled source.
    /// </summary>
    internal bool IsSourceDisabled(SourceKind kind)
        => GetSourceOverride(kind) == SourceOverride.Disabled;

    /// <summary>Typed access to a source for facades. Internal.</summary>
    internal TSource GetSource<TSource>(SourceKind kind) where TSource : GameWatcherSource
        => (TSource)sources[kind];

    /// <summary>The sources table, for diagnostics.</summary>
    internal IReadOnlyDictionary<SourceKind, GameWatcherSource> SourcesView => sources;

    #endregion

    #region Thread guard

    /// <summary>
    /// Throws when called off the framework thread while the game is available. Queries always read live game
    /// state and must run on the framework thread; use <c>NoireService.Framework.RunOnFrameworkThread</c> to hop.
    /// </summary>
    internal static void EnsureFrameworkThread()
    {
        if (NoireService.IsInitialized() && !NoireService.Framework.IsInFrameworkUpdateThread)
            throw new InvalidOperationException(
                "GameWatcher queries read live game state and must be called from the framework thread. " +
                "Use NoireService.Framework.RunOnFrameworkThread(...) or run from an event handler / tick.");
    }

    #endregion
}
