using Newtonsoft.Json;
using NoireLib.Core.Modules;
using NoireLib.Core.Subscriptions;
using NoireLib.Helpers;
using System;

namespace NoireLib.ObservedStore;

/// <summary>
/// Remembers what the client was seen to hold, so a plugin can answer a question the game itself does not
/// proactively expose (a retainer's inventory, a housing interior's contents) with a timestamp and a source.
/// It does not decide what is worth remembering or when a sighting is too old to use; it records and reports
/// the age, and the plugin owns that policy.
/// </summary>
public partial class NoireObservedStore : NoireModuleBase<NoireObservedStore>
{
    /// <summary>The database file a store writes to when its options do not name one.</summary>
    public const string DefaultDatabaseName = "NoireObservedStore";

    private readonly object gate = new();

    private NoireSubscriptionRegistry<Type, object> registry = null!;
    private ObservedStoreOptions options = new();
    private ObservedStoreOptions? activeOptions;
    private JsonSerializerSettings? resolvedSerializerSettings;

    /// <summary>
    /// The default constructor needed for internal purposes; configure through <see cref="Options"/> before activating.
    /// </summary>
    public NoireObservedStore() : base((string?)null, false, true) { }

    /// <summary>
    /// Creates a new observed-state store.
    /// </summary>
    /// <param name="options">Optional settings; everything works with none.</param>
    /// <param name="moduleId">Optional module ID, for keeping several independent stores.</param>
    /// <param name="active">Whether to activate on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    public NoireObservedStore(
        ObservedStoreOptions? options,
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
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>, for internal module management only.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    internal NoireObservedStore(ModuleId? moduleId, bool active = true, bool enableLogging = true)
        : base(moduleId, active, enableLogging) { }

    /// <inheritdoc/>
    protected override void InitializeModule(params object?[] args)
    {
        registry = new((ex, description) => NoireLogger.LogError(this, ex, $"Unhandled exception in {description}."));
    }

    #region Public state

    /// <summary>
    /// The options of this store. Changes made while the store is active require a restart (deactivate/activate) to
    /// apply; <see cref="SetOptions"/> performs that cycle for you.
    /// </summary>
    public ObservedStoreOptions Options => options;

    /// <summary>The options snapshot in effect since the last activation.</summary>
    internal ObservedStoreOptions ActiveOptions => activeOptions ?? options;

    /// <summary>The database file this store is writing to.</summary>
    public string DatabaseName => ActiveOptions.DatabaseName;

    /// <summary>
    /// The content id of the logged-in character, or null when nobody is logged in. A character-scoped record or
    /// read has nothing to key on while this is null, so both answer "nothing" rather than writing under a
    /// placeholder.
    /// </summary>
    public ulong? CurrentCharacterId
    {
        get
        {
            // The content id comes out of the game's own player state, so it is gated the way every other read of
            // that data is: being logged in is not the same as the character being assembled.
            if (!CharacterHelper.IsStateReady)
                return null;

            var contentId = NoireService.PlayerState.ContentId;
            return contentId == 0 ? null : contentId;
        }
    }

    /// <summary>
    /// Observations about the logged-in character. Reads and records through this view answer nothing while nobody
    /// is logged in.
    /// </summary>
    public ObservationView Character => new(this, ObservationScope.Character, null);

    /// <summary>Observations that are the same whoever saw them.</summary>
    public ObservationView Shared => new(this, ObservationScope.Shared, 0);

    /// <summary>
    /// Observations about a specific character, named by content id. This is how an import writes down what it
    /// learned about a character other than the one logged in, and how a plugin reads one back later.
    /// </summary>
    /// <param name="characterId">The character's content id.</param>
    /// <returns>A view bound to that character.</returns>
    public ObservationView Of(ulong characterId) => new(this, ObservationScope.Character, characterId);

    /// <summary>
    /// The view the store's own methods use: <see cref="ObservedStoreOptions.DefaultScope"/>, and the logged-in
    /// character when that scope is <see cref="ObservationScope.Character"/>.
    /// </summary>
    public ObservationView Default => ActiveOptions.DefaultScope == ObservationScope.Shared ? Shared : Character;

    /// <summary>
    /// Sets the store options. When the store is active, it is restarted so the new options apply.
    /// </summary>
    /// <param name="newOptions">The new options.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireObservedStore SetOptions(ObservedStoreOptions newOptions)
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
        resolvedSerializerSettings = BuildSerializerSettings(activeOptions.SerializerSettings);

        if (string.IsNullOrWhiteSpace(activeOptions.DatabaseName))
        {
            NoireLogger.LogError(this, "ObservedStore activated with no database name; the store is inert.");
            IsActive = false;
            return;
        }

        if (activeOptions.PruneExpiredOnActivate)
        {
            var pruned = PruneExpired();

            if (pruned > 0 && EnableLogging)
                NoireLogger.LogDebug(this, $"Pruned {pruned} expired observation(s) on activation.");
        }

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"ObservedStore activated against database '{activeOptions.DatabaseName}'.");
    }

    /// <inheritdoc/>
    protected override void OnDeactivated()
    {
        // Subscriptions are kept: nothing about them depends on the database being open, and a deactivate/activate
        // cycle is how options are applied, which must not silently drop a consumer's handlers.
        if (EnableLogging)
            NoireLogger.LogDebug(this, "ObservedStore deactivated. Subscriptions are kept and resume on reactivation.");
    }

    /// <inheritdoc/>
    protected override void DisposeInternal()
    {
        SetActive(false);
        registry.ClearAll();

        if (EnableLogging)
            NoireLogger.LogDebug(this, "ObservedStore disposed.");
    }

    #endregion

    #region Internal plumbing

    /// <summary>
    /// Resolves the character a call is about, given whatever the caller named.
    /// </summary>
    /// <param name="scope">The scope in play.</param>
    /// <param name="requested">The character id the caller named, or null to take the logged-in one.</param>
    /// <param name="characterId">The resolved id.</param>
    /// <returns>False when a character-scoped call has no character to key on.</returns>
    internal bool TryResolveCharacter(ObservationScope scope, ulong? requested, out ulong characterId)
    {
        if (scope == ObservationScope.Shared)
        {
            characterId = 0;
            return true;
        }

        if (requested is { } named)
        {
            characterId = named;
            return named != 0;
        }

        if (CurrentCharacterId is { } current)
        {
            characterId = current;
            return true;
        }

        characterId = 0;
        return false;
    }

    /// <summary>
    /// Whether the store can be used right now, logging the reason once per call when it cannot.
    /// </summary>
    /// <param name="operation">The operation's name, for the log line.</param>
    /// <returns>True when the store is usable.</returns>
    internal bool IsUsable(string operation)
    {
        if (IsActive && !IsDisposed)
            return true;

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"{operation} ignored: the store is not active.");

        return false;
    }

    /// <summary>The serializer settings in force, with the type-name invariant applied.</summary>
    internal JsonSerializerSettings? SerializerSettings => resolvedSerializerSettings;

    /// <summary>
    /// Copies the caller's settings and forces the one field that must never be taken from configuration. A payload
    /// that names its own type would let anything able to write the database file choose which type gets constructed
    /// on read, so the store always deserializes into the type the caller asked for.
    /// </summary>
    /// <param name="requested">The caller's settings, or null.</param>
    /// <returns>The settings to serialize with, or null to use the Newtonsoft defaults.</returns>
    internal static JsonSerializerSettings? BuildSerializerSettings(JsonSerializerSettings? requested)
    {
        if (requested == null)
            return null;

        var settings = new JsonSerializerSettings
        {
            Formatting = requested.Formatting,
            NullValueHandling = requested.NullValueHandling,
            DefaultValueHandling = requested.DefaultValueHandling,
            MissingMemberHandling = requested.MissingMemberHandling,
            ReferenceLoopHandling = requested.ReferenceLoopHandling,
            DateTimeZoneHandling = requested.DateTimeZoneHandling,
            DateFormatHandling = requested.DateFormatHandling,
            FloatParseHandling = requested.FloatParseHandling,
            StringEscapeHandling = requested.StringEscapeHandling,
            ContractResolver = requested.ContractResolver,
            Culture = requested.Culture,
            MaxDepth = requested.MaxDepth,
            TypeNameHandling = TypeNameHandling.None,
        };

        foreach (var converter in requested.Converters)
            settings.Converters.Add(converter);

        return settings;
    }

    #endregion
}
