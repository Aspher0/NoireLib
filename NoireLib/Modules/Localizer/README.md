# Module Documentation : NoireLocalizer

You are reading the documentation for the `NoireLocalizer` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Registering Translations](#registering-translations)
- [Retrieving Translations](#retrieving-translations)
- [Advanced Features](#advanced-features)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireLocalizer` is a complete localization module for plugin text and language workflows. It provides:
- **Locale-based translation storage** with runtime lookup
- **Flexible fallback chains** (requested locale, parent locale, explicit fallbacks, default locale)
- **Runtime locale switching** with Events and EventBus events
- **Fluent translation registration API** for clean setup code
- **Attribute-based auto-registration** from provider classes
- **JSON import/export support** for flexibility

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Register Translations

```csharp
[NoireLocalizationLocale("en-US", RegisterAutomatically = true)]
public class EnglishTranslations
{
    [NoireLocalization("UI.Hello")]
    public static string HelloEn => "Hello";
}

// Or use the fluent builder

localizer
    .AddTranslation("en-US", "UI.Hello", "Hello")
    .AddTranslation("fr-FR", "UI.Hello", "Bonjour")
    .AddTranslation("de-DE", "UI.Hello", "Hallo");
```

### 2. Read Localized Values

```csharp
// Not necessary, but you can set the current locale explicitly if you want to test or switch languages at runtime
localizer.SetCurrentLocale("fr-FR"); // Or let the user pick with the managed Combo Box

var text = localizer.Get("UI.Hello");
// text = "Bonjour"
```

That's it! You now have a working localization flow.

---

## Configuration

### Module Parameters

Configure the localizer with the constructor:

```csharp
var localizer = new NoireLocalizer(
    moduleId: "MyLocalizer",
    active: true,
    enableLogging: true,
    defaultLocale: "en-US",
    currentLocale: "en-US",
    returnKeyWhenMissing: false,
    allowParentCultureFallback: true,
    allowDefaultLocaleFallback: true,
    defaultLocaleSource: DefaultLocaleSource.Custom,
    allowCustomLocales: false,
    eventBus: eventBus
);
```

### Fallback Behavior

Control the fallback strategy:

```csharp
localizer
    .SetAllowParentCultureFallback(true)
    .SetAllowDefaultLocaleFallback(true)
    .SetFallbackLocales("en-UK", "en-US"); // explicit fallback chain, en-UK -> en-US
```

### Missing Translation Behavior

Configure how missing keys are handled:

```csharp
localizer
    .SetReturnKeyWhenMissing(false)
    .SetMissingTranslationFormat("[Missing translation: {0}]")
    .SetAutoCreateMissingKeysInDefaultLocale(true);
```

---

## Registering Translations

### Fluent Locale Writer

Use fluent registration for readable setup:

```csharp
localizer
    .ForLocale("en-US")
    .Add("Window.Title", "Settings")
    .Add("Window.Save", "Save")
    .Add("Window.Cancel", "Cancel")
    .Done()
    .ForLocale("fr-FR")
    .Add("Window.Title", "Paramètres")
    .Add("Window.Save", "Enregistrer")
    .Add("Window.Cancel", "Annuler")
    .Done();
```

### Bulk Registration

Register many keys at once:

```csharp
localizer.AddTranslations("ja-JP", new Dictionary<string, string>
{
    ["Window.Title"] = "設定",
    ["Window.Save"] = "保存",
    ["Window.Cancel"] = "キャンセル"
});
```

### Attribute-Based Registration

Register values from attributed providers:

```csharp
[NoireLocalizationLocale("en-US", RegisterAutomatically = true)]
public sealed class UiTexts
{
    [NoireLocalization("Menu.Open")]
    public static string Open => "Open";

    [NoireLocalization("Menu.Close")]
    public static string Close => "Close";
}

localizer.RegisterAttributedTranslations<UiTexts>(); // Not needed if RegisterAutomatically = true and the class is visible in the plugin assembly
```

---

## Retrieving Translations

### Basic Lookup

```csharp
var value = localizer.Get("Window.Title");
var frValue = localizer.Get("fr-FR", "Window.Title");
```

### Indexed Format Arguments

```csharp
localizer.AddTranslation("en-US", "Greeting", "Hello {0}!");

var message = localizer.Get("Greeting", "Aspher");
```

### Named Token Replacement

```csharp
localizer.AddTranslation("en-US", "Greeting.Named", "Hello {PlayerName}!");

var message = localizer.Get("Greeting.Named", new Dictionary<string, object?>
{
    ["PlayerName"] = "Aspher"
});
```

### Safe Try Pattern

```csharp
if (localizer.TryGet("fr-FR", "Window.Save", out var saveText))
{
    // Use saveText
}
```

---

## Advanced Features

### Runtime Locale Switching UI

Draw an ImGui combo for users:

```csharp
localizer.DrawLocaleCombo(label: "Language", width: 220f, showLocaleCode: true);
```

### Import and Export

```csharp
// Export
var json = localizer.ExportToJson(indented: true);
localizer.ExportToJsonFile("Config/Localization.json");

// Import
localizer.ImportFromJsonFile("Config/Localization.json", overwrite: true, clearExisting: false);
```

### Statistics and Monitoring

Inspect localization state and missing keys:

```csharp
var stats = localizer.GetStatistics();
var missing = localizer.GetMissingTranslationCounts();

NoireLogger.LogInfo($"Locales: {stats.LocaleCount}");
NoireLogger.LogInfo($"Translations: {stats.TranslationCount}");
NoireLogger.LogInfo($"Missing tracked keys: {missing.Count}");
```

---

## Troubleshooting

### Translation not found
- Ensure the key exists in the requested locale.
- Verify fallback options (`AllowParentCultureFallback`, `AllowDefaultLocaleFallback`).
- Check for key typos and whitespace.

### Locale cannot be set
- Ensure the locale is valid (ex: `en-US`, `fr-FR`).
- Enable custom locales via `SetAllowCustomLocales(true)` if needed.

### Attribute provider not registering
- Verify the class has `[NoireLocalizationLocale]` or each member has `Locale` in `[NoireLocalization]`.
- Ensure attributed members are `string` fields/properties.
- Confirm provider types are visible in the plugin assembly when using auto-registration.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [EventBus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
- [FileWatcher Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/FileWatcher/README.md)
