using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace NoireLib.Localizer;

/// <summary>
/// The language every <see cref="NoireString"/> and <see cref="NoirePlural"/> is read in, taken from the most recently
/// activated <see cref="NoireLocalizer"/>.
/// </summary>
public static class NoireLanguages
{
    /// <summary>
    /// The code of the pseudo-language, which shows every declared text accented, bracketed and padded by 30%.
    /// </summary>
    public const string Pseudo = "qps-ploc";

    /// <summary>
    /// The code of the long pseudo-language, which shows every declared text accented, bracketed and written twice, for
    /// finding a layout that cannot hold a translation twice as long as the source.
    /// </summary>
    public const string PseudoLong = "qps-long";

    /// <summary>Whether a code is one of the pseudo-languages, <see cref="Pseudo"/> or <see cref="PseudoLong"/>.</summary>
    /// <param name="code">The language code.</param>
    /// <returns>True for a pseudo-language.</returns>
    public static bool IsPseudo(string? code)
        => string.Equals(code, Pseudo, StringComparison.OrdinalIgnoreCase) || string.Equals(code, PseudoLong, StringComparison.OrdinalIgnoreCase);

    private const int CachedNumbers = 256;

    private static readonly object RegistryLock = new();
    private static readonly Dictionary<string, NoireString> Texts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, NoirePlural> PluralTexts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string?[] Numbers = new string?[CachedNumbers];

    private static NoireLocalizer? localizer;
    private static int revision;
    private static int registryVersion;
    private static CultureInfo culture = CultureInfo.InvariantCulture;
    private static int cultureRevision = -1;
    private static int numbersRevision = -1;

    /// <summary>The localizer declared texts are read from, or <see langword="null"/> when none is active.</summary>
    public static NoireLocalizer? Localizer => Volatile.Read(ref localizer);

    /// <summary>Moves whenever the active language or a translation changes.</summary>
    public static int Revision => Volatile.Read(ref revision);

    private static IReadOnlyList<NoireLanguageInfo>? creditsFor;
    private static int creditsRevision = -1;
    private static string[] creditLines = [];

    /// <summary>One cached line per credited language file, such as <c>Deutsch: Anna, Ben</c>. Empty without credits.</summary>
    public static IReadOnlyList<string> CreditLines
    {
        get
        {
            var languages = Localizer?.Languages;

            if (languages == null)
                return [];

            if (ReferenceEquals(languages, creditsFor) && creditsRevision == Revision)
                return creditLines;

            var lines = new List<string>();

            foreach (var language in languages)
            {
                if (language.Credits.Count > 0)
                    lines.Add(NoireStrings.TranslationCreditLine.With("language", language.NativeName, "names", string.Join(", ", language.Credits)));
            }

            creditsFor = languages;
            creditsRevision = Revision;
            creditLines = [.. lines];
            return creditLines;
        }
    }

    /// <summary>Writes a whole number the active language's way, cached per language for 0 to 255.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The formatted number.</returns>
    public static string Number(int value)
    {
        if ((uint)value >= CachedNumbers)
            return value.ToString("N0", Culture);

        var current = Revision;

        if (numbersRevision != current)
        {
            Array.Clear(Numbers);
            numbersRevision = current;
        }

        return Numbers[value] ??= value.ToString("N0", Culture);
    }

    // Raised after Revision moved, on the thread that changed the language or a translation.
    internal static event Action? OnChanged;

    internal static int RegistryVersion => Volatile.Read(ref registryVersion);

    internal static CultureInfo Culture
    {
        get
        {
            var current = Revision;

            if (cultureRevision != current)
            {
                culture = CultureFor(Localizer?.CurrentLocale);
                cultureRevision = current;
            }

            return culture;
        }
    }

    internal static NoireString[] Strings
    {
        get
        {
            lock (RegistryLock)
                return [.. Texts.Values];
        }
    }

    internal static NoirePlural[] Plurals
    {
        get
        {
            lock (RegistryLock)
                return [.. PluralTexts.Values];
        }
    }

    internal static NoireString? Find(string key)
    {
        lock (RegistryLock)
            return Texts.GetValueOrDefault(key);
    }

    internal static void Register(NoireString text)
    {
        lock (RegistryLock)
            Texts[text.Key] = text;

        Interlocked.Increment(ref registryVersion);
    }

    internal static void Register(NoirePlural text)
    {
        lock (RegistryLock)
            PluralTexts[text.Key] = text;

        Interlocked.Increment(ref registryVersion);
    }

    internal static void Bind(NoireLocalizer target)
    {
        Volatile.Write(ref localizer, target);
        Bump();
    }

    internal static void Unbind(NoireLocalizer target)
    {
        if (Interlocked.CompareExchange(ref localizer, null, target) == target)
            Bump();
    }

    internal static void Bump()
    {
        Interlocked.Increment(ref revision);
        OnChanged?.Invoke();
    }

    internal static string Resolve(NoireString text)
        => Localizer?.ResolveDeclared(text.Key, text.Source) ?? text.Source;

    internal static string Record(NoireString text)
        => Localizer?.ResolveRecord(text.Key, text.Source) ?? text.Source;

    internal static string PluralForm(NoirePlural text, int count)
        => Localizer?.ResolvePluralForm(text, count) ?? text.SourceForm(count);

    internal static CultureInfo CultureFor(string? locale)
    {
        if (string.IsNullOrEmpty(locale) || IsPseudo(locale))
            return CultureInfo.InvariantCulture;

        try
        {
            return CultureInfo.GetCultureInfo(locale);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
