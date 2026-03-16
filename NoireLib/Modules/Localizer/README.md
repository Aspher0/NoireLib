# Module Documentation : NoireLocalizer

You are reading the documentation for the `NoireLocalizer` module.

## Table of Contents

- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
  - [Constructor Parameters](#constructor-parameters)
  - [Default Locale Source](#default-locale-source)
  - [Fallback Behavior](#fallback-behavior)
  - [Missing Translation Behavior](#missing-translation-behavior)
  - [Custom Locales](#custom-locales)
  - [Persistent Configuration](#persistent-configuration)
- [Registering Translations](#registering-translations)
  - [Single Translation](#single-translation)
  - [Bulk Registration](#bulk-registration)
  - [Tuple Registration](#tuple-registration)
  - [Fluent Locale Writer](#fluent-locale-writer)
  - [Attribute-Based Registration](#attribute-based-registration)
  - [Automatic Discovery](#automatic-discovery)
- [Retrieving Translations](#retrieving-translations)
  - [Basic Lookup](#basic-lookup)
  - [Locale-Specific Lookup](#locale-specific-lookup)
  - [Indexed Format Arguments](#indexed-format-arguments)
  - [Named Token Replacement](#named-token-replacement)
  - [Raw Lookup (No Formatting)](#raw-lookup-no-formatting)
  - [Safe Try Pattern](#safe-try-pattern)
- [Locale Management](#locale-management)
  - [Ensuring a Locale Exists](#ensuring-a-locale-exists)
  - [Switching Locale at Runtime](#switching-locale-at-runtime)
  - [Locale Display Names](#locale-display-names)
  - [Listing Locales and Keys](#listing-locales-and-keys)
  - [Clearing Translations](#clearing-translations)
  - [Removing a Key](#removing-a-key)
- [Fallback Resolution Order](#fallback-resolution-order)
- [Events](#events)
  - [CLR Events](#clr-events)
  - [EventBus Integration](#eventbus-integration)
  - [Event Types Reference](#event-types-reference)
- [ImGui Locale Combo](#imgui-locale-combo)
- [JSON Import and Export](#json-import-and-export)
- [Statistics and Monitoring](#statistics-and-monitoring)
- [Attribute Reference](#attribute-reference)
  - [NoireLocalizationLocaleAttribute](#noirelocalizationlocaleattribute)
  - [NoireLocalizationAttribute](#noirelocalizationattribute)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

`NoireLocalizer` is a complete localization module for plugin text and language workflows.
It is designed to be flexible, safe, and easy to integrate into any plugin.

**Features at a glance:**

- **Locale-based translation storage** with thread-safe runtime lookup.
- **Multi-level fallback chains** — requested locale → parent culture → explicit fallbacks → default locale.
- **Runtime locale switching** with CLR events and optional `NoireEventBus` integration.
- **Fluent translation registration** via `ForLocale()` / `LocaleWriter` for clean setup code.
- **Attribute-based registration** with automatic discovery from the plugin assembly.
- **JSON import/export** to and from strings or files.
- **Built-in ImGui combo box** for user-facing locale selection.
- **Persistent configuration** — selected locale and default-locale strategy are saved to disk automatically.
- **Missing-translation tracking** with configurable behavior and statistics.

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Register Translations

```csharp
// Option A — attribute-based (auto-registered at startup)
[NoireLocalizationLocale("en-US", RegisterAutomatically = true)]
public class EnglishTranslations
{
    [NoireLocalization("UI.Hello")]
    public static string Hello => "Hello";
}

// Option B — fluent API
localizer
    .ForLocale("en-US")
        .Add("UI.Hello", "Hello")
    .Done()
    .ForLocale("fr-FR")
        .Add("UI.Hello", "Bonjour")
    .Done()
    .ForLocale("de-DE")
        .Add("UI.Hello", "Hallo")
    .Done();

// Option C — direct registration
localizer
    .AddTranslation("en-US", "UI.Hello", "Hello")
    .AddTranslation("fr-FR", "UI.Hello", "Bonjour")
    .AddTranslation("de-DE", "UI.Hello", "Hallo");
```

### 2. Read Localized Values

```csharp
localizer.SetCurrentLocale("fr-FR");

var text = localizer.Get("UI.Hello");
// text == "Bonjour"
```

That's it! You now have a working localization flow.

---

## Configuration

### Constructor Parameters

```csharp
var localizer = new NoireLocalizer(
    moduleId: "MyLocalizer",           // Optional module identifier
    active: true,                      // Whether the module starts active
    enableLogging: true,               // Log lifecycle and missing-key events
    defaultLocale: "en-US",            // Fallback locale
    currentLocale: "en-US",            // Initial active locale (defaults to defaultLocale)
    returnKeyWhenMissing: false,       // Return the key itself when no translation is found
    allowParentCultureFallback: true,  // Try parent cultures (e.g. fr-CA → fr)
    allowDefaultLocaleFallback: true,  // Fall back to the default locale as last resort
    defaultLocaleSource: DefaultLocaleSource.Custom,
    allowCustomLocales: false,         // Accept non-standard locale codes
    eventBus: myEventBus               // Optional NoireEventBus for event publishing
);
```

All options and the `EventBus` reference can also be changed at any time through fluent setters:

```csharp
localizer
    .SetReturnKeyWhenMissing(true)
    .SetAllowParentCultureFallback(false)
    .SetAllowDefaultLocaleFallback(true)
    .SetAllowCustomLocales(true)
    .SetAutoCreateMissingKeysInDefaultLocale(true);

localizer.EventBus = myEventBus;
```

### Default Locale Source

The `DefaultLocaleSource` enum controls how the default locale is resolved:
- `Custom` Uses the locale explicitly set via `SetDefaultLocale()`.
- `Windows` Uses the current Windows UI culture (`CultureInfo.CurrentUICulture`).
- `GameClient` Maps the Dalamud game client language to a locale (`en-US`, `fr-FR`, `de-DE`, `ja-JP`).

```csharp
// Explicit custom default
localizer.UseCustomDefaultLocale("en-US");

// Derive from Windows settings
localizer.UseWindowsLocaleAsDefaultLocale();

// Derive from the game client language
localizer.UseGameClientLocaleAsDefaultLocale();

// Or set the source directly
localizer.SetDefaultLocaleSource(DefaultLocaleSource.GameClient);
```

### Fallback Behavior

Control the fallback strategy with properties or fluent setters:

```csharp
localizer
    .SetAllowParentCultureFallback(true)   // fr-CA → fr
    .SetAllowDefaultLocaleFallback(true);  // ... → en-US (default locale)
```

You can also configure explicit fallback chains per locale:

```csharp
// When looking up en-GB, try en-US next before any implicit fallback
localizer.SetFallbackLocales("en-GB", "en-US");

// Clear explicit fallbacks by passing no values
localizer.SetFallbackLocales("en-GB");
```

### Missing Translation Behavior

Configure what happens when a key cannot be resolved in any locale:

- `ReturnKeyWhenMissing`: When `true`, missing translations return the key itself.
Default: `false`
- `MissingTranslationFormat`: Format string used when `ReturnKeyWhenMissing` is `false`. `{0}` is replaced with the key.
Default: `"[Missing: {0}]"`
- `AutoCreateMissingKeysInDefaultLocale`: When `true`, a missing key is automatically created in the default locale using the key as value.
Default: `false`

```csharp
localizer
    .SetReturnKeyWhenMissing(false)
    .SetMissingTranslationFormat("[Missing translation: {0}]")
    .SetAutoCreateMissingKeysInDefaultLocale(true);
```

### Custom Locales

By default, locale codes must be valid .NET `CultureInfo` names (e.g. `en-US`, `fr-FR`).
Enable custom locales to accept arbitrary codes:

```csharp
localizer.SetAllowCustomLocales(true);

// Now you can use non-standard codes
localizer.AddTranslation("custom_code");
```

When `AllowCustomLocales` is `true`, unrecognized locale strings are normalized to lowercase with hyphens (underscores are converted automatically).

### Persistent Configuration

The module automatically persists the following settings to disk via `LocalizerConfig`:

- **`SelectedLocale`**: the current active locale.
- **`DefaultLocaleSource`**: the default-locale resolution strategy.
- **`CustomDefaultLocale`**: the explicit default locale when using `DefaultLocaleSource.Custom`.

These are restored automatically when the module initializes, so the user's locale choice survives plugin reloads.

---

## Registering Translations

### Single Translation

```csharp
localizer.AddTranslation("en-US", "UI.Save", "Save");
localizer.AddTranslation("en-US", "UI.Save", "Save Changes", overwrite: true); // overwrites
localizer.AddTranslation("en-US", "UI.Save", "Nope", overwrite: false);        // skipped, key exists
```

### Bulk Registration

Pass a dictionary to register many keys at once:

```csharp
localizer.AddTranslations("ja-JP", new Dictionary<string, string>
{
    ["Window.Title"]  = "設定",
    ["Window.Save"]   = "保存",
    ["Window.Cancel"] = "キャンセル"
});
```

### Tuple Registration

Use tuples for inline registration:

```csharp
localizer.AddTranslations("de-DE",
    ("Window.Title", "Einstellungen"),
    ("Window.Save", "Speichern"),
    ("Window.Cancel", "Abbrechen")
);
```

### Fluent Locale Writer

`ForLocale()` returns a `LocaleWriter` that provides a fluent API scoped to a single locale.
Call `Done()` to return to the localizer for chaining:

```csharp
localizer
    .ForLocale("en-US")
        .Add("Window.Title", "Settings")
        .Add("Window.Save", "Save")
        .Add("Window.Cancel", "Cancel")
        .AddRange(new Dictionary<string, string>
        {
            ["Menu.Open"]  = "Open",
            ["Menu.Close"] = "Close"
        })
    .Done()
    .ForLocale("fr-FR")
        .Add("Window.Title", "Paramètres")
        .Add("Window.Save", "Enregistrer")
        .Add("Window.Cancel", "Annuler")
    .Done();
```

### Attribute-Based Registration

Decorate a class with `[NoireLocalizationLocale]` and its members with `[NoireLocalization]`:

```csharp
[NoireLocalizationLocale("en-US")]
public sealed class UiTexts
{
    // Value read from the property
    [NoireLocalization("Menu.Open")]
    public static string Open => "Open";

    // Value read from the field
    [NoireLocalization("Menu.Close")]
    public static string Close = "Close";

    // Explicit value in the attribute (member value is ignored)
    [NoireLocalization("Menu.Help", Value = "Help")]
    public static string HelpText => "ignored";

    // Per-member locale override
    [NoireLocalization("Menu.Open", Locale = "fr-FR")]
    public static string OpenFr => "Ouvrir";

    // Control overwrite behavior per member
    [NoireLocalization("Menu.Close", Overwrite = false)]
    public static string CloseNoOverwrite => "Close (won't overwrite)";
}
```

Register manually when `RegisterAutomatically` is not set or `false`:

```csharp
// From type
localizer.RegisterAttributedTranslations<UiTexts>();

// From instance (required for non-static members without a parameterless constructor)
localizer.RegisterAttributedTranslations(new UiTexts());

// From a Type reference
localizer.RegisterAttributedTranslations(typeof(UiTexts));
```

### Automatic Discovery

When `RegisterAutomatically = true` on `[NoireLocalizationLocale]`, the module automatically discovers and registers all matching provider classes from the plugin assembly at initialization time.

**Requirements for auto-discovery:**

1. The class must have `[NoireLocalizationLocale("...", RegisterAutomatically = true)]`.
2. The class must be non-abstract.
3. The class must contain at least one member with `[NoireLocalization]`.
4. The class must be in the plugin assembly (the assembly containing the Dalamud plugin entry point).

```csharp
[NoireLocalizationLocale("en-US", RegisterAutomatically = true)]
public sealed class EnglishTexts
{
    [NoireLocalization("UI.Hello")]
    public static string Hello => "Hello";
}

[NoireLocalizationLocale("fr-FR", RegisterAutomatically = true)]
public sealed class FrenchTexts
{
    [NoireLocalization("UI.Hello")]
    public static string Hello => "Bonjour";
}

// Both classes are registered automatically — no manual call needed.
```

---

## Retrieving Translations

### Basic Lookup

Retrieve a translation in the current locale:

```csharp
var value = localizer.Get("Window.Title");
```

### Locale-Specific Lookup

Retrieve a translation in a specific locale:

```csharp
var frValue = localizer.Get("fr-FR", "Window.Title");
```

### Indexed Format Arguments

Use `{0}`, `{1}`, etc. placeholders with positional arguments:

```csharp
localizer.AddTranslation("en-US", "Greeting", "Hello {0}, you have {1} messages!");

var message = localizer.Get("Greeting", "Aspher", 5);
// message == "Hello Aspher, you have 5 messages!"
```

### Named Token Replacement

Use `{TokenName}` placeholders with a named-arguments dictionary:

```csharp
localizer.AddTranslation("en-US", "Welcome", "Welcome back, {PlayerName}! Level {Level}.");

var message = localizer.Get("Welcome", new Dictionary<string, object?>
{
    ["PlayerName"] = "Aspher",
    ["Level"] = 90
});
// message == "Welcome back, Aspher! Level 90."
```

Token names support letters, digits, underscores, dots, and hyphens (`[A-Za-z0-9_.\-]+`).
Unmatched tokens are left as-is in the output.

You can optionally specify a locale:

```csharp
var msg = localizer.Get("Welcome", new Dictionary<string, object?>
{
    ["PlayerName"] = "Aspher"
}, locale: "fr-FR");
```

### Raw Lookup (No Formatting)

Retrieve the translation value without applying any format arguments:

```csharp
var raw = localizer.GetRaw("Greeting");                  // current locale
var raw = localizer.GetRaw("fr-FR", "Greeting");         // specific locale
```

### Safe Try Pattern

Check for existence without triggering missing-translation side effects (no events, no statistics tracking):

```csharp
if (localizer.TryGet("Window.Save", out var saveText))
{
    // Use saveText — found in current locale or fallback chain
}

if (localizer.TryGet("fr-FR", "Window.Save", out var frSave))
{
    // Use frSave — found in fr-FR or its fallback chain
}
```

---

## Locale Management

### Ensuring a Locale Exists

`EnsureLocale()` creates an empty locale entry if it doesn't already exist.
It is called automatically by most registration methods, but you can call it explicitly:

```csharp
localizer.EnsureLocale("pt-BR");
```

### Switching Locale at Runtime

```csharp
localizer.SetCurrentLocale("ja-JP");
```

This fires a `LocalizationLocaleChangedEvent` (both as a CLR event and through the EventBus) if the locale actually changed.
The new locale is also persisted to disk.

### Locale Display Names

Each locale has a human-readable display name used in the ImGui combo and other UI:

```csharp
// Set a custom display name
localizer.SetLocaleName("en-US", "English");
localizer.SetLocaleName("fr-FR", "Français");

// Set multiple at once
localizer.SetLocaleNames(new Dictionary<string, string>
{
    ["de-DE"] = "Deutsch",
    ["ja-JP"] = "日本語"
});

// Read the display name (falls back to CultureInfo.NativeName if not manually set)
var name = localizer.GetLocaleName("fr-FR"); // "Français"
```

### Listing Locales and Keys

```csharp
// All registered locales (sorted)
IReadOnlyList<string> locales = localizer.GetLocales();

// All translation keys across all locales (sorted, deduplicated)
IReadOnlyList<string> keys = localizer.GetAllKeys();

// All key/value pairs for a specific locale
IReadOnlyDictionary<string, string> translations = localizer.GetLocaleTranslations("en-US");
```

### Clearing Translations

```csharp
// Clear all translations from a single locale (locale entry remains)
localizer.ClearLocale("fr-FR");

// Clear everything (all locales, fallbacks, missing-key counters)
// Default and current locale entries are re-created automatically
localizer.ClearAllTranslations();
```

### Removing a Key

Remove a translation key from **all** locales:

```csharp
localizer.RemoveKey("Deprecated.Key");
```

---

## Fallback Resolution Order

When resolving a translation key, the module walks the following chain in order:

1. **Requested locale** — e.g. `fr-CA`.
2. **Parent culture** *(if `AllowParentCultureFallback` is `true`)* — e.g. `fr`.
3. **Explicit fallback locales** — configured via `SetFallbackLocales()`.
4. For each explicit fallback, its parent cultures are also walked (if enabled).
5. **Default locale** *(if `AllowDefaultLocaleFallback` is `true`)* — e.g. `en-US`.
6. **Default locale's parent cultures** *(if both options are enabled)* — e.g. `en`.

The first locale in this chain that contains the key wins. Circular references are detected and skipped.

If no translation is found after exhausting the full chain, the missing-translation handler is invoked.

---

## Events

The module exposes localization lifecycle events through two channels: standard CLR events and optional `NoireEventBus` publishing.

### CLR Events

Subscribe directly on the localizer instance:

```csharp
localizer.LocaleChanged += evt =>
    NoireLogger.LogInfo($"Locale changed: {evt.PreviousLocale} → {evt.NewLocale}");

localizer.LocaleRegistered += evt =>
    NoireLogger.LogInfo($"Locale registered: {evt.Locale}");

localizer.TranslationChanged += evt =>
    NoireLogger.LogInfo($"Translation [{evt.Locale}] {evt.Key} = {evt.Value} (existed: {evt.AlreadyExisted})");

localizer.MissingTranslation += evt =>
    NoireLogger.LogWarning($"Missing key '{evt.Key}' in '{evt.RequestedLocale}', tried: {string.Join(", ", evt.AttemptedLocales)}");
```

### EventBus Integration

When `EventBus` is set, all events are also published through the `NoireEventBus`:

```csharp
localizer.EventBus = myEventBus;

myEventBus.Subscribe<LocalizationLocaleChangedEvent>(evt =>
{
    // React to locale changes from the EventBus
});
```

### Event Types Reference

| Event Record                            | Fired When                                      | Properties |
|-----------------------------------------|-------------------------------------------------|------------|
| `LocalizationLocaleChangedEvent`        | `SetCurrentLocale()` changes the active locale.  | `PreviousLocale`, `NewLocale` |
| `LocalizationLocaleRegisteredEvent`     | A locale is created for the first time.          | `Locale` |
| `LocalizationTranslationChangedEvent`   | A translation is added or updated.               | `Locale`, `Key`, `Value`, `AlreadyExisted`, `OverwriteAttempted` |
| `LocalizationMissingTranslationEvent`   | A key cannot be resolved in any locale.          | `RequestedLocale`, `Key`, `AttemptedLocales` |

---

## ImGui Locale Combo

Draw a ready-made ImGui combo box that lets the user switch locale at runtime:

```csharp
// Default usage
localizer.DrawLocaleCombo();

// Customized
localizer.DrawLocaleCombo(
    label: "Language",
    width: 220f,           // pixel width (0 = auto)
    showLocaleCode: true   // append locale code, e.g. "English (en-US)"
);
```

The combo lists all registered locales sorted by display name.
When the user picks a different locale, `SetCurrentLocale()` is called automatically.
Returns `true` if the locale was changed.

---

## JSON Import and Export

### Export

```csharp
// Export to a JSON string
string json = localizer.ExportToJson(indented: true);

// Export directly to a file (directories are created as needed)
localizer.ExportToJsonFile("Config/Localization.json", indented: true);
```

**Output format:**

```json
{
  "en-US": {
    "UI.Hello": "Hello",
    "UI.Save": "Save"
  },
  "fr-FR": {
    "UI.Hello": "Bonjour",
    "UI.Save": "Enregistrer"
  }
}
```

### Import

```csharp
// Import from a JSON string
localizer.ImportFromJson(json, overwrite: true, clearExisting: false);

// Import from a file
localizer.ImportFromJsonFile("Config/Localization.json", overwrite: true, clearExisting: false);
```

---

## Statistics and Monitoring

Inspect the localization state at any time:

```csharp
LocalizationStatistics stats = localizer.GetStatistics();

NoireLogger.LogInfo($"Locales:              {stats.LocaleCount}");
NoireLogger.LogInfo($"Translations:         {stats.TranslationCount}");
NoireLogger.LogInfo($"Total added:          {stats.TotalTranslationsAdded}");
NoireLogger.LogInfo($"Total updated:        {stats.TotalTranslationsUpdated}");
NoireLogger.LogInfo($"Total locales created:{stats.TotalLocalesCreated}");
```

### Missing Translation Counters

Track which keys are being looked up but not found, and how many times:

```csharp
IReadOnlyDictionary<string, int> missing = localizer.GetMissingTranslationCounts();

foreach (var (key, count) in missing)
    NoireLogger.LogWarning($"Missing key '{key}' was requested {count} time(s).");
```

The same data is also available in `stats.MissingTranslationsByKey`.

---

## Attribute Reference

### NoireLocalizationLocaleAttribute

Applied to a **class** to declare its default locale and opt into automatic discovery.

```csharp
[NoireLocalizationLocale("en-US", RegisterAutomatically = true)]
public sealed class MyTranslations { ... }
```

### NoireLocalizationAttribute

Applied to a `string` **field** or **property** to declare a translation key.

```csharp
[NoireLocalization("UI.Hello")]
public static string Hello => "Hello";

[NoireLocalization("UI.Hello", Locale = "fr-FR", Value = "Bonjour", Overwrite = false)]
public static string HelloFr => "ignored when Value is set";
```

---

## Troubleshooting

### Translation not found

- Ensure the key exists in the requested locale or in one of its fallback locales.
- Verify fallback options are enabled (`AllowParentCultureFallback`, `AllowDefaultLocaleFallback`).
- Check for key typos — keys are case-insensitive but whitespace-sensitive.
- Use `GetMissingTranslationCounts()` to see which keys are being missed.
- Enable `AutoCreateMissingKeysInDefaultLocale` to auto-populate missing keys for later translation.

### Locale cannot be set

- Ensure the locale is a valid .NET culture name (e.g. `en-US`, `fr-FR`, `ja-JP`).
- If you need non-standard locale codes, enable `SetAllowCustomLocales(true)`.

### Attribute provider not registering

- Verify the class has `[NoireLocalizationLocale]` with the correct locale.
- Ensure `RegisterAutomatically = true` if you want auto-discovery (it is `true` by default).
- Ensure attributed members are `string` fields or properties.
- Properties must have a getter and must not be indexed.
- For instance (non-static) members, the class must have a parameterless constructor or you must pass an instance via `RegisterAttributedTranslations(instance)`.
- Confirm provider classes are visible in the plugin assembly (the assembly containing the Dalamud plugin entry point).

### Events not firing

- CLR events fire regardless of configuration.
- EventBus events only fire when `localizer.EventBus` is set to a valid `NoireEventBus` instance.
- `LocaleChanged` only fires when the locale actually changes (setting the same locale again is a no-op).

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [EventBus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
