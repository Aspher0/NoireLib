using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using Lumina.Data;
using Newtonsoft.Json;
using NoireLib.Core.Modules;
using NoireLib.EventBus;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace NoireLib.Localizer;

/// <summary>
/// Localizes a plugin: locale fallback, runtime locale switching, fluent registration, JSON import and export, and
/// EventBus events.
/// </summary>
public partial class NoireLocalizer : NoireModuleBase<NoireLocalizer, LocalizerConfigInstance>
{
    #region Private Properties and Fields

    // Built with Create: no process-global DefaultSettings leak in. TypeNameHandling stays None: a file never names a type.
    private static readonly JsonSerializer TranslationSerializer = CreateTranslationSerializer();

    private static JsonSerializer CreateTranslationSerializer()
    {
        var serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
        });

        serializer.CheckAdditionalContent = true;
        return serializer;
    }

    private readonly object localizationLock = new();
    private readonly Dictionary<string, Dictionary<string, string>> translationsByLocale = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> fallbackLocalesByLocale = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> missingTranslationByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> localeDisplayNames = new(StringComparer.OrdinalIgnoreCase);

    // UI code looks a key up every frame. An order depends only on the locale settings, never on the translations.
    private readonly Dictionary<string, IReadOnlyList<string>> lookupOrderCache = new(StringComparer.OrdinalIgnoreCase);

    // Without the default-locale tail: declared texts fall back to their source text.
    private readonly Dictionary<string, IReadOnlyList<string>> declaredLookupOrderCache = new(StringComparer.OrdinalIgnoreCase);

    // A key missing from per-frame UI text is reported once per locale, not every frame.
    private readonly Dictionary<string, HashSet<string>> announcedMissingLocalesByKey = new(StringComparer.OrdinalIgnoreCase);

    private long totalTranslationsAdded;
    private long totalTranslationsUpdated;
    private long totalLocalesCreated;

    private string defaultLocale = "en-US";
    private string currentLocale = "en-US";
    private DefaultLocaleSource defaultLocaleSource = DefaultLocaleSource.Custom;

    #endregion

    #region Public Properties and Constructors

    /// <summary>The EventBus localization events are published to, or null for CLR events only.</summary>
    public NoireEventBus? EventBus { get; set; }

    /// <summary>The default constructor needed for internal purposes.</summary>
    public NoireLocalizer() : base() { }

    /// <summary>Creates a new instance of the <see cref="NoireLocalizer"/> module.</summary>
    /// <param name="moduleId">The optional module identifier.</param>
    /// <param name="active">Whether the module is active on creation.</param>
    /// <param name="enableLogging">Whether this module logs.</param>
    /// <param name="defaultLocale">
    /// The default locale the plugin declares. A persisted <see cref="DefaultLocaleSource"/> and a locale chosen in an
    /// earlier session both take precedence. It is never persisted.
    /// </param>
    /// <param name="currentLocale">The initial locale, <paramref name="defaultLocale"/> when null.</param>
    /// <param name="returnKeyWhenMissing">Whether a missing key returns the key itself.</param>
    /// <param name="allowParentCultureFallback">Whether parent cultures are tried.</param>
    /// <param name="allowDefaultLocaleFallback">Whether the default locale is tried.</param>
    /// <param name="defaultLocaleSource">How the default locale is resolved.</param>
    /// <param name="eventBus">The EventBus localization events are published to.</param>
    /// <param name="allowCustomLocales">Whether unknown locales are accepted as custom locales.</param>
    /// <param name="rejectOutdated">Whether outdated translations are set aside. See <see cref="RejectOutdated"/>.</param>
    /// <param name="rejectMissingOrExtraTags">Whether translations with wrong placeholders are set aside. See <see cref="RejectMissingOrExtraTags"/>.</param>
    public NoireLocalizer(
        string? moduleId = null,
        bool active = true,
        bool enableLogging = true,
        string defaultLocale = "en-US",
        string? currentLocale = null,
        bool returnKeyWhenMissing = false,
        bool allowParentCultureFallback = true,
        bool allowDefaultLocaleFallback = true,
        DefaultLocaleSource defaultLocaleSource = DefaultLocaleSource.Custom,
        bool allowCustomLocales = false,
        NoireEventBus? eventBus = null,
        bool rejectOutdated = false,
        bool rejectMissingOrExtraTags = false)
        : base(moduleId, active, enableLogging, defaultLocale, currentLocale, returnKeyWhenMissing, allowParentCultureFallback, allowDefaultLocaleFallback, defaultLocaleSource, allowCustomLocales, eventBus)
    {
        RejectOutdated = rejectOutdated;
        RejectMissingOrExtraTags = rejectMissingOrExtraTags;
    }

    internal NoireLocalizer(ModuleId? moduleId, bool active = true, bool enableLogging = true)
        : base(moduleId, active, enableLogging) { }

    #endregion

    #region Module Lifecycle Methods

    /// <summary>Initializes the module with optional initialization parameters.</summary>
    protected override void InitializeModule(params object?[] args)
    {
        if (args.Length > 0 && args[0] is string initDefaultLocale)
            DefaultLocale = initDefaultLocale;

        if (args.Length > 1 && args[1] is string initCurrentLocale)
            CurrentLocale = initCurrentLocale;
        else
            CurrentLocale = DefaultLocale;

        if (args.Length > 2 && args[2] is bool returnKeyWhenMissing)
            ReturnKeyWhenMissing = returnKeyWhenMissing;

        if (args.Length > 3 && args[3] is bool allowParentCultureFallback)
            AllowParentCultureFallback = allowParentCultureFallback;

        if (args.Length > 4 && args[4] is bool allowDefaultLocaleFallback)
            AllowDefaultLocaleFallback = allowDefaultLocaleFallback;

        if (args.Length > 5 && args[5] is DefaultLocaleSource initDefaultLocaleSource)
            DefaultLocaleSource = initDefaultLocaleSource;

        if (args.Length > 6 && args[6] is bool allowCustomLocales)
            AllowCustomLocales = allowCustomLocales;

        if (args.Length > 7 && args[7] is NoireEventBus eventBus)
            EventBus = eventBus;

        ApplyConfiguration();
        ApplyDefaultLocaleSource();

        EnsureLocale(DefaultLocale);
        EnsureLocale(CurrentLocale);
        AutoRegisterAttributedTranslations();
        StartDeclaredTexts();

        if (EnableLogging)
            NoireLogger.LogInfo(this, $"Localization module initialized. Default locale: '{DefaultLocale}', Current locale: '{CurrentLocale}'.");
    }

    /// <summary>
    /// Called when the module is activated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> false to true.
    /// </summary>
    protected override void OnActivated()
    {
        NoireLanguages.Bind(this);

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Localization module activated.");
    }

    /// <summary>
    /// Called when the module is deactivated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> true to false.
    /// </summary>
    protected override void OnDeactivated()
    {
        NoireLanguages.Unbind(this);

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Localization module deactivated.");
    }

    #endregion

    #region Module Configuration

    private bool allowParentCultureFallback = true;

    /// <summary>Whether to fallback to parent cultures during translation lookup (ex: fr-CA -> fr).</summary>
    public bool AllowParentCultureFallback
    {
        get => allowParentCultureFallback;
        set
        {
            if (allowParentCultureFallback == value)
                return;

            allowParentCultureFallback = value;
            InvalidateLookupOrderCache();
            NotifyTextsChanged();
        }
    }

    /// <summary>Sets whether parent locale fallback is allowed.</summary>
    /// <param name="enabled">True to enable parent locale fallback; otherwise, false.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer SetAllowParentCultureFallback(bool enabled)
    {
        AllowParentCultureFallback = enabled;
        return this;
    }

    private bool allowDefaultLocaleFallback = true;

    /// <summary>
    /// Whether to fallback to <see cref="DefaultLocale"/> when no translation is found in the requested locale chain.
    /// </summary>
    public bool AllowDefaultLocaleFallback
    {
        get => allowDefaultLocaleFallback;
        set
        {
            if (allowDefaultLocaleFallback == value)
                return;

            allowDefaultLocaleFallback = value;
            InvalidateLookupOrderCache();
        }
    }

    /// <summary>Sets whether fallback to default locale is allowed.</summary>
    /// <param name="enabled">True to enable default locale fallback; otherwise, false.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer SetAllowDefaultLocaleFallback(bool enabled)
    {
        AllowDefaultLocaleFallback = enabled;
        return this;
    }

    /// <summary>Whether unknown locales should be accepted as custom locales.</summary>
    public bool AllowCustomLocales { get; set; } = false;

    /// <summary>Sets whether unknown locales should be accepted as custom locales.</summary>
    /// <param name="enabled">True to allow unknown custom locales; otherwise, false.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer SetAllowCustomLocales(bool enabled)
    {
        AllowCustomLocales = enabled;
        return this;
    }

    /// <summary>Whether to return the key when a translation is missing.</summary>
    public bool ReturnKeyWhenMissing { get; set; } = false;

    /// <summary>Sets whether missing translations return their key.</summary>
    /// <param name="enabled">True to return the key when missing; otherwise, false.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer SetReturnKeyWhenMissing(bool enabled)
    {
        ReturnKeyWhenMissing = enabled;
        return this;
    }

    /// <summary>
    /// Whether missing keys should be automatically created in the default locale with the fallback text.
    /// </summary>
    public bool AutoCreateMissingKeysInDefaultLocale { get; set; } = false;

    /// <summary>Sets whether missing keys should be automatically created in the default locale.</summary>
    /// <param name="enabled">True to auto-create missing keys in default locale; otherwise, false.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer SetAutoCreateMissingKeysInDefaultLocale(bool enabled)
    {
        AutoCreateMissingKeysInDefaultLocale = enabled;
        return this;
    }

    /// <summary>The text of a missing translation when <see cref="ReturnKeyWhenMissing"/> is false. {0} is the key.</summary>
    public string MissingTranslationFormat { get; set; } = "[Missing: {0}]";

    /// <summary>Sets <see cref="MissingTranslationFormat"/>. The format must hold {0}.</summary>
    /// <param name="format">The format.</param>
    /// <returns>This module.</returns>
    public NoireLocalizer SetMissingTranslationFormat(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
            throw new ArgumentException("Missing translation format cannot be null or whitespace.", nameof(format));

        MissingTranslationFormat = format;
        return this;
    }

    /// <summary>
    /// The locale tried when the requested locale chain has no translation. Resolved at construction from the persisted
    /// <see cref="DefaultLocaleSource"/>, then an earlier selection, then the <c>defaultLocale</c> argument.
    /// </summary>
    public string DefaultLocale
    {
        get => defaultLocale;
        private set
        {
            var normalized = NormalizeLocaleOrThrow(value, nameof(DefaultLocale));

            if (string.Equals(defaultLocale, normalized, StringComparison.OrdinalIgnoreCase))
                return;

            defaultLocale = normalized;

            // Every lookup order ends with the default locale.
            InvalidateLookupOrderCache();
        }
    }

    /// <summary>
    /// Selects the default locale, switching <see cref="DefaultLocaleSource"/> to Custom. The choice is persisted and
    /// outranks the <c>defaultLocale</c> constructor argument in later sessions.
    /// </summary>
    /// <param name="locale">The default locale.</param>
    /// <returns>This module.</returns>
    public NoireLocalizer SetDefaultLocale(string locale)
    {
        var normalized = NormalizeLocaleOrThrow(locale, nameof(locale));
        EnsureLocale(normalized);
        DefaultLocale = normalized;
        DefaultLocaleSource = DefaultLocaleSource.Custom;
        PersistConfiguration(recordDefaultLocaleSelection: true);
        return this;
    }

    /// <summary>The currently active locale used by simple lookup helpers.</summary>
    public string CurrentLocale
    {
        get => currentLocale;
        private set => currentLocale = NormalizeLocaleOrThrow(value, nameof(CurrentLocale));
    }

    /// <summary>Sets the active locale used by simple lookup helpers.</summary>
    /// <param name="locale">The locale to activate.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer SetCurrentLocale(string locale)
    {
        var normalized = NormalizeLocaleOrThrow(locale, nameof(locale));
        EnsureLocale(normalized);

        var previous = CurrentLocale;
        CurrentLocale = normalized;

        if (!string.Equals(previous, normalized, StringComparison.OrdinalIgnoreCase))
        {
            NotifyTextsChanged();

            var evt = new LocalizationLocaleChangedEvent(previous, normalized);
            LocaleChanged?.Invoke(evt);
            PublishEvent(evt);
            PersistConfiguration();
        }

        return this;
    }

    /// <summary>Registers translations from an attributed provider class instance.</summary>
    /// <typeparam name="TProvider">The provider class type.</typeparam>
    /// <param name="provider">The provider instance.</param>
    /// <param name="overwrite">Whether existing translations should be overwritten.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer RegisterAttributedTranslations<TProvider>(TProvider provider, bool overwrite = true)
        where TProvider : class
    {
        if (provider == null)
            throw new ArgumentNullException(nameof(provider));

        return RegisterAttributedTranslations(typeof(TProvider), provider, overwrite);
    }

    /// <summary>Registers translations from an attributed provider class.</summary>
    /// <typeparam name="TProvider">The provider class type.</typeparam>
    /// <param name="overwrite">Whether existing translations should be overwritten.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer RegisterAttributedTranslations<TProvider>(bool overwrite = true)
        where TProvider : class
        => RegisterAttributedTranslations(typeof(TProvider), providerInstance: null, overwrite);

    /// <summary>Registers translations from an attributed provider class type.</summary>
    /// <param name="providerType">The attributed provider type.</param>
    /// <param name="providerInstance">Optional provider instance used for instance members.</param>
    /// <param name="overwrite">Whether existing translations should be overwritten.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer RegisterAttributedTranslations(Type providerType, object? providerInstance = null, bool overwrite = true)
    {
        if (providerType == null)
            throw new ArgumentNullException(nameof(providerType));

        if (!providerType.IsClass)
            throw new ArgumentException("Localization provider type must be a class.", nameof(providerType));

        var localeAttribute = providerType.GetCustomAttribute<NoireLocalizationLocaleAttribute>(inherit: true);
        var classLocale = localeAttribute?.Locale;

        RegisterMemberLevelAttributeEntries(providerType, ref providerInstance, classLocale, overwrite);

        return this;
    }

    /// <summary>Defines how the default locale should be resolved.</summary>
    public DefaultLocaleSource DefaultLocaleSource
    {
        get => defaultLocaleSource;
        private set => defaultLocaleSource = value;
    }

    /// <summary>Sets the default locale source strategy and applies it immediately.</summary>
    /// <param name="source">The default locale source strategy.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer SetDefaultLocaleSource(DefaultLocaleSource source)
    {
        DefaultLocaleSource = source;
        ApplyDefaultLocaleSource();
        PersistConfiguration();
        return this;
    }

    /// <summary>Selects a custom default locale, persisted as <see cref="SetDefaultLocale(string)"/> does.</summary>
    /// <param name="locale">The custom default locale.</param>
    /// <returns>This module.</returns>
    public NoireLocalizer UseCustomDefaultLocale(string locale)
    {
        return SetDefaultLocale(locale);
    }

    /// <summary>Configures the module to use the current Windows locale as default locale.</summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer UseWindowsLocaleAsDefaultLocale()
        => SetDefaultLocaleSource(DefaultLocaleSource.Windows);

    /// <summary>Configures the module to use the game client language as default locale.</summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer UseGameClientLocaleAsDefaultLocale()
        => SetDefaultLocaleSource(DefaultLocaleSource.GameClient);

    #endregion

    #region Public Events

    /// <summary>Event raised when the current locale changes.</summary>
    public event Action<LocalizationLocaleChangedEvent>? LocaleChanged;

    /// <summary>Event raised when a locale gets registered for the first time.</summary>
    public event Action<LocalizationLocaleRegisteredEvent>? LocaleRegistered;

    /// <summary>Event raised when a translation is added or updated.</summary>
    public event Action<LocalizationTranslationChangedEvent>? TranslationChanged;

    /// <summary>
    /// Raised the first time a lookup fails for a key and requested locale. Later misses are only counted, see
    /// <see cref="GetMissingTranslationCounts"/>. <see cref="ClearAllTranslations"/> resets the record.
    /// </summary>
    public event Action<LocalizationMissingTranslationEvent>? MissingTranslation;

    #endregion

    #region Public Methods

    /// <summary>Ensures a locale exists in the localization store.</summary>
    /// <param name="locale">The locale to ensure.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer EnsureLocale(string locale)
    {
        var normalized = NormalizeLocaleOrThrow(locale, nameof(locale));
        bool created = false;

        lock (localizationLock)
        {
            if (!translationsByLocale.ContainsKey(normalized))
            {
                translationsByLocale[normalized] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                totalLocalesCreated++;
                created = true;
            }

            if (!localeDisplayNames.ContainsKey(normalized))
                localeDisplayNames[normalized] = GetCultureDisplayName(normalized);
        }

        // Outside the lock: a handler may call back into the module.
        if (created)
        {
            var evt = new LocalizationLocaleRegisteredEvent(normalized);
            LocaleRegistered?.Invoke(evt);
            PublishEvent(evt);
        }

        return this;
    }

    /// <summary>Configures explicit fallback locales for a given locale.</summary>
    /// <param name="locale">The locale to configure.</param>
    /// <param name="fallbackLocales">The fallback locales to attempt after the requested locale.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer SetFallbackLocales(string locale, params string[] fallbackLocales)
    {
        var normalized = NormalizeLocaleOrThrow(locale, nameof(locale));
        var normalizedFallbacks = fallbackLocales
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => NormalizeLocaleOrThrow(x, nameof(fallbackLocales)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(x => !string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();

        EnsureLocale(normalized);
        foreach (var fallback in normalizedFallbacks)
            EnsureLocale(fallback);

        lock (localizationLock)
        {
            if (normalizedFallbacks.Count == 0)
                fallbackLocalesByLocale.Remove(normalized);
            else
                fallbackLocalesByLocale[normalized] = normalizedFallbacks;

            // Every cached order: another locale may reach this one through its own chain.
            ClearLookupOrdersLocked();
        }

        NotifyTextsChanged();
        return this;
    }

    /// <summary>Gets a fluent writer for a locale.</summary>
    /// <param name="locale">The locale to write translations for.</param>
    /// <returns>A fluent writer bound to <paramref name="locale"/>.</returns>
    public LocaleWriter ForLocale(string locale)
        => new(this, NormalizeLocaleOrThrow(locale, nameof(locale)));

    /// <summary>Sets a human-readable display name for a locale.</summary>
    /// <param name="locale">The locale key.</param>
    /// <param name="displayName">The locale display name to use in UI components.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer SetLocaleName(string locale, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Locale display name cannot be null or whitespace.", nameof(displayName));

        var normalized = NormalizeLocaleOrThrow(locale, nameof(locale));
        EnsureLocale(normalized);

        lock (localizationLock)
            localeDisplayNames[normalized] = displayName.Trim();

        NotifyTextsChanged();
        return this;
    }

    /// <summary>Sets multiple locale display names.</summary>
    /// <param name="localeNames">Locale key and display-name pairs.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer SetLocaleNames(IReadOnlyDictionary<string, string> localeNames)
    {
        if (localeNames == null)
            throw new ArgumentNullException(nameof(localeNames));

        foreach (var pair in localeNames)
            SetLocaleName(pair.Key, pair.Value);

        return this;
    }

    /// <summary>Gets the display name for a locale.</summary>
    /// <param name="locale">The locale key.</param>
    /// <returns>The configured display name, or a culture-derived name when none was manually configured.</returns>
    public string GetLocaleName(string locale)
    {
        var normalized = NormalizeLocaleOrThrow(locale, nameof(locale));

        lock (localizationLock)
        {
            if (localeDisplayNames.TryGetValue(normalized, out var customName))
                return customName;
        }

        return GetCultureDisplayName(normalized);
    }

    /// <summary>Adds or updates a single translation entry.</summary>
    public NoireLocalizer AddTranslation(string locale, string key, string value, bool overwrite = true)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Translation key cannot be null or whitespace.", nameof(key));

        var normalizedLocale = NormalizeLocaleOrThrow(locale, nameof(locale));
        EnsureLocale(normalizedLocale);

        var normalizedKey = key.Trim();
        bool existed;
        bool wasUpdated = false;

        lock (localizationLock)
        {
            var localeTranslations = translationsByLocale[normalizedLocale];
            existed = localeTranslations.ContainsKey(normalizedKey);

            if (!existed || overwrite)
            {
                localeTranslations[normalizedKey] = value ?? string.Empty;
                wasUpdated = existed;

                if (wasUpdated)
                    totalTranslationsUpdated++;
                else
                    totalTranslationsAdded++;
            }
        }

        if (!existed || wasUpdated)
        {
            NotifyTextsChanged();

            var evt = new LocalizationTranslationChangedEvent(normalizedLocale, normalizedKey, value ?? string.Empty, existed, overwrite);
            TranslationChanged?.Invoke(evt);
            PublishEvent(evt);
        }

        return this;
    }

    /// <summary>Adds or updates multiple translations for a locale.</summary>
    public NoireLocalizer AddTranslations(string locale, IReadOnlyDictionary<string, string> values, bool overwrite = true)
    {
        if (values == null)
            throw new ArgumentNullException(nameof(values));

        foreach (var entry in values)
            AddTranslation(locale, entry.Key, entry.Value, overwrite);

        return this;
    }

    /// <summary>Adds or updates multiple translations for a locale.</summary>
    public NoireLocalizer AddTranslations(string locale, params (string Key, string Value)[] values)
    {
        if (values == null)
            throw new ArgumentNullException(nameof(values));

        foreach (var (key, value) in values)
            AddTranslation(locale, key, value);

        return this;
    }

    /// <summary>Removes a translation key from all locales.</summary>
    public NoireLocalizer RemoveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return this;

        lock (localizationLock)
        {
            foreach (var localeTranslations in translationsByLocale.Values)
                localeTranslations.Remove(key.Trim());
        }

        NotifyTextsChanged();
        return this;
    }

    /// <summary>Clears all translations from a locale.</summary>
    public NoireLocalizer ClearLocale(string locale)
    {
        var normalized = NormalizeLocaleOrThrow(locale, nameof(locale));
        lock (localizationLock)
        {
            if (translationsByLocale.TryGetValue(normalized, out var localeTranslations))
                localeTranslations.Clear();
        }

        NotifyTextsChanged();
        return this;
    }

    /// <summary>Clears all locales and all translations.</summary>
    public NoireLocalizer ClearAllTranslations()
    {
        lock (localizationLock)
        {
            translationsByLocale.Clear();
            fallbackLocalesByLocale.Clear();
            missingTranslationByKey.Clear();

            ClearLookupOrdersLocked();

            // A key still missing afterwards is reported again.
            announcedMissingLocalesByKey.Clear();
        }

        EnsureLocale(DefaultLocale);
        EnsureLocale(CurrentLocale);
        NotifyTextsChanged();

        return this;
    }

    /// <summary>A translation in <see cref="CurrentLocale"/>, with indexed formatting.</summary>
    /// <param name="key">The translation key.</param>
    /// <param name="formatArgs">The values of the {0}, {1}, ... placeholders.</param>
    public string Get(string key, params object?[] formatArgs)
        => GetForLocale(CurrentLocale, key, formatArgs);

    /// <summary>A translation in a given locale, with indexed formatting.</summary>
    /// <param name="locale">The locale to look in.</param>
    /// <param name="key">The translation key.</param>
    /// <param name="formatArgs">The values of the {0}, {1}, ... placeholders.</param>
    public string GetForLocale(string locale, string key, params object?[] formatArgs)
    {
        var raw = GetRaw(locale, key);

        if (formatArgs == null || formatArgs.Length == 0)
            return raw;

        try
        {
            return string.Format(CultureInfo.InvariantCulture, raw, formatArgs);
        }
        catch (FormatException)
        {
            return raw;
        }
    }

    /// <summary>A translation with named tokens, written {TokenName} in the value.</summary>
    public string Get(string key, IReadOnlyDictionary<string, object?> namedArgs, string? locale = null)
    {
        if (namedArgs == null)
            throw new ArgumentNullException(nameof(namedArgs));

        var raw = GetRaw(locale ?? CurrentLocale, key);
        return ReplaceNamedTokens(raw, namedArgs);
    }

    /// <summary>Retrieves a translation without applying format arguments.</summary>
    public string GetRaw(string key)
        => GetRaw(CurrentLocale, key);

    /// <summary>Retrieves a translation in the specified locale without applying format arguments.</summary>
    public string GetRaw(string locale, string key)
    {
        var normalizedLocale = NormalizeLocaleOrThrow(locale, nameof(locale));

        if (string.IsNullOrWhiteSpace(key))
        {
            if (EnableLogging)
                NoireLogger.LogDebug(this, "Localization lookup requested with an empty key.");

            return string.Empty;
        }

        var normalizedKey = key.Trim();

        if (TryResolve(normalizedLocale, normalizedKey, out var value, out _, out var attemptedLocales))
            return value;

        return HandleMissingTranslation(normalizedLocale, normalizedKey, attemptedLocales);
    }

    /// <summary>
    /// Tries to retrieve a translation in <see cref="CurrentLocale"/> without fallback handling side effects.
    /// </summary>
    public bool TryGet(string key, out string value)
        => TryGet(CurrentLocale, key, out value);

    /// <summary>
    /// Tries to retrieve a translation in the specified locale without fallback handling side effects.
    /// </summary>
    public bool TryGet(string locale, string key, out string value)
    {
        value = string.Empty;

        if (string.IsNullOrWhiteSpace(key))
            return false;

        var normalizedLocale = NormalizeLocaleOrThrow(locale, nameof(locale));
        var normalizedKey = key.Trim();

        if (TryResolve(normalizedLocale, normalizedKey, out var foundValue, out _, out _))
        {
            value = foundValue;
            return true;
        }

        return false;
    }

    /// <summary>Gets all currently registered locales.</summary>
    public IReadOnlyList<string> GetLocales()
    {
        lock (localizationLock)
            return translationsByLocale.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Draws an ImGui combo box to switch between all available locales.</summary>
    /// <param name="label">The visible label for the combo box, or <see langword="null"/> for <see cref="NoireStrings.LanguageLabel"/>.</param>
    /// <param name="width">Optional combo width in pixels. Set to 0 to use automatic sizing.</param>
    /// <param name="showLocaleCode">Whether to append locale code to displayed options.</param>
    /// <returns>True if the locale was changed through the combo, otherwise false.</returns>
    public bool DrawLocaleCombo(string? label = null, float width = 220f, bool showLocaleCode = true)
    {
        label ??= NoireStrings.LanguageLabel;

        var locales = GetLocales();
        if (locales.Count == 0)
            return false;

        if (width > 0)
            ImGui.SetNextItemWidth(width);

        var currentDisplayName = GetLocaleName(CurrentLocale);
        var previewValue = showLocaleCode
            ? $"{currentDisplayName} ({CurrentLocale})"
            : currentDisplayName;

        var changed = false;
        if (ImGui.BeginCombo(label, previewValue))
        {
            foreach (var locale in locales.OrderBy(GetLocaleName, StringComparer.OrdinalIgnoreCase))
            {
                var localeDisplayName = GetLocaleName(locale);
                var itemLabel = showLocaleCode
                    ? $"{localeDisplayName} ({locale})"
                    : localeDisplayName;

                var isSelected = string.Equals(locale, CurrentLocale, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(itemLabel, isSelected))
                {
                    SetCurrentLocale(locale);
                    changed = true;
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    /// <summary>Gets all keys found across all locales.</summary>
    public IReadOnlyList<string> GetAllKeys()
    {
        lock (localizationLock)
        {
            return translationsByLocale
                .Values
                .SelectMany(x => x.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>Gets all translation key/value pairs for a specific locale.</summary>
    public IReadOnlyDictionary<string, string> GetLocaleTranslations(string locale)
    {
        var normalized = NormalizeLocaleOrThrow(locale, nameof(locale));
        lock (localizationLock)
        {
            if (!translationsByLocale.TryGetValue(normalized, out var localeTranslations))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            return new Dictionary<string, string>(localeTranslations, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Exports every translation as JSON: { "locale": { "key": "value" } }.</summary>
    public string ExportToJson(bool indented = true)
    {
        Dictionary<string, Dictionary<string, string>> snapshot;

        lock (localizationLock)
        {
            snapshot = translationsByLocale.ToDictionary(
                locale => locale.Key,
                locale => new Dictionary<string, string>(locale.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        }

        var builder = new StringBuilder(256);

        using (var stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture))
        using (var jsonWriter = new JsonTextWriter(stringWriter))
        {
            jsonWriter.Formatting = indented ? Formatting.Indented : Formatting.None;
            TranslationSerializer.Serialize(jsonWriter, snapshot);
        }

        return builder.ToString();
    }

    /// <summary>Exports every translation to a JSON file, creating missing directories.</summary>
    /// <param name="filePath">The file to write.</param>
    /// <param name="indented">Whether the JSON is indented.</param>
    /// <returns>This module.</returns>
    /// <exception cref="IOException">When the file could not be written.</exception>
    public NoireLocalizer ExportToJsonFile(string filePath, bool indented = true)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or whitespace.", nameof(filePath));

        // FileHelper reports failure instead of throwing.
        if (!FileHelper.WriteTextToFile(filePath, ExportToJson(indented)))
            throw new IOException($"Failed to write the localization file: {filePath}");

        return this;
    }

    /// <summary>Imports translations from JSON: { "locale": { "key": "value" } }.</summary>
    public NoireLocalizer ImportFromJson(string json, bool overwrite = true, bool clearExisting = false)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON content cannot be null or whitespace.", nameof(json));

        Dictionary<string, Dictionary<string, string>>? parsed;

        using (var stringReader = new StringReader(json))
        using (var jsonReader = new JsonTextReader(stringReader))
        {
            parsed = TranslationSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(jsonReader);
        }

        var payload = parsed
            ?? throw new InvalidDataException("Localization JSON payload is invalid or empty.");

        if (clearExisting)
            ClearAllTranslations();

        foreach (var localeEntry in payload)
            AddTranslations(localeEntry.Key, localeEntry.Value, overwrite);

        return this;
    }

    /// <summary>Imports translations from a JSON file.</summary>
    /// <param name="filePath">The full path of the file to read.</param>
    /// <param name="overwrite">Whether existing translations should be overwritten.</param>
    /// <param name="clearExisting">Whether all existing locales and translations should be cleared first.</param>
    /// <returns>The module instance for chaining.</returns>
    /// <exception cref="FileNotFoundException">Thrown when no file exists at <paramref name="filePath"/>.</exception>
    /// <exception cref="IOException">Thrown when the file exists but could not be read.</exception>
    public NoireLocalizer ImportFromJsonFile(string filePath, bool overwrite = true, bool clearExisting = false)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or whitespace.", nameof(filePath));

        if (!FileHelper.FileExists(filePath))
            throw new FileNotFoundException("Localization file not found.", filePath);

        // FileHelper reports a failed read as null. The file may have gone since it was checked.
        var content = FileHelper.ReadTextFromFile(filePath)
            ?? throw new IOException($"Failed to read the localization file: {filePath}");

        return ImportFromJson(content, overwrite, clearExisting);
    }

    /// <summary>Returns current localization statistics.</summary>
    public LocalizationStatistics GetStatistics()
    {
        lock (localizationLock)
        {
            var localeCount = translationsByLocale.Count;
            var translationCount = translationsByLocale.Values.Sum(x => x.Count);

            return new LocalizationStatistics(
                localeCount,
                translationCount,
                totalTranslationsAdded,
                totalTranslationsUpdated,
                totalLocalesCreated,
                new Dictionary<string, int>(missingTranslationByKey, StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>Returns a snapshot of missing translation counters by key.</summary>
    public IReadOnlyDictionary<string, int> GetMissingTranslationCounts()
    {
        lock (localizationLock)
            return new Dictionary<string, int>(missingTranslationByKey, StringComparer.OrdinalIgnoreCase);
    }

    #endregion

    #region Private Helper Methods

    private bool TryResolve(
        string locale,
        string key,
        out string value,
        out string? resolvedLocale,
        out IReadOnlyList<string> attemptedLocales)
    {
        lock (localizationLock)
        {
            attemptedLocales = GetLookupOrderLocked(locale);

            foreach (var candidateLocale in attemptedLocales)
            {
                if (translationsByLocale.TryGetValue(candidateLocale, out var localeTranslations)
                    && localeTranslations.TryGetValue(key, out var foundValue))
                {
                    resolvedLocale = candidateLocale;
                    value = foundValue;
                    return true;
                }
            }
        }

        resolvedLocale = null;
        value = string.Empty;
        return false;
    }

    private void RegisterMemberLevelAttributeEntries(Type providerType, ref object? providerInstance, string? classLocale, bool overwrite)
    {
        var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        foreach (var member in providerType.GetMembers(bindingFlags))
        {
            var attribute = member.GetCustomAttribute<NoireLocalizationAttribute>(inherit: true);
            if (attribute == null)
                continue;

            var key = NormalizeTranslationKeyOrThrow(attribute.Key, providerType, member.Name);
            var locale = ResolveAttributeLocaleOrThrow(attribute.Locale, classLocale, providerType, member.Name);

            var value = attribute.Value ?? ReadAttributedMemberValue(providerType, member, ref providerInstance);
            AddTranslation(locale, key, value, overwrite && attribute.Overwrite);
        }
    }

    private static string NormalizeTranslationKeyOrThrow(string key, Type providerType, string memberName)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException($"Translation key is missing on '{providerType.FullName}.{memberName}'.");

        return key.Trim();
    }

    private string ResolveAttributeLocaleOrThrow(string? attributeLocale, string? classLocale, Type providerType, string memberName)
    {
        var candidate = !string.IsNullOrWhiteSpace(attributeLocale)
            ? attributeLocale
            : classLocale;

        if (string.IsNullOrWhiteSpace(candidate))
            throw new InvalidOperationException($"No locale specified for '{providerType.FullName}.{memberName}'. Add [NoireLocalizationLocale] to the class or set Locale on the translation attribute.");

        return NormalizeLocaleOrThrow(candidate, nameof(attributeLocale));
    }

    private static string ReadAttributedMemberValue(Type providerType, MemberInfo member, ref object? providerInstance)
    {
        return member switch
        {
            PropertyInfo property => ReadAttributedPropertyValue(providerType, property, ref providerInstance),
            FieldInfo field => ReadAttributedFieldValue(providerType, field, ref providerInstance),
            _ => throw new InvalidOperationException($"Member '{providerType.FullName}.{member.Name}' must be a string field or property to be used with [NoireLocalization]."),
        };
    }

    private static string ReadAttributedPropertyValue(Type providerType, PropertyInfo property, ref object? providerInstance)
    {
        if (property.PropertyType != typeof(string))
            throw new InvalidOperationException($"Property '{providerType.FullName}.{property.Name}' must be of type string.");

        if (property.GetMethod == null)
            throw new InvalidOperationException($"Property '{providerType.FullName}.{property.Name}' must have a getter.");

        if (property.GetIndexParameters().Length != 0)
            throw new InvalidOperationException($"Indexed property '{providerType.FullName}.{property.Name}' is not supported for localization registration.");

        var target = property.GetMethod.IsStatic
            ? null
            : ResolveProviderInstance(providerType, ref providerInstance, property.Name);

        return property.GetValue(target) as string ?? string.Empty;
    }

    private static string ReadAttributedFieldValue(Type providerType, FieldInfo field, ref object? providerInstance)
    {
        if (field.FieldType != typeof(string))
            throw new InvalidOperationException($"Field '{providerType.FullName}.{field.Name}' must be of type string.");

        var target = field.IsStatic
            ? null
            : ResolveProviderInstance(providerType, ref providerInstance, field.Name);

        return field.GetValue(target) as string ?? string.Empty;
    }

    private static object ResolveProviderInstance(Type providerType, ref object? providerInstance, string memberName)
    {
        if (providerInstance != null)
            return providerInstance;

        if (providerType.GetConstructor(Type.EmptyTypes) == null)
            throw new InvalidOperationException($"No instance was provided for '{providerType.FullName}.{memberName}', and '{providerType.FullName}' has no parameterless constructor.");

        providerInstance = Activator.CreateInstance(providerType)
            ?? throw new InvalidOperationException($"Unable to create an instance of '{providerType.FullName}' for localization registration.");

        return providerInstance;
    }

    private void AutoRegisterAttributedTranslations()
    {
        var pluginAssembly = NoireService.PluginInstance?.GetType().Assembly;
        if (pluginAssembly == null)
            return;

        var providerTypes = DiscoverAutoRegisterLocalizationProviderTypes(pluginAssembly);
        foreach (var providerType in providerTypes)
            RegisterAttributedTranslations(providerType, providerInstance: null, overwrite: true);

        if (EnableLogging && providerTypes.Count > 0)
            NoireLogger.LogDebug(this, $"Auto-registered {providerTypes.Count} localization provider class(es) from assembly '{pluginAssembly.GetName().Name}'.");
    }

    private static IReadOnlyList<Type> DiscoverAutoRegisterLocalizationProviderTypes(Assembly pluginAssembly)
    {
        var assemblyTypes = GetLoadableTypes(pluginAssembly);

        const BindingFlags memberFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        return assemblyTypes
            .Where(type => type.IsClass && !type.IsAbstract)
            .Where(type => type.GetCustomAttribute<NoireLocalizationLocaleAttribute>(inherit: true) is { RegisterAutomatically: true })
            .Where(type => type.GetMembers(memberFlags).Any(member => member.GetCustomAttribute<NoireLocalizationAttribute>(inherit: true) != null))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null).Cast<Type>().ToArray();
        }
    }

    // Called by every path that changes the fallback chains, DefaultLocale or the two fallback toggles.
    private void InvalidateLookupOrderCache()
    {
        lock (localizationLock)
            ClearLookupOrdersLocked();
    }

    // Caller holds localizationLock.
    private void ClearLookupOrdersLocked()
    {
        lookupOrderCache.Clear();
        declaredLookupOrderCache.Clear();
    }

    // Caller holds localizationLock.
    private IReadOnlyList<string> GetLookupOrderLocked(string locale)
    {
        if (lookupOrderCache.TryGetValue(locale, out var cachedOrder))
            return cachedOrder;

        var order = BuildLookupOrderLocked(locale, includeDefaultLocale: true);
        lookupOrderCache[locale] = order;
        return order;
    }

    // Caller holds localizationLock.
    private IReadOnlyList<string> GetDeclaredLookupOrderLocked(string locale)
    {
        if (declaredLookupOrderCache.TryGetValue(locale, out var cachedOrder))
            return cachedOrder;

        var order = BuildLookupOrderLocked(locale, includeDefaultLocale: false);
        declaredLookupOrderCache[locale] = order;
        return order;
    }

    // Caller holds localizationLock: the fallback chains are read in place.
    private IReadOnlyList<string> BuildLookupOrderLocked(string locale, bool includeDefaultLocale)
    {
        var currentDefaultLocale = DefaultLocale;
        var order = new List<string>();
        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        queue.Enqueue(locale);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current))
                continue;

            order.Add(current);

            if (AllowParentCultureFallback)
            {
                var parent = GetParentLocale(current);
                if (parent != null)
                    queue.Enqueue(parent);
            }

            if (fallbackLocalesByLocale.TryGetValue(current, out var explicitFallbacks))
            {
                foreach (var fallbackLocale in explicitFallbacks)
                    queue.Enqueue(fallbackLocale);
            }
        }

        if (!includeDefaultLocale)
            return order;

        if (AllowDefaultLocaleFallback && visited.Add(currentDefaultLocale))
            order.Add(currentDefaultLocale);

        if (AllowDefaultLocaleFallback && AllowParentCultureFallback)
        {
            var parent = GetParentLocale(currentDefaultLocale);
            while (parent != null)
            {
                if (visited.Add(parent))
                    order.Add(parent);

                parent = GetParentLocale(parent);
            }
        }

        return order;
    }

    private string HandleMissingTranslation(string locale, string key, IReadOnlyList<string> attemptedLocales)
    {
        bool isFirstMiss;

        lock (localizationLock)
        {
            // Counted on every miss: the totals stay exact though the event fires once.
            missingTranslationByKey[key] = missingTranslationByKey.GetValueOrDefault(key) + 1;

            if (!announcedMissingLocalesByKey.TryGetValue(key, out var announcedLocales))
            {
                announcedLocales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                announcedMissingLocalesByKey[key] = announcedLocales;
            }

            isFirstMiss = announcedLocales.Add(locale);
        }

        if (AutoCreateMissingKeysInDefaultLocale)
            AddTranslation(DefaultLocale, key, key, overwrite: false);

        if (isFirstMiss)
        {
            var evt = new LocalizationMissingTranslationEvent(locale, key, attemptedLocales.ToList());
            MissingTranslation?.Invoke(evt);
            PublishEvent(evt);

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Missing translation for key '{key}' in locale '{locale}'.");
        }

        return ReturnKeyWhenMissing
            ? key
            : string.Format(CultureInfo.InvariantCulture, MissingTranslationFormat, key);
    }

    private static string ReplaceNamedTokens(string template, IReadOnlyDictionary<string, object?> namedArgs)
    {
        return Regex.Replace(template, @"\{([A-Za-z0-9_.\-]+)\}", match =>
        {
            var tokenName = match.Groups[1].Value;
            return namedArgs.TryGetValue(tokenName, out var replacement)
                ? replacement?.ToString() ?? string.Empty
                : match.Value;
        });
    }

    private static string? GetParentLocale(string locale)
    {
        var separatorIndex = locale.LastIndexOf('-');
        return separatorIndex > 0 ? locale[..separatorIndex] : null;
    }

    private string NormalizeLocaleOrThrow(string locale, string paramName)
    {
        if (string.IsNullOrWhiteSpace(locale))
            throw new ArgumentException("Locale cannot be null or whitespace.", paramName);

        var normalized = locale.Trim().Replace('_', '-');

        if (string.Equals(normalized, NoireLanguages.Pseudo, StringComparison.OrdinalIgnoreCase))
            return NoireLanguages.Pseudo;

        if (string.Equals(normalized, NoireLanguages.PseudoLong, StringComparison.OrdinalIgnoreCase))
            return NoireLanguages.PseudoLong;

        try
        {
            return CultureInfo.GetCultureInfo(normalized).Name;
        }
        catch (CultureNotFoundException) when (AllowCustomLocales)
        {
            return normalized.ToLowerInvariant();
        }
    }

    private void PublishEvent<TEvent>(TEvent eventData)
    {
        EventBus?.Publish(eventData);
    }

    private void ApplyConfiguration()
    {
        DefaultLocaleSource = LocalizerConfig.DefaultLocaleSource;

        // Only a stored selection. CustomDefaultLocale is populated as soon as a configuration exists.
        if (LocalizerConfig.HasCustomDefaultLocaleSelection && !string.IsNullOrWhiteSpace(LocalizerConfig.CustomDefaultLocale))
            DefaultLocale = NormalizeLocaleOrThrow(LocalizerConfig.CustomDefaultLocale, nameof(LocalizerConfig.CustomDefaultLocale));

        if (!string.IsNullOrWhiteSpace(LocalizerConfig.SelectedLocale))
            CurrentLocale = NormalizeLocaleOrThrow(LocalizerConfig.SelectedLocale, nameof(LocalizerConfig.SelectedLocale));
    }

    private void PersistConfiguration(bool recordDefaultLocaleSelection = false)
    {
        // Through the instance: the static [AutoSave] accessor would write the file once per value.
        var config = LocalizerConfig.Instance;

        config.SelectedLocale = CurrentLocale;
        config.DefaultLocaleSource = DefaultLocaleSource;

        // Only an explicit selection is recorded, never the locale a source last resolved to.
        if (recordDefaultLocaleSelection)
        {
            config.CustomDefaultLocale = DefaultLocale;
            config.HasCustomDefaultLocaleSelection = true;
        }

        LocalizerConfig.Save();
    }

    private void ApplyDefaultLocaleSource()
    {
        var resolvedLocale = ResolveLocaleFromSource(DefaultLocaleSource);
        if (!string.IsNullOrWhiteSpace(resolvedLocale))
        {
            DefaultLocale = resolvedLocale;
            EnsureLocale(DefaultLocale);
        }
    }

    private string ResolveLocaleFromSource(DefaultLocaleSource source)
    {
        return source switch
        {
            DefaultLocaleSource.Windows => NormalizeLocaleOrThrow(CultureInfo.CurrentUICulture.Name, nameof(CultureInfo.CurrentUICulture)),
            DefaultLocaleSource.GameClient => ResolveGameClientLocale(),
            _ => DefaultLocale,
        };
    }

    private string ResolveGameClientLocale()
    {
        if (NoireService.ClientState is not { } clientState)
            return DefaultLocale;

        // Korean and Chinese exist only in the regional Dalamud builds, whose ToLumina knows them.
        var language = SafeExecutor.ExecuteSafely(() => clientState.ClientLanguage.ToLumina(), Language.None);

        return language switch
        {
            Language.English => "en-US",
            Language.French => "fr-FR",
            Language.German => "de-DE",
            Language.Japanese => "ja-JP",
            Language.Korean => "ko-KR",
            Language.ChineseSimplified => "zh-CN",
            Language.ChineseTraditional or Language.TraditionalChinese => "zh-TW",
            _ => DefaultLocale,
        };
    }

    private static string GetCultureDisplayName(string locale)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(locale);
            return culture.NativeName;
        }
        catch (CultureNotFoundException)
        {
            return locale;
        }
    }

    #endregion

    /// <summary>Disposes the module resources.</summary>
    protected override void DisposeInternal()
    {
        StopDeclaredTexts();

        lock (localizationLock)
        {
            translationsByLocale.Clear();
            fallbackLocalesByLocale.Clear();
            missingTranslationByKey.Clear();
            ClearLookupOrdersLocked();
            announcedMissingLocalesByKey.Clear();
        }

        LocaleChanged = null;
        LocaleRegistered = null;
        TranslationChanged = null;
        MissingTranslation = null;

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Localization module disposed.");
    }
}
