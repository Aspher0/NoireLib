using NoireLib.FileWatcher;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace NoireLib.Localizer;

public partial class NoireLocalizer
{
    internal const string DefaultSourceLanguage = "en";

    private const string LanguageFileExtension = ".lang";

    // The build's blank template (see NoireLanguageTemplate) is not a language, wherever it lands.
    private const string TemplateFileName = "template";

    private readonly Dictionary<string, Dictionary<string, string>> embeddedTables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> userTables = new(StringComparer.OrdinalIgnoreCase);

    // The source each translation was made from, per file and then per locale as applied; see LanguageFile.
    private readonly Dictionary<string, Dictionary<string, string>> embeddedBasis = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> userBasis = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> basisByLocale = new(StringComparer.OrdinalIgnoreCase);

    // The translators' own comments above each key, kept so a save writes them back.
    private readonly Dictionary<string, Dictionary<string, string>> embeddedNotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> userNotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> notesByLocale = new(StringComparer.OrdinalIgnoreCase);

    // The keys each locale last received from its language files, removed again before the files are reapplied.
    private readonly Dictionary<string, HashSet<string>> fileKeysByLocale = new(StringComparer.OrdinalIgnoreCase);

    private string sourceLanguage = DefaultSourceLanguage;
    private string? logLanguage;
    private NoireFileWatcher? userFileWatcher;
    private int textsVersion;
    private int batchDepth;
    private bool batchChanged;
    private NoireLanguageInfo[] languages = [];
    private int languagesVersion = -1;
    private int languagesRegistryVersion = -1;

    /// <summary>
    /// The language the declared texts are written in; a text missing from the active language shows in it.
    /// </summary>
    public string SourceLanguage => sourceLanguage;

    /// <summary>Sets the language the declared texts are written in. Defaults to <c>en</c>.</summary>
    /// <param name="language">The locale code.</param>
    /// <returns>This module.</returns>
    public NoireLocalizer SetSourceLanguage(string language)
    {
        sourceLanguage = NormalizeLocaleOrThrow(language, nameof(language));
        NotifyTextsChanged();
        return this;
    }

    /// <summary>The language <see cref="NoireMessage.Record"/> is written in, for history and exports.</summary>
    public string LogLanguage => logLanguage ?? sourceLanguage;

    /// <summary>Sets the language records are written in.</summary>
    /// <param name="language">The locale code, or <see langword="null"/> for <see cref="SourceLanguage"/>.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer SetLogLanguage(string? language)
    {
        logLanguage = language == null ? null : NormalizeLocaleOrThrow(language, nameof(language));
        return this;
    }

    private bool rejectOutdated;
    private bool rejectMissingOrExtraTags;

    /// <summary>Whether a translation of a since-changed source is set aside. Off by default.</summary>
    public bool RejectOutdated
    {
        get => rejectOutdated;
        set
        {
            if (rejectOutdated == value)
                return;

            rejectOutdated = value;
            NotifyTextsChanged();
        }
    }

    /// <summary>Whether a translation that lost or invented a <c>{name}</c> placeholder is set aside. Off by default.</summary>
    public bool RejectMissingOrExtraTags
    {
        get => rejectMissingOrExtraTags;
        set
        {
            if (rejectMissingOrExtraTags == value)
                return;

            rejectMissingOrExtraTags = value;
            NotifyTextsChanged();
        }
    }

    /// <summary>Sets <see cref="RejectOutdated"/>.</summary>
    /// <param name="enabled">True to set outdated translations aside.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer SetRejectOutdated(bool enabled)
    {
        RejectOutdated = enabled;
        return this;
    }

    /// <summary>Sets <see cref="RejectMissingOrExtraTags"/>.</summary>
    /// <param name="enabled">True to set translations with wrong placeholders aside.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireLocalizer SetRejectMissingOrExtraTags(bool enabled)
    {
        RejectMissingOrExtraTags = enabled;
        return this;
    }

    /// <summary>
    /// The languages the declared texts can be shown in: the source language, every loaded language, then
    /// <see cref="NoireLanguages.Pseudo"/> and <see cref="NoireLanguages.PseudoLong"/>.
    /// </summary>
    public IReadOnlyList<NoireLanguageInfo> Languages
    {
        get
        {
            var version = Volatile.Read(ref textsVersion);
            var registryVersion = NoireLanguages.RegistryVersion;

            if (languagesVersion != version || languagesRegistryVersion != registryVersion)
            {
                languages = BuildLanguages();
                languagesVersion = version;
                languagesRegistryVersion = registryVersion;
            }

            return languages;
        }
    }

    /// <summary>Lists the declared keys a language does not translate, plural forms included, sorted.</summary>
    /// <param name="language">The locale code.</param>
    /// <returns>The missing keys, empty for the source language and the pseudo-language.</returns>
    public IReadOnlyList<string> MissingKeys(string language)
    {
        var normalized = NormalizeLocaleOrThrow(language, nameof(language));
        var missing = new List<string>();

        if (IsSourceOrPseudo(normalized))
            return missing;

        var texts = NoireLanguages.Strings;
        var plurals = NoireLanguages.Plurals;
        var categories = PluralRules.CategoriesOf(normalized);

        lock (localizationLock)
        {
            foreach (var text in texts)
            {
                if (!TryResolveDeclaredLocked(normalized, text.Key, out _))
                    missing.Add(text.Key);
            }

            foreach (var plural in plurals)
            {
                foreach (var category in categories)
                {
                    var key = PluralKey(plural.Key, category);

                    if (!TryResolveDeclaredLocked(normalized, key, out _))
                        missing.Add(key);
                }
            }
        }

        missing.Sort(StringComparer.OrdinalIgnoreCase);
        return missing;
    }

    /// <summary>
    /// Writes a language's translations of the declared texts as a <c>.lang</c> file, each line under its source text.
    /// </summary>
    /// <param name="language">The locale code.</param>
    /// <returns>The file content.</returns>
    public string ExportLanguageFile(string language)
    {
        var normalized = NormalizeLocaleOrThrow(language, nameof(language));
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var text in NoireLanguages.Strings)
            sources[text.Key] = text.Source;

        foreach (var plural in NoireLanguages.Plurals)
        {
            foreach (var form in plural.Forms)
                sources[PluralKey(plural.Key, form.Key)] = form.Value;
        }

        lock (localizationLock)
        {
            if (translationsByLocale.TryGetValue(normalized, out var translations))
            {
                foreach (var pair in translations)
                    table[pair.Key] = pair.Value;
            }
        }

        Dictionary<string, string>? basis;
        Dictionary<string, string>? notes;

        lock (localizationLock)
        {
            basis = basisByLocale.TryGetValue(normalized, out var found) ? new Dictionary<string, string>(found, StringComparer.OrdinalIgnoreCase) : null;
            notes = notesByLocale.TryGetValue(normalized, out var comments) ? new Dictionary<string, string>(comments, StringComparer.OrdinalIgnoreCase) : null;
        }

        return LanguageFile.Write(table, key => sources.GetValueOrDefault(key), basis == null ? null : key => basis.GetValueOrDefault(key),
            notes == null ? null : key => notes.GetValueOrDefault(key));
    }

    // The source a language's translation of a key was made from, or null when unknown.
    internal string? TranslatedFrom(string language, string key)
    {
        lock (localizationLock)
            return basisByLocale.TryGetValue(language, out var basis) ? basis.GetValueOrDefault(key) : null;
    }

    // Null forgets the source, for a translation put back to one saved without it.
    internal void SetTranslatedFrom(string language, string key, string? source)
    {
        var normalized = NormalizeLocaleOrThrow(language, nameof(language));
        bool changed;

        lock (localizationLock)
        {
            if (!basisByLocale.TryGetValue(normalized, out var basis))
                basisByLocale[normalized] = basis = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            changed = !string.Equals(basis.GetValueOrDefault(key), source, StringComparison.Ordinal);

            if (source == null)
                basis.Remove(key);
            else
                basis[key] = source;
        }

        if (changed)
            NotifyTextsChanged();
    }

    internal string? UserLanguageFolder
    {
        get
        {
            var directory = FileHelper.GetPluginConfigDirectory();
            return directory == null ? null : Path.Combine(directory, "Localization");
        }
    }

    internal string ResolveDeclared(string key, string source)
    {
        var language = CurrentLocale;

        if (IsPseudo(language))
            return PseudoLanguage.Transform(source, IsPseudoLong(language));

        return ResolveDeclaredIn(language, key, source) ?? source;
    }

    internal string ResolveRecord(string key, string source) => ResolveDeclaredIn(LogLanguage, key, source) ?? source;

    internal string ResolvePluralForm(NoirePlural plural, int count)
    {
        var language = CurrentLocale;

        if (IsPseudo(language))
            return PseudoLanguage.Transform(plural.SourceForm(count), IsPseudoLong(language));

        var category = PluralRules.For(language, count);
        var sourceCategory = PluralRules.For(sourceLanguage, count);

        lock (localizationLock)
        {
            if (TryResolveDeclaredLocked(language, PluralKey(plural.Key, category), out var form, SourceFormOf(plural, category)))
                return form;

            if (category != PluralCategory.Other
                && TryResolveDeclaredLocked(language, PluralKey(plural.Key, PluralCategory.Other), out form, SourceFormOf(plural, PluralCategory.Other)))
                return form;

            if (TryResolveDeclaredLocked(sourceLanguage, PluralKey(plural.Key, sourceCategory), out form, SourceFormOf(plural, sourceCategory)))
                return form;
        }

        return plural.SourceForm(count);
    }

    // Replaces a language's embedded or user table and reapplies its files.
    internal void LoadLanguageText(string language, string text, bool userFile)
    {
        var normalized = NormalizeLocaleOrThrow(language, nameof(language));
        var basis = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var table = LanguageFile.Parse(text, basis, notes);

        lock (localizationLock)
        {
            (userFile ? userTables : embeddedTables)[normalized] = table;
            (userFile ? userBasis : embeddedBasis)[normalized] = basis;
            (userFile ? userNotes : embeddedNotes)[normalized] = notes;
        }

        ApplyLanguageFiles(normalized);
    }

    // Reads, or forgets when deleted, one user language file and reapplies its language.
    internal void ReloadUserLanguageFile(string path)
    {
        if (!path.EndsWith(LanguageFileExtension, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileNameWithoutExtension(path), TemplateFileName, StringComparison.OrdinalIgnoreCase))
            return;

        string language;

        try
        {
            language = NormalizeLocaleOrThrow(Path.GetFileNameWithoutExtension(path), nameof(path));
        }
        catch (ArgumentException)
        {
            NoireLogger.LogWarning(this, $"Ignored language file with an unknown language code: {path}");
            return;
        }

        if (File.Exists(path))
        {
            var text = FileHelper.ReadTextFromFile(path);

            if (text == null)
                return;

            var basis = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var table = LanguageFile.Parse(text, basis, notes);

            lock (localizationLock)
            {
                userTables[language] = table;
                userBasis[language] = basis;
                userNotes[language] = notes;
            }
        }
        else
        {
            lock (localizationLock)
            {
                userTables.Remove(language);
                userBasis.Remove(language);
                userNotes.Remove(language);
            }
        }

        ApplyLanguageFiles(language);
    }

    // Removes one key from one language, for a translation emptied in the editor.
    internal void RemoveTranslation(string language, string key)
    {
        var normalized = NormalizeLocaleOrThrow(language, nameof(language));
        bool removed;

        lock (localizationLock)
        {
            removed = translationsByLocale.TryGetValue(normalized, out var translations) && translations.Remove(key);

            if (basisByLocale.TryGetValue(normalized, out var basis))
                basis.Remove(key);
        }

        if (removed)
            NotifyTextsChanged();
    }

    // Writes a language to the user's language folder and reads it back; null when there is no folder or the write failed.
    internal string? SaveUserLanguageFile(string language)
        => UserLanguageFolder is { } folder ? SaveUserLanguageFile(language, folder) : null;

    internal string? SaveUserLanguageFile(string language, string folder)
    {
        var normalized = NormalizeLocaleOrThrow(language, nameof(language));
        var path = Path.Combine(folder, normalized + LanguageFileExtension);

        if (!FileHelper.WriteTextToFile(path, ExportLanguageFile(normalized)))
            return null;

        ReloadUserLanguageFile(path);

        if (userFileWatcher == null && NoireService.IsInitialized())
            WatchUserLanguageFolder(folder);

        return path;
    }

    private static string PluralKey(string key, PluralCategory category) => key + "." + PluralRules.Suffix(category);

    private static bool IsPseudo(string language) => NoireLanguages.IsPseudo(language);

    private static bool IsPseudoLong(string language) => string.Equals(language, NoireLanguages.PseudoLong, StringComparison.OrdinalIgnoreCase);

    private bool IsSourceOrPseudo(string language)
        => IsPseudo(language) || string.Equals(language, sourceLanguage, StringComparison.OrdinalIgnoreCase) || IsRegionOf(language, sourceLanguage);

    private static bool IsRegionOf(string language, string parent)
        => language.Length > parent.Length && language[parent.Length] == '-' && language.StartsWith(parent, StringComparison.OrdinalIgnoreCase);

    // The active language's chain, then the source language's, whose table holds corrections of the source texts.
    private string? ResolveDeclaredIn(string language, string key, string? source = null)
    {
        lock (localizationLock)
        {
            if (TryResolveDeclaredLocked(language, key, out var value, source))
                return value;

            if (TryResolveDeclaredLocked(sourceLanguage, key, out value, source))
                return value;
        }

        return null;
    }

    // Caller holds localizationLock. A translation the reject options set aside passes to the next language.
    private bool TryResolveDeclaredLocked(string language, string key, out string value, string? source = null)
    {
        foreach (var candidate in GetDeclaredLookupOrderLocked(language))
        {
            if (translationsByLocale.TryGetValue(candidate, out var translations) && translations.TryGetValue(key, out value!)
                && (source == null || !RejectedLocked(candidate, key, source, value)))
                return true;
        }

        value = string.Empty;
        return false;
    }

    private bool RejectedLocked(string language, string key, string source, string translation)
    {
        if (rejectOutdated && basisByLocale.TryGetValue(language, out var basis) && basis.TryGetValue(key, out var from)
            && !string.Equals(from, source, StringComparison.Ordinal))
            return true;

        if (!rejectMissingOrExtraTags)
            return false;

        var (missing, unknown) = PlaceholderCheck.Compare(source, translation);
        return missing.Length > 0 || unknown.Length > 0;
    }

    private static string SourceFormOf(NoirePlural plural, PluralCategory category)
        => plural.Forms.TryGetValue(category, out var form) ? form : plural.Forms[PluralCategory.Other];

    private void NotifyTextsChanged()
    {
        Interlocked.Increment(ref textsVersion);

        if (Volatile.Read(ref batchDepth) > 0)
        {
            batchChanged = true;
            return;
        }

        if (ReferenceEquals(NoireLanguages.Localizer, this))
            NoireLanguages.Bump();
    }

    // The embedded table first, the user's file on top: a user file holds only its changes.
    private void ApplyLanguageFiles(string language)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var mergedBasis = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var mergedNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        lock (localizationLock)
        {
            Merge(embeddedTables, embeddedBasis);
            Merge(userTables, userBasis);
            basisByLocale[language] = mergedBasis;

            foreach (var notes in new[] { embeddedNotes, userNotes })
            {
                if (notes.TryGetValue(language, out var found))
                {
                    foreach (var pair in found)
                        mergedNotes[pair.Key] = pair.Value;
                }
            }

            notesByLocale[language] = mergedNotes;
        }

        // A key a file translates takes that file's source with it, or none when the file names none.
        void Merge(Dictionary<string, Dictionary<string, string>> tables, Dictionary<string, Dictionary<string, string>> bases)
        {
            if (!tables.TryGetValue(language, out var table))
                return;

            bases.TryGetValue(language, out var basis);

            foreach (var pair in table)
            {
                merged[pair.Key] = pair.Value;

                if (basis != null && basis.TryGetValue(pair.Key, out var source))
                    mergedBasis[pair.Key] = source;
                else
                    mergedBasis.Remove(pair.Key);
            }
        }

        Interlocked.Increment(ref batchDepth);

        try
        {
            EnsureLocale(language);

            lock (localizationLock)
            {
                if (fileKeysByLocale.TryGetValue(language, out var previous) && translationsByLocale.TryGetValue(language, out var translations))
                {
                    foreach (var key in previous)
                    {
                        if (!merged.ContainsKey(key))
                            translations.Remove(key);
                    }
                }

                fileKeysByLocale[language] = new HashSet<string>(merged.Keys, StringComparer.OrdinalIgnoreCase);
            }

            NotifyTextsChanged();
            AddTranslations(language, merged);
        }
        finally
        {
            Interlocked.Decrement(ref batchDepth);
        }

        if (batchChanged && Volatile.Read(ref batchDepth) == 0)
        {
            batchChanged = false;
            NotifyTextsChanged();
        }

        CarryChangelogTexts();
    }

    // An edited changelog text has a new key: its old translation moves there, outdated, as the build does to the files.
    internal void CarryChangelogTexts()
    {
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var text in NoireLanguages.Strings)
        {
            if (text.Key.StartsWith("changelog.", StringComparison.OrdinalIgnoreCase))
                sources[text.Key] = text.Source;
        }

        if (sources.Count == 0)
            return;

        var moved = 0;

        lock (localizationLock)
        {
            foreach (var pair in translationsByLocale)
            {
                if (basisByLocale.TryGetValue(pair.Key, out var basis))
                    moved += ChangelogCarry.Carry(sources, pair.Value, basis);
            }
        }

        if (moved > 0)
            NotifyTextsChanged();
    }

    private NoireLanguageInfo[] BuildLanguages()
    {
        var texts = NoireLanguages.Strings;
        var plurals = NoireLanguages.Plurals;
        var total = texts.Length + plurals.Length;
        var current = CurrentLocale;
        var codes = new List<string>();
        var userOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var credits = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        lock (localizationLock)
        {
            foreach (var pair in translationsByLocale)
            {
                if (pair.Value.Count > 0 && !IsPseudo(pair.Key) && !string.Equals(pair.Key, sourceLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    codes.Add(pair.Key);
                    credits[pair.Key] = LanguageFile.Credits(pair.Value.GetValueOrDefault(LanguageFile.CreditsKey));
                }
            }

            foreach (var code in userTables.Keys)
            {
                if (!embeddedTables.ContainsKey(code))
                    userOnly.Add(code);
            }
        }

        var active = IsPseudo(current) ? current : ActiveCode(current, codes);
        var list = new List<NoireLanguageInfo>(codes.Count + 3)
        {
            new(sourceLanguage, DisplayName(sourceLanguage), total, total, false, string.Equals(active, sourceLanguage, StringComparison.OrdinalIgnoreCase)),
        };

        var others = new List<NoireLanguageInfo>(codes.Count);

        foreach (var code in codes)
            others.Add(new NoireLanguageInfo(code, DisplayName(code), TranslatedCount(code, texts, plurals), total, userOnly.Contains(code), string.Equals(active, code, StringComparison.OrdinalIgnoreCase))
            {
                Credits = credits[code],
                Outdated = OutdatedCount(code, texts, plurals),
            });

        others.Sort(static (a, b) => string.Compare(a.NativeName, b.NativeName, StringComparison.CurrentCultureIgnoreCase));
        list.AddRange(others);
        list.Add(new NoireLanguageInfo(NoireLanguages.Pseudo, "Pseudo", total, total, false, IsPseudo(current) && !IsPseudoLong(current)));
        list.Add(new NoireLanguageInfo(NoireLanguages.PseudoLong, "Pseudo (long)", total, total, false, IsPseudoLong(current)));
        return [.. list];
    }

    // The listed code the active language shows: itself, else its closest listed parent, else the source language.
    private string ActiveCode(string current, List<string> codes)
    {
        string? parent = null;

        foreach (var code in codes)
        {
            if (string.Equals(code, current, StringComparison.OrdinalIgnoreCase))
                return code;

            if (IsRegionOf(current, code) && code.Length > (parent?.Length ?? 0))
                parent = code;
        }

        return parent ?? sourceLanguage;
    }

    private int TranslatedCount(string language, NoireString[] texts, NoirePlural[] plurals)
    {
        var count = 0;
        var categories = PluralRules.CategoriesOf(language);

        lock (localizationLock)
        {
            foreach (var text in texts)
            {
                if (TryResolveDeclaredLocked(language, text.Key, out _))
                    count++;
            }

            foreach (var plural in plurals)
            {
                var complete = true;

                foreach (var category in categories)
                    complete &= TryResolveDeclaredLocked(language, PluralKey(plural.Key, category), out _);

                if (complete)
                    count++;
            }
        }

        return count;
    }

    // How many texts a language translates from a source that has changed since.
    private int OutdatedCount(string language, NoireString[] texts, NoirePlural[] plurals)
    {
        var count = 0;

        lock (localizationLock)
        {
            if (!basisByLocale.TryGetValue(language, out var basis) || !translationsByLocale.TryGetValue(language, out var translations))
                return 0;

            foreach (var text in texts)
            {
                if (translations.ContainsKey(text.Key) && basis.TryGetValue(text.Key, out var from) && !string.Equals(from, text.Source, StringComparison.Ordinal))
                    count++;
            }

            foreach (var plural in plurals)
            {
                foreach (var form in plural.Forms)
                {
                    var key = PluralKey(plural.Key, form.Key);

                    if (translations.ContainsKey(key) && basis.TryGetValue(key, out var from) && !string.Equals(from, form.Value, StringComparison.Ordinal))
                    {
                        count++;
                        break;
                    }
                }
            }
        }

        return count;
    }

    private string DisplayName(string language)
    {
        var name = GetLocaleName(language);

        if (name.Length == 0 || char.IsUpper(name[0]))
            return name;

        return NoireLanguages.CultureFor(language).TextInfo.ToUpper(name[0]) + name[1..];
    }

    private void StartDeclaredTexts()
    {
        RuntimeHelpers.RunClassConstructor(typeof(NoireStrings).TypeHandle);

        var assembly = NoireService.PluginInstance?.GetType().Assembly;

        if (assembly == null)
            return;

        RunDeclaringClassConstructors(assembly);
        LoadEmbeddedLanguageFiles(assembly);
        LoadUserLanguageFiles();
    }

    private void StopDeclaredTexts()
    {
        NoireLanguages.Unbind(this);
        userFileWatcher?.Dispose();
        userFileWatcher = null;
    }

    // Static texts register themselves when their class initializes, which would otherwise wait for the first read.
    private void RunDeclaringClassConstructors(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (var type in GetLoadableTypes(assembly))
        {
            if (type.ContainsGenericParameters)
                continue;

            foreach (var field in type.GetFields(flags))
            {
                if (field.FieldType != typeof(NoireString) && field.FieldType != typeof(NoirePlural))
                    continue;

                try
                {
                    RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                }
                catch (TypeInitializationException ex)
                {
                    NoireLogger.LogError(this, ex, $"Could not initialize the texts declared in {type.FullName}.");
                }

                break;
            }
        }
    }

    private void LoadEmbeddedLanguageFiles(Assembly assembly)
    {
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.EndsWith(LanguageFileExtension, StringComparison.OrdinalIgnoreCase))
                continue;

            var name = resource[..^LanguageFileExtension.Length];
            var language = name[(name.LastIndexOf('.') + 1)..];

            if (string.Equals(language, TemplateFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                using var stream = assembly.GetManifestResourceStream(resource);

                if (stream == null)
                    continue;

                using var reader = new StreamReader(stream);
                LoadLanguageText(language, reader.ReadToEnd(), userFile: false);
            }
            catch (ArgumentException)
            {
                NoireLogger.LogWarning(this, $"Ignored embedded language file with an unknown language code: {resource}");
            }
        }
    }

    private void LoadUserLanguageFiles()
    {
        var folder = UserLanguageFolder;

        if (folder == null || !Directory.Exists(folder))
            return;

        foreach (var path in Directory.EnumerateFiles(folder, "*" + LanguageFileExtension))
            ReloadUserLanguageFile(path);

        WatchUserLanguageFolder(folder);
    }

    private void WatchUserLanguageFolder(string folder)
    {
        userFileWatcher = new NoireFileWatcher(active: true, enableLogging: false);
        userFileWatcher.WatchDirectory(folder, OnUserLanguageFileEvent, patterns: ["*" + LanguageFileExtension], includeSubdirectories: false);
    }

    private void OnUserLanguageFileEvent(FileWatchNotification notification)
    {
        ReloadUserLanguageFile(notification.FullPath);

        if (notification.OldFullPath != null)
            ReloadUserLanguageFile(notification.OldFullPath);
    }
}
