using System.Collections.Generic;

namespace NoireLib.Localizer;

/// <summary>
/// Localization statistics snapshot.
/// </summary>
/// <param name="LocaleCount">The total number of locales.</param>
/// <param name="TranslationCount">The total number of translations.</param>
/// <param name="TotalTranslationsAdded">The total number of translations added.</param>
/// <param name="TotalTranslationsUpdated">The total number of translations updated.</param>
/// <param name="TotalLocalesCreated">The total number of locales created.</param>
/// <param name="MissingTranslationsByKey">A dictionary of missing translations by key.</param>
public sealed record LocalizationStatistics(
    int LocaleCount,
    int TranslationCount,
    long TotalTranslationsAdded,
    long TotalTranslationsUpdated,
    long TotalLocalesCreated,
    IReadOnlyDictionary<string, int> MissingTranslationsByKey);
