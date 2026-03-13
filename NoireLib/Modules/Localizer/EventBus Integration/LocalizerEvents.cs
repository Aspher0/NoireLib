using System.Collections.Generic;

namespace NoireLib.Localizer;

/// <summary>
/// Event emitted when current locale changes.
/// </summary>
/// <param name="PreviousLocale">The previous locale that was active before the change.</param>
/// <param name="NewLocale">The new locale that has been set.</param>
public sealed record LocalizationLocaleChangedEvent(string PreviousLocale, string NewLocale);

/// <summary>
/// Event emitted when a locale is registered.
/// </summary>
/// <param name="Locale">The locale that was registered.</param>
public sealed record LocalizationLocaleRegisteredEvent(string Locale);

/// <summary>
/// Event emitted when a translation is added or updated.
/// </summary>
/// <param name="Locale">The locale for which the translation was added or updated.</param>
/// <param name="Key">The key of the translation that was added or updated.</param>
/// <param name="Value">The value of the translation that was added or updated.</param>
/// <param name="AlreadyExisted">Indicates whether the translation already existed.</param>
/// <param name="OverwriteAttempted">Indicates whether an attempt was made to overwrite an existing translation.</param>
public sealed record LocalizationTranslationChangedEvent(string Locale, string Key, string Value, bool AlreadyExisted, bool OverwriteAttempted);

/// <summary>
/// Event emitted when a key cannot be resolved for a locale.
/// </summary>
/// <param name="RequestedLocale">The locale that was requested for translation.</param>
/// <param name="Key">The key that could not be resolved.</param>
/// <param name="AttemptedLocales">The list of locales that were attempted to resolve the key, in order of resolution attempts.</param>
public sealed record LocalizationMissingTranslationEvent(string RequestedLocale, string Key, IReadOnlyList<string> AttemptedLocales);
