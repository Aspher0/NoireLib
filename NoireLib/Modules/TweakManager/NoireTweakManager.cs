using NoireLib.Configuration;
using NoireLib.Core.Modules;
using NoireLib.EventBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NoireLib.TweakManager;

/// <summary>
/// A module that manages a collection of tweaks (small, toggleable features), automatically discovered from the
/// plugin assembly on initialization. Provides tweak registration, lifecycle management, typed persistent
/// configuration via <see cref="TweakConfigBase"/>, key migration support, a full management UI, and events via
/// <see cref="EventBus"/>.
/// </summary>
public class NoireTweakManager : NoireModuleWithWindowBase<NoireTweakManager, TweakManagerWindow, TweakManagerConfigInstance>
{
    private readonly Dictionary<string, TweakBase> tweaks = new();

    // Bookkeeping for operations that record several tweaks at once and want a single write.
    private readonly List<string> deferredSaveReportedKeys = new();
    private bool batchingConfigSaves;
    private bool deferredSavePending;

    /// <summary>
    /// Gets the <see cref="TweakManagerConfigInstance"/> used by this module; deliberately shadows the generated
    /// static accessor of the same name, whose members write the configuration file on every call, so this module
    /// can gate every save on <see cref="AutomaticPersistence"/> instead.
    /// </summary>
    private static TweakManagerConfigInstance TweakManagerConfig
        => NoireConfigManager.GetConfig<TweakManagerConfigInstance>()!;

    /// <summary>
    /// The associated EventBus for publishing tweak events; if null, no events are published.
    /// </summary>
    public NoireEventBus? EventBus { get; set; } = null;

    /// <summary>
    /// The default constructor needed for internal purposes.
    /// </summary>
    public NoireTweakManager() : base() { }

    /// <summary>
    /// Creates a new instance of the <see cref="NoireTweakManager"/> module.
    /// </summary>
    /// <param name="moduleId">The optional module identifier.</param>
    /// <param name="active">Whether the module should be active upon creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="automaticPersistence">Whether to automatically persist tweak configuration to disk.</param>
    /// <param name="additionalTweaks">Additional tweaks to register alongside auto-discovered ones.</param>
    /// <param name="eventBus">Optional EventBus instance to publish tweak events. If null, no events will be published.</param>
    public NoireTweakManager(
        string? moduleId = null,
        bool active = true,
        bool enableLogging = true,
        bool automaticPersistence = true,
        List<TweakBase>? additionalTweaks = null,
        NoireEventBus? eventBus = null)
            : base(moduleId, active, enableLogging, automaticPersistence, additionalTweaks, eventBus) { }

    /// <summary>
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>, for internal module management only.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    internal NoireTweakManager(ModuleId? moduleId, bool active = true, bool enableLogging = true)
        : base(moduleId, active, enableLogging) { }

    /// <summary>
    /// Initializes the module with optional initialization parameters.
    /// </summary>
    /// <param name="args">The initialization parameters.</param>
    protected override void InitializeModule(params object?[] args)
    {
        if (args.Length > 0 && args[0] is bool persistence)
            automaticPersistence = persistence;

        if (args.Length > 2 && args[2] is NoireEventBus eventBus)
            EventBus = eventBus;

        RegisterWindow(new TweakManagerWindow(this));

        var config = TweakManagerConfig;
        var migratedCount = config.ExecuteKeyMigrations();
        if (migratedCount > 0)
        {
            // A migration rewrites the store and spends the mapping that produced it; writing that out lets the
            // file settle, since a move left in memory is redone from the same old keys on every load.
            PersistConfigStore(null);
            PublishEvent(new TweakKeyMigrationsExecutedEvent(migratedCount));

            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Executed {migratedCount} tweak key migration(s).");
        }

        // NoireLib's own premade tweaks.
        LoadTweaksFromAssembly(typeof(TweakBase).Assembly);

        // The plugin's custom tweaks.
        LoadTweaksFromAssembly();

        if (args.Length > 1 && args[1] is List<TweakBase> tweakList)
        {
            foreach (var tweak in tweakList)
                RegisterTweakInternal(tweak, suppressEvent: true);
        }

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Tweak Manager initialized.");
    }

    /// <summary>
    /// Called when the module is activated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> false to true.
    /// </summary>
    protected override void OnActivated()
    {
        RunAsBatch(() =>
        {
            // Ensure globally locked tweaks are disabled in config and never loaded
            foreach (var tweak in tweaks.Values)
            {
                if (tweak.IsGloballyDisabled)
                {
                    var lockedConfig = TweakManagerConfig.GetTweakConfig(tweak.InternalKey);
                    if (lockedConfig is { Enabled: true })
                    {
                        TweakManagerConfig.SetTweakConfig(tweak.InternalKey,
                            new TweakConfigEntry(false, lockedConfig.ConfigJson, lockedConfig.ConfigVersion));

                        PersistConfigStore(null);

                        if (EnableLogging)
                            NoireLogger.LogInfo(this, $"Tweak '{tweak.Name}' ({tweak.InternalKey}) is globally locked. Disabled in config.");
                    }

                    continue;
                }

                // Restoring what the configuration already says never writes back to it.
                var config = TweakManagerConfig.GetTweakConfig(tweak.InternalKey);
                if (config is { Enabled: true } && !tweak.Enabled)
                    ApplyTweakEnable(tweak);
            }
        });

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Tweak Manager activated.");
    }

    /// <summary>
    /// Called when the module is deactivated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> true to false.
    /// </summary>
    protected override void OnDeactivated()
    {
        // Unhook every enabled tweak without recording it, so that the set the user chose is intact
        // when the module is activated again.
        foreach (var tweak in tweaks.Values.Where(t => t.Enabled).ToList())
            ApplyTweakDisable(tweak);

        if (ModuleWindow!.IsOpen)
            ModuleWindow.IsOpen = false;

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Tweak Manager deactivated.");
    }

    // Matches the public constructor's default, so that the constructors which take no arguments
    // (including the one NoireLibMain.AddModule uses) persist rather than silently discarding writes.
    private bool automaticPersistence = true;

    /// <summary>
    /// Whether tweak configuration is automatically persisted to disk. True saves tweak enabled states and
    /// configs via <see cref="TweakManagerConfigInstance"/> automatically; false applies enabling, disabling,
    /// favoriting, <see cref="TweakBase.MarkConfigDirty"/>, imports and key migrations in memory only, leaving
    /// <see cref="GetAllTweakConfigs"/> for manual persistence. This governs only the writes the module makes by
    /// itself: <see cref="SaveTweakConfig"/> and <see cref="SaveAllTweakConfigs"/> always write regardless, so
    /// turning it off controls timing rather than blocking writes outright.
    /// </summary>
    public bool AutomaticPersistence
    {
        get => automaticPersistence;
        set => automaticPersistence = value;
    }

    /// <summary>
    /// Sets whether tweak configuration should be automatically persisted.
    /// </summary>
    /// <param name="enabled">Whether to enable automatic persistence.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireTweakManager SetAutomaticPersistence(bool enabled)
    {
        AutomaticPersistence = enabled;
        return this;
    }

    #region Window Management

    /// <summary>
    /// Toggles the tweak manager window.
    /// </summary>
    /// <param name="show">Set to null to toggle the state, true to force show, false to force hide.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireTweakManager ShowWindow(bool? show = null)
    {
        if (!IsActive)
            return this;

        if (show == false || (show == null && ModuleWindow!.IsOpen))
            ModuleWindow!.CloseWindow();
        else
            ModuleWindow!.OpenWindow();

        return this;
    }

    /// <inheritdoc cref="NoireModuleWithWindowBase{TModule, TWindow}.ToggleWindow"/>
    public override NoireTweakManager ToggleWindow()
        => ShowWindow();

    /// <inheritdoc cref="ShowWindow(bool?)"/>
    public override NoireTweakManager ShowWindow()
        => ShowWindow(null);

    /// <summary>
    /// Internal method called by TweakManagerWindow when the window is opened.
    /// </summary>
    internal void OnWindowOpened()
    {
        PublishEvent(new TweakWindowOpenedEvent());
    }

    /// <summary>
    /// Internal method called by TweakManagerWindow when the window is closed.
    /// </summary>
    internal void OnWindowClosed()
    {
        PublishEvent(new TweakWindowClosedEvent());
    }

    /// <summary>
    /// Internal method called by TweakManagerWindow when a tweak is selected.
    /// </summary>
    /// <param name="tweak">The selected tweak.</param>
    internal void OnTweakSelected(TweakBase tweak)
    {
        PublishEvent(new TweakSelectedEvent(tweak.InternalKey, tweak.Name));
    }

    #endregion

    #region Tweak Registration

    /// <summary>
    /// Registers a custom tweak with the manager, for tweaks not automatically discovered from the plugin
    /// assembly (e.g. dynamically created or from external sources).
    /// </summary>
    /// <param name="tweak">The tweak to register.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireTweakManager RegisterTweak(TweakBase tweak)
    {
        RegisterTweakInternal(tweak, suppressEvent: false);
        return this;
    }

    /// <summary>
    /// Registers multiple custom tweaks with the manager.
    /// </summary>
    /// <param name="tweaksToRegister">The list of tweaks to register.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireTweakManager RegisterTweaks(List<TweakBase> tweaksToRegister)
    {
        foreach (var tweak in tweaksToRegister)
            RegisterTweakInternal(tweak, suppressEvent: false);
        return this;
    }

    /// <summary>
    /// Unregisters a tweak by its internal key, disposing it in the process.
    /// </summary>
    /// <param name="internalKey">The internal key of the tweak to unregister.</param>
    /// <returns><see langword="true"/> if the tweak was found and unregistered; otherwise, <see langword="false"/>.</returns>
    public bool UnregisterTweak(string internalKey)
    {
        if (!tweaks.TryGetValue(internalKey, out var tweak))
            return false;

        var name = tweak.Name;

        try
        {
            // Removing a tweak from the manager leaves its persisted entry alone, so registering it
            // again restores the state and the favorite the user had chosen for it.
            if (tweak.Enabled)
                ApplyTweakDisable(tweak);

            tweak.Dispose();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"Error disposing tweak '{name}' ({internalKey}) during unregistration.");
        }

        tweaks.Remove(internalKey);

        PublishEvent(new TweakUnregisteredEvent(internalKey, name));

        if (EnableLogging)
            NoireLogger.LogInfo(this, $"Tweak '{name}' ({internalKey}) unregistered.");

        return true;
    }

    /// <summary>
    /// Scans the plugin assembly for classes inheriting from <see cref="TweakBase"/> and registers them, called
    /// automatically during module initialization; tweaks decorated with <see cref="TweakDisabledAttribute"/> are
    /// detected and marked as globally disabled on the instance.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireTweakManager LoadTweaksFromAssembly()
    {
        try
        {
            if (!NoireService.IsInitialized())
            {
                if (EnableLogging)
                    NoireLogger.LogError(this, "NoireLib was not initialized. Cannot scan assembly for tweaks.");
                return this;
            }

            var assembly = NoireService.PluginInstance!.GetType().Assembly;
            LoadTweaksFromAssembly(assembly);
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "Failed to load tweaks from plugin assembly.");
        }

        return this;
    }

    /// <summary>
    /// Scans the specified assembly for classes inheriting from <see cref="TweakBase"/> and registers them.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireTweakManager LoadTweaksFromAssembly(Assembly assembly)
    {
        try
        {
            var tweakTypes = assembly.GetTypes()
                .Where(t => typeof(TweakBase).IsAssignableFrom(t) &&
                            !t.IsAbstract &&
                            !t.IsInterface)
                .ToList();

            foreach (var type in tweakTypes)
            {
                try
                {
                    if (Activator.CreateInstance(type) is TweakBase tweakInstance)
                        RegisterTweakInternal(tweakInstance, suppressEvent: false);
                }
                catch (Exception ex)
                {
                    if (EnableLogging)
                        NoireLogger.LogError(this, ex, $"Failed to instantiate tweak from type {type.Name}.");
                }
            }
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, $"Failed to load tweaks from assembly {assembly.GetName().Name}.");
        }

        return this;
    }

    private void RegisterTweakInternal(TweakBase tweak, bool suppressEvent)
    {
        if (tweak == null)
            return;

        if (tweaks.ContainsKey(tweak.InternalKey))
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, $"Tweak with key '{tweak.InternalKey}' is already registered. Skipping.");
            return;
        }

        tweak.Manager = this;
        tweaks[tweak.InternalKey] = tweak;

        // Populate globally disabled state from TweakDisabledAttribute
        var disabledAttr = tweak.GetType().GetCustomAttribute<TweakDisabledAttribute>();
        if (disabledAttr != null)
        {
            tweak.IsGloballyDisabled = true;
            tweak.GloballyDisabledReason = disabledAttr.Reason;
            tweak.ShowWhenDisabled = disabledAttr.ShowInList;

            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Tweak '{tweak.Name}' ({tweak.InternalKey}) is globally disabled{(disabledAttr.Reason != null ? $": {disabledAttr.Reason}" : ".")}" +
                    (disabledAttr.ShowInList ? " (visible in list)" : ""));
        }

        // Auto-migrate persisted data from old keys declared via TweakKeyMigrationAttribute
        var keyMigrationAttrs = tweak.GetType().GetCustomAttributes<TweakKeyMigrationAttribute>();
        foreach (var attr in keyMigrationAttrs)
        {
            if (!TweakManagerConfig.MigrateTweakKey(attr.OldKey, tweak.InternalKey))
                continue;

            PersistConfigStore(null);
            PublishEvent(new TweakKeyMigrationsExecutedEvent(1));

            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Auto-migrated persisted data from old key '{attr.OldKey}' to '{tweak.InternalKey}'.");
        }

        // Load config if available
        var config = TweakManagerConfig.GetTweakConfig(tweak.InternalKey);
        if (config != null)
        {
            try
            {
                tweak.DeserializeConfig(config.ConfigJson, config.ConfigVersion);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(this, ex, $"Error loading config into tweak '{tweak.Name}' ({tweak.InternalKey}).");
            }
        }

        if (!suppressEvent)
            PublishEvent(new TweakRegisteredEvent(tweak.InternalKey, tweak.Name));

        if (EnableLogging)
            NoireLogger.LogInfo(this, $"Tweak '{tweak.Name}' ({tweak.InternalKey}) registered.");
    }

    #endregion

    #region Tweak Enable/Disable

    /// <summary>
    /// Enables a tweak by its internal key.
    /// </summary>
    /// <param name="internalKey">The internal key of the tweak to enable.</param>
    /// <returns><see langword="true"/> if the tweak was enabled successfully; otherwise, <see langword="false"/>.</returns>
    public bool EnableTweak(string internalKey)
    {
        if (!tweaks.TryGetValue(internalKey, out var tweak))
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, $"Tweak with key '{internalKey}' not found.");
            return false;
        }

        if (tweak.IsGloballyDisabled)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, $"Tweak '{tweak.Name}' ({internalKey}) is globally disabled and cannot be enabled.");
            return false;
        }

        return EnableTweakInternal(tweak);
    }

    /// <summary>
    /// Enables a tweak by its type.
    /// </summary>
    /// <typeparam name="T">The tweak type to enable.</typeparam>
    /// <returns><see langword="true"/> if the tweak was found and enabled successfully; otherwise, <see langword="false"/>.</returns>
    public bool EnableTweak<T>() where T : TweakBase
    {
        var tweak = tweaks.Values.OfType<T>().FirstOrDefault();
        if (tweak == null)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, $"Tweak of type '{typeof(T).Name}' not found.");
            return false;
        }

        return EnableTweak(tweak.InternalKey);
    }

    /// <summary>
    /// Enables multiple tweaks by their internal keys.
    /// </summary>
    /// <param name="internalKeys">The internal keys of the tweaks to enable.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireTweakManager EnableTweaks(params string[] internalKeys)
    {
        RunAsBatch(() =>
        {
            foreach (var key in internalKeys)
                EnableTweak(key);
        });
        return this;
    }

    /// <summary>
    /// Disables a tweak by its internal key.
    /// </summary>
    /// <param name="internalKey">The internal key of the tweak to disable.</param>
    /// <returns><see langword="true"/> if the tweak was disabled successfully; otherwise, <see langword="false"/>.</returns>
    public bool DisableTweak(string internalKey)
    {
        if (!tweaks.TryGetValue(internalKey, out var tweak))
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, $"Tweak with key '{internalKey}' not found.");
            return false;
        }

        return DisableTweakInternal(tweak);
    }

    /// <summary>
    /// Disables a tweak by its type.
    /// </summary>
    /// <typeparam name="T">The tweak type to disable.</typeparam>
    /// <returns><see langword="true"/> if the tweak was found and disabled successfully; otherwise, <see langword="false"/>.</returns>
    public bool DisableTweak<T>() where T : TweakBase
    {
        var tweak = tweaks.Values.OfType<T>().FirstOrDefault();
        if (tweak == null)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, $"Tweak of type '{typeof(T).Name}' not found.");
            return false;
        }

        return DisableTweak(tweak.InternalKey);
    }

    /// <summary>
    /// Disables multiple tweaks by their internal keys.
    /// </summary>
    /// <param name="internalKeys">The internal keys of the tweaks to disable.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireTweakManager DisableTweaks(params string[] internalKeys)
    {
        RunAsBatch(() =>
        {
            foreach (var key in internalKeys)
                DisableTweak(key);
        });
        return this;
    }

    /// <summary>
    /// Toggles a tweak's enabled state by its internal key.
    /// </summary>
    /// <param name="internalKey">The internal key of the tweak to toggle.</param>
    /// <returns><see langword="true"/> if the operation succeeded; otherwise, <see langword="false"/>.</returns>
    public bool ToggleTweak(string internalKey)
    {
        if (!tweaks.TryGetValue(internalKey, out var tweak))
            return false;

        return tweak.Enabled ? DisableTweak(internalKey) : EnableTweak(internalKey);
    }

    /// <summary>
    /// Toggles a tweak's enabled state by its type.
    /// </summary>
    /// <typeparam name="T">The tweak type to toggle.</typeparam>
    /// <returns><see langword="true"/> if the operation succeeded; otherwise, <see langword="false"/>.</returns>
    public bool ToggleTweak<T>() where T : TweakBase
    {
        var tweak = tweaks.Values.OfType<T>().FirstOrDefault();
        if (tweak == null)
            return false;

        return ToggleTweak(tweak.InternalKey);
    }

    /// <summary>
    /// Applies a tweak's enabled effect and reports the outcome, without touching the configuration; restoring a
    /// tweak on activation uses this, since the configuration already says the tweak is on, and an enable that
    /// fails must not be mistaken for the user turning the tweak off.
    /// </summary>
    /// <param name="tweak">The tweak to hook up.</param>
    /// <returns><see langword="true"/> if the tweak was enabled successfully; otherwise, <see langword="false"/>.</returns>
    private bool ApplyTweakEnable(TweakBase tweak)
    {
        var success = tweak.Enable();

        if (success)
        {
            PublishEvent(new TweakEnabledEvent(tweak.InternalKey, tweak.Name));

            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Tweak '{tweak.Name}' ({tweak.InternalKey}) enabled.");
        }
        else
        {
            PublishEvent(new TweakErrorEvent(tweak.InternalKey, tweak.Name, tweak.LastError!));

            if (EnableLogging)
                NoireLogger.LogError(this, $"Failed to enable tweak '{tweak.Name}' ({tweak.InternalKey}).");
        }

        return success;
    }

    /// <summary>
    /// Applies a tweak's disabled effect and reports the outcome, without touching the configuration; teardown
    /// uses this, since unhooking a tweak because the module is going away or the tweak is being removed is not
    /// the user turning it off, and must not overwrite the enabled set the next activation reads back.
    /// </summary>
    /// <param name="tweak">The tweak to unhook.</param>
    /// <returns><see langword="true"/> if the tweak was disabled successfully; otherwise, <see langword="false"/>.</returns>
    private bool ApplyTweakDisable(TweakBase tweak)
    {
        var success = tweak.Disable();

        if (success)
        {
            PublishEvent(new TweakDisabledEvent(tweak.InternalKey, tweak.Name));

            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Tweak '{tweak.Name}' ({tweak.InternalKey}) disabled.");
        }
        else
        {
            PublishEvent(new TweakErrorEvent(tweak.InternalKey, tweak.Name, tweak.LastError!));

            if (EnableLogging)
                NoireLogger.LogError(this, $"Failed to disable tweak '{tweak.Name}' ({tweak.InternalKey}).");
        }

        return success;
    }

    /// <summary>
    /// Enables a tweak on the user's behalf, applying the effect and recording the intent.
    /// </summary>
    /// <param name="tweak">The tweak to enable.</param>
    /// <returns><see langword="true"/> if the tweak was enabled successfully; otherwise, <see langword="false"/>.</returns>
    private bool EnableTweakInternal(TweakBase tweak)
    {
        var success = ApplyTweakEnable(tweak);
        SaveTweakConfigInternal(tweak);
        return success;
    }

    /// <summary>
    /// Disables a tweak on the user's behalf, applying the effect and recording the intent.
    /// </summary>
    /// <param name="tweak">The tweak to disable.</param>
    /// <returns><see langword="true"/> if the tweak was disabled successfully; otherwise, <see langword="false"/>.</returns>
    private bool DisableTweakInternal(TweakBase tweak)
    {
        var success = ApplyTweakDisable(tweak);

        if (success)
            SaveTweakConfigInternal(tweak);

        return success;
    }

    #endregion

    #region Tweak Queries

    /// <summary>
    /// Retrieves a registered tweak by its internal key.
    /// </summary>
    /// <param name="internalKey">The internal key of the tweak.</param>
    /// <returns>The tweak if found; otherwise, <see langword="null"/>.</returns>
    public TweakBase? GetTweak(string internalKey)
    {
        return tweaks.GetValueOrDefault(internalKey);
    }

    /// <summary>
    /// Retrieves a registered tweak by its internal key, cast to the specified type.
    /// </summary>
    /// <typeparam name="T">The expected tweak type.</typeparam>
    /// <param name="internalKey">The internal key of the tweak.</param>
    /// <returns>The tweak cast to <typeparamref name="T"/> if found and matching; otherwise, <see langword="null"/>.</returns>
    public T? GetTweak<T>(string internalKey) where T : TweakBase
    {
        return tweaks.GetValueOrDefault(internalKey) as T;
    }

    /// <summary>
    /// Retrieves the first registered tweak of the specified type.
    /// </summary>
    /// <typeparam name="T">The tweak type to find.</typeparam>
    /// <returns>The tweak instance if found; otherwise, <see langword="null"/>.</returns>
    public T? GetTweak<T>() where T : TweakBase
    {
        return tweaks.Values.OfType<T>().FirstOrDefault();
    }

    /// <summary>
    /// Gets all registered tweaks.
    /// </summary>
    /// <returns>A read-only list of all registered tweaks.</returns>
    public IReadOnlyList<TweakBase> GetAllTweaks()
    {
        return tweaks.Values.ToList();
    }

    /// <summary>
    /// Gets all currently enabled tweaks.
    /// </summary>
    /// <returns>A read-only list of enabled tweaks.</returns>
    public IReadOnlyList<TweakBase> GetEnabledTweaks()
    {
        return tweaks.Values.Where(t => t.Enabled).ToList();
    }

    /// <summary>
    /// Gets all favorited tweaks that are currently registered.
    /// </summary>
    /// <returns>A read-only list of favorited tweaks.</returns>
    public IReadOnlyList<TweakBase> GetFavoriteTweaks()
    {
        return tweaks.Values
            .Where(t => TweakManagerConfig.IsFavorite(t.InternalKey))
            .ToList();
    }

    /// <summary>
    /// Determines whether a tweak is marked as a favorite.
    /// </summary>
    /// <param name="internalKey">The tweak internal key.</param>
    /// <returns><see langword="true"/> if the tweak is favorited; otherwise, <see langword="false"/>.</returns>
    public bool IsFavorite(string internalKey)
    {
        return TweakManagerConfig.IsFavorite(internalKey);
    }

    /// <summary>
    /// Sets the favorite state for a tweak; recorded on the same terms as enabling one, since starring is a user
    /// decision: written when <see cref="AutomaticPersistence"/> is on, collapsed into a running batch's single
    /// write when part of one.
    /// </summary>
    /// <param name="internalKey">The tweak internal key.</param>
    /// <param name="isFavorite">Whether the tweak should be favorited.</param>
    /// <returns><see langword="true"/> if the tweak exists and the favorite state was updated; otherwise, <see langword="false"/>.</returns>
    public bool SetFavorite(string internalKey, bool isFavorite)
    {
        if (!tweaks.ContainsKey(internalKey))
            return false;

        TweakManagerConfig.SetFavorite(internalKey, isFavorite);
        PersistConfigStore(null);

        return true;
    }

    /// <summary>
    /// Toggles the favorite state for a tweak.
    /// </summary>
    /// <param name="internalKey">The tweak internal key.</param>
    /// <returns><see langword="true"/> if the tweak exists and the favorite state was toggled; otherwise, <see langword="false"/>.</returns>
    public bool ToggleFavorite(string internalKey)
    {
        if (!tweaks.ContainsKey(internalKey))
            return false;

        var isFavorite = !TweakManagerConfig.IsFavorite(internalKey);
        return SetFavorite(internalKey, isFavorite);
    }

    /// <summary>
    /// Gets all tweaks that are in an error state.
    /// </summary>
    /// <returns>A read-only list of errored tweaks.</returns>
    public IReadOnlyList<TweakBase> GetErroredTweaks()
    {
        return tweaks.Values.Where(t => t.HasError).ToList();
    }

    #endregion

    #region Configuration Management

    /// <summary>
    /// Manually saves the configuration for a specific tweak by its internal key. This is a write the consumer
    /// asked for, so it happens whatever <see cref="AutomaticPersistence"/> says: turning that off controls
    /// writes the module makes on its own, not ones requested by name. A tweak reporting its own change instead
    /// goes through <see cref="TweakBase.MarkConfigDirty"/>, which does obey <see cref="AutomaticPersistence"/>.
    /// </summary>
    /// <param name="internalKey">The internal key of the tweak to save.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireTweakManager SaveTweakConfig(string internalKey)
    {
        if (tweaks.TryGetValue(internalKey, out var tweak))
            SaveTweakConfigInternal(tweak, explicitRequest: true);
        return this;
    }

    /// <summary>
    /// Manually saves the configuration for all registered tweaks, costing a single write however many tweaks
    /// there are; each tweak still announces its own <see cref="TweakConfigSavedEvent"/>. As with
    /// <see cref="SaveTweakConfig"/>, this is a write the consumer asked for and happens whatever
    /// <see cref="AutomaticPersistence"/> says.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireTweakManager SaveAllTweakConfigs()
    {
        RunAsBatch(() =>
        {
            foreach (var tweak in tweaks.Values)
                SaveTweakConfigInternal(tweak, explicitRequest: true);
        });
        return this;
    }

    /// <summary>
    /// Gets the configuration JSON for a specific tweak, as a read-only snapshot of the current config for
    /// display, export, or custom persistence logic.
    /// </summary>
    /// <param name="internalKey">The internal key of the tweak.</param>
    /// <returns>The JSON string if the tweak has config; otherwise, <see langword="null"/>.</returns>
    public string? GetTweakConfigAsJson(string internalKey)
    {
        if (!tweaks.TryGetValue(internalKey, out var tweak))
            return null;

        return tweak.SerializeConfig();
    }

    /// <summary>
    /// Gets a deserialized copy of a tweak's configuration; the returned instance is standalone, so modifying it
    /// does not affect the live tweak config.
    /// </summary>
    /// <typeparam name="TConfig">The tweak config type.</typeparam>
    /// <param name="internalKey">The internal key of the tweak.</param>
    /// <returns>A copy of the config if found and matching; otherwise, <see langword="null"/>.</returns>
    public TConfig? GetTweakConfigCopy<TConfig>(string internalKey) where TConfig : TweakConfigBase, new()
    {
        var json = GetTweakConfigAsJson(internalKey);
        if (json == null)
            return null;

        return TweakConfigBase.DeserializeFromJson<TConfig>(json, new TConfig().Version);
    }

    /// <summary>
    /// Gets all tweak configurations as a dictionary for manual persistence, for use when
    /// <see cref="AutomaticPersistence"/> is <see langword="false"/> and the consumer manages persistence themselves.
    /// </summary>
    /// <returns>A dictionary mapping tweak internal keys to their config entries.</returns>
    public Dictionary<string, TweakConfigEntry> GetAllTweakConfigs()
    {
        var configs = new Dictionary<string, TweakConfigEntry>(TweakManagerConfig.TweakConfigs);

        // Live tweaks are the authority on their own state, so the snapshot reflects them without
        // writing them back into the store that a caller only asked to read.
        foreach (var tweak in tweaks.Values)
            configs[tweak.InternalKey] = BuildTweakConfigEntry(tweak);

        return configs;
    }

    /// <summary>
    /// Imports tweak configurations from an external dictionary, for restoring configuration from a custom
    /// persistence source. Recorded on the module's usual terms: written when <see cref="AutomaticPersistence"/>
    /// is on, applied in memory only when it is off; use <see cref="SaveAllTweakConfigs"/> to write it explicitly.
    /// Whatever the number of entries, the import costs a single write.
    /// </summary>
    /// <param name="configs">The configuration dictionary to import.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireTweakManager ImportTweakConfigs(Dictionary<string, TweakConfigEntry> configs)
    {
        foreach (var (key, entry) in configs)
            TweakManagerConfig.SetTweakConfig(key, entry);

        LoadConfigIntoTweaks();

        PersistConfigStore(null);

        return this;
    }

    /// <summary>
    /// Registers a key migration mapping so persisted data is preserved when a tweak's InternalKey changes: the
    /// enabled state, the serialized config, and the user's favorite move together. Mappings are applied by
    /// <see cref="ExecuteKeyMigrations"/>, run automatically on initialization. Registering a mapping writes
    /// nothing by itself, since it is a declaration consumer code makes on every run rather than state the user
    /// built up; only the move it produces gets written.
    /// </summary>
    /// <param name="oldKey">The old internal key.</param>
    /// <param name="newKey">The new internal key.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireTweakManager AddKeyMigration(string oldKey, string newKey)
    {
        TweakManagerConfig.AddKeyMigration(oldKey, newKey);
        return this;
    }

    /// <summary>
    /// Registers multiple key migration mappings; as with <see cref="AddKeyMigration"/>, this writes nothing by
    /// itself, only the move it produces.
    /// </summary>
    /// <param name="migrations">Dictionary of old keys to new keys.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireTweakManager AddKeyMigrations(Dictionary<string, string> migrations)
    {
        foreach (var (oldKey, newKey) in migrations)
            TweakManagerConfig.AddKeyMigration(oldKey, newKey);
        return this;
    }

    /// <summary>
    /// Executes any pending key migrations now; a migration that moves something is written to disk when
    /// <see cref="AutomaticPersistence"/> is on, and a run that finds nothing to move writes nothing.
    /// </summary>
    /// <returns>The number of migrations executed.</returns>
    public int ExecuteKeyMigrations()
    {
        var count = TweakManagerConfig.ExecuteKeyMigrations();
        if (count > 0)
        {
            // A migration rewrites the store and spends the mapping that produced it; writing that out lets the
            // file settle, since a move left in memory is redone from the same old keys on every load.
            PersistConfigStore(null);
            PublishEvent(new TweakKeyMigrationsExecutedEvent(count));
            LoadConfigIntoTweaks();
        }
        return count;
    }

    /// <summary>
    /// Records a tweak's current state because the tweak reported that its configuration changed, via
    /// <see cref="TweakBase.MarkConfigDirty"/>; the module is recording a change it was told about rather than a
    /// write the consumer asked for, so it obeys <see cref="AutomaticPersistence"/>, unlike <see cref="SaveTweakConfig"/>.
    /// </summary>
    /// <param name="internalKey">The internal key of the tweak whose configuration changed.</param>
    internal void RecordTweakConfig(string internalKey)
    {
        if (tweaks.TryGetValue(internalKey, out var tweak))
            SaveTweakConfigInternal(tweak);
    }

    /// <summary>
    /// Records a tweak's current state in the configuration store and persists it.
    /// </summary>
    /// <param name="tweak">The tweak whose state should be recorded.</param>
    /// <param name="explicitRequest">Whether the consumer asked for this write, rather than the module making it on its own.</param>
    private void SaveTweakConfigInternal(TweakBase tweak, bool explicitRequest = false)
    {
        SaveTweakConfigToStore(tweak);
        PersistConfigStore(tweak.InternalKey, explicitRequest);
    }

    /// <summary>
    /// Writes the configuration store to disk, deciding both whether a write is allowed and when it happens;
    /// every write the module makes is requested here, so no path can escape either decision. A write the module
    /// makes on its own obeys <see cref="AutomaticPersistence"/>, while a write the consumer asked for by name
    /// (through <see cref="SaveTweakConfig"/> or <see cref="SaveAllTweakConfigs"/>) is carried out regardless.
    /// Inside a batch the write is deferred either way, so an operation covering many tweaks costs one write
    /// instead of one per tweak.
    /// </summary>
    /// <param name="reportedKey">The tweak key to announce with <see cref="TweakConfigSavedEvent"/>, or <see langword="null"/> to announce none.</param>
    /// <param name="explicitRequest">Whether the consumer asked for this write, rather than the module making it on its own.</param>
    private void PersistConfigStore(string? reportedKey, bool explicitRequest = false)
    {
        if (!explicitRequest && !automaticPersistence)
            return;

        if (batchingConfigSaves)
        {
            deferredSavePending = true;

            if (reportedKey != null)
                deferredSaveReportedKeys.Add(reportedKey);

            return;
        }

        TweakManagerConfig.Save();

        if (reportedKey != null)
            PublishEvent(new TweakConfigSavedEvent(reportedKey));
    }

    /// <summary>
    /// Runs an operation that records several tweaks, collapsing the writes it produces into a single one; every
    /// tweak still announces its own <see cref="TweakConfigSavedEvent"/> once the write lands. Batches nest: an
    /// operation already running inside one contributes to it rather than opening its own.
    /// </summary>
    /// <param name="operation">The operation to run.</param>
    internal void RunAsBatch(Action operation)
    {
        if (batchingConfigSaves)
        {
            operation();
            return;
        }

        batchingConfigSaves = true;

        try
        {
            operation();
        }
        finally
        {
            batchingConfigSaves = false;
            FlushDeferredConfigSaves();
        }
    }

    /// <summary>
    /// Performs the single write a batch accumulated, if any, and announces the tweaks it covered.
    /// </summary>
    private void FlushDeferredConfigSaves()
    {
        var reportedKeys = deferredSaveReportedKeys.ToList();
        var pending = deferredSavePending;

        deferredSaveReportedKeys.Clear();
        deferredSavePending = false;

        if (!pending)
            return;

        TweakManagerConfig.Save();

        foreach (var key in reportedKeys)
            PublishEvent(new TweakConfigSavedEvent(key));
    }

    private void SaveTweakConfigToStore(TweakBase tweak)
    {
        TweakManagerConfig.SetTweakConfig(tweak.InternalKey, BuildTweakConfigEntry(tweak));
    }

    /// <summary>
    /// Builds the configuration entry describing a tweak's current state.
    /// </summary>
    /// <param name="tweak">The tweak to describe.</param>
    /// <returns>The entry for the tweak.</returns>
    private TweakConfigEntry BuildTweakConfigEntry(TweakBase tweak)
    {
        string? configJson = null;
        int configVersion = 0;

        if (tweak.HasConfig)
        {
            try
            {
                configJson = tweak.SerializeConfig();
                var configInstance = tweak.GetConfigInstance();
                if (configInstance != null)
                    configVersion = configInstance.Version;
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(this, ex, $"Error serializing config from tweak '{tweak.Name}' ({tweak.InternalKey}).");
            }
        }

        return new TweakConfigEntry(tweak.Enabled, configJson, configVersion);
    }

    private void LoadConfigIntoTweaks()
    {
        foreach (var tweak in tweaks.Values)
        {
            var config = TweakManagerConfig.GetTweakConfig(tweak.InternalKey);
            if (config != null)
            {
                try
                {
                    tweak.DeserializeConfig(config.ConfigJson, config.ConfigVersion);
                }
                catch (Exception ex)
                {
                    NoireLogger.LogError(this, ex, $"Error loading config into tweak '{tweak.Name}' ({tweak.InternalKey}).");
                }
            }
        }
    }

    #endregion

    #region Bulk Operations

    /// <summary>
    /// Enables all registered tweaks that are eligible (not globally disabled).
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireTweakManager EnableAllTweaks()
    {
        RunAsBatch(() =>
        {
            foreach (var tweak in tweaks.Values)
            {
                if (!tweak.IsGloballyDisabled && !tweak.Enabled)
                    EnableTweakInternal(tweak);
            }
        });
        return this;
    }

    /// <summary>
    /// Disables all currently enabled tweaks.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireTweakManager DisableAllTweaks()
    {
        RunAsBatch(() =>
        {
            foreach (var tweak in tweaks.Values.Where(t => t.Enabled).ToList())
                DisableTweakInternal(tweak);
        });
        return this;
    }

    /// <summary>
    /// Clears all registered tweaks, disposing them in the process; persisted tweak configuration is left
    /// untouched, so the tweaks come back in the state the user chose if they are registered again.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireTweakManager ClearTweaks()
    {
        foreach (var tweak in tweaks.Values.ToList())
        {
            try
            {
                if (tweak.Enabled)
                    ApplyTweakDisable(tweak);
                tweak.Dispose();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(this, ex, $"Error disposing tweak '{tweak.Name}' ({tweak.InternalKey}) during clear.");
            }
        }

        tweaks.Clear();

        ModuleWindow?.ClearSelection();
        PublishEvent(new TweaksClearedEvent());

        if (EnableLogging)
            NoireLogger.LogInfo(this, "All tweaks cleared.");

        return this;
    }

    #endregion

    /// <summary>
    /// Internal dispose method called when the module is disposed.
    /// </summary>
    protected override void DisposeInternal()
    {
        foreach (var tweak in tweaks.Values.ToList())
        {
            try
            {
                tweak.Dispose();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(this, ex, $"Error disposing tweak '{tweak.Name}' ({tweak.InternalKey}).");
            }
        }

        tweaks.Clear();

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Tweak Manager disposed.");
    }

    #region EventBus Integration

    /// <summary>
    /// Publishes an event to the EventBus if available.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="eventData">The event data.</param>
    private void PublishEvent<TEvent>(TEvent eventData)
    {
        EventBus?.Publish(eventData);
    }

    #endregion
}
