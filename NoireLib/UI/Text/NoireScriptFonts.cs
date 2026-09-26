using Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using NoireLib.Localizer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NoireLib.UI;

/// <summary>
/// The CJK glyphs the plugin's loaded translations use, merged from Dalamud's Noto Sans CJK. Every loaded language
/// at once: switching language never rebuilds a font.
/// </summary>
public static class NoireScriptFonts
{
    private static readonly object SyncRoot = new();

    private static int seenRevision = -1;
    private static int seenRegistry = -1;
    private static int seenExtras = -1;
    private static int extrasVersion;
    private static readonly Dictionary<string, string[]> Extras = new();
    private static readonly HashSet<string> Prepared = new(StringComparer.OrdinalIgnoreCase);
    private static bool currentLanguageOnly;
    private static ushort[]? ranges;
    private static int glyphCount;
    private static int generation;

    /// <summary>The glyph ranges merged into every face, or <see langword="null"/> when no loaded text needs one.</summary>
    public static ushort[]? Ranges
    {
        get
        {
            Refresh();
            return Volatile.Read(ref ranges);
        }
    }

    // Moves when Ranges changes.
    internal static int Generation
    {
        get
        {
            Refresh();
            return Volatile.Read(ref generation);
        }
    }

    internal static (ushort[]? Ranges, int Generation) Snapshot
    {
        get
        {
            Refresh();

            lock (SyncRoot)
                return (ranges, generation);
        }
    }

    internal static int GlyphCount
    {
        get
        {
            Refresh();
            return Volatile.Read(ref glyphCount);
        }
    }

    public static bool CurrentLanguageOnly
    {
        get => Volatile.Read(ref currentLanguageOnly);
        set
        {
            lock (SyncRoot)
            {
                if (currentLanguageOnly == value)
                    return;

                currentLanguageOnly = value;
                Interlocked.Increment(ref extrasVersion);
            }

            UiFaceAtlas.MarkAllStale();
        }
    }

    public static void Prepare(string locale)
    {
        lock (SyncRoot)
        {
            if (!Prepared.Add(locale))
                return;

            Interlocked.Increment(ref extrasVersion);
        }

        UiFaceAtlas.MarkAllStale();
    }

    public static void Unprepare(string locale)
    {
        lock (SyncRoot)
        {
            if (!Prepared.Remove(locale))
                return;

            Interlocked.Increment(ref extrasVersion);
        }

        UiFaceAtlas.MarkAllStale();
    }

    public static void Include(string key, IEnumerable<string?> texts)
    {
        var kept = new List<string>();

        foreach (var text in texts)
        {
            if (!string.IsNullOrEmpty(text))
                kept.Add(text);
        }

        lock (SyncRoot)
        {
            Extras[key] = kept.ToArray();
            Interlocked.Increment(ref extrasVersion);
        }

        UiFaceAtlas.MarkAllStale();
    }

    public static void Exclude(string key)
    {
        lock (SyncRoot)
        {
            if (!Extras.Remove(key))
                return;

            Interlocked.Increment(ref extrasVersion);
        }

        UiFaceAtlas.MarkAllStale();
    }

    // Called from a font's pre-build step, after its own glyphs.
    internal static void Merge(IFontAtlasBuildToolkitPreBuild toolkit, ImFontPtr font, float sizePx)
        => Merge(toolkit, font, sizePx, Ranges);

    internal static void Merge(IFontAtlasBuildToolkitPreBuild toolkit, ImFontPtr font, float sizePx, ushort[]? needed, float? gamma = null)
    {
        if (font.IsNull || needed == null)
            return;

        var config = new SafeFontConfig
        {
            SizePx = sizePx,
            MergeFont = font,
            GlyphRanges = needed,
            PixelSnapH = true,
        };

        if (gamma is { } value)
            config.RasterizerGamma = value;

        toolkit.AddDalamudAssetFont(DalamudAsset.NotoSansCjkMedium, config);
    }

    // Kana, Hangul, CJK ideographs, symbols and punctuation, and the full width forms: what Noto Sans CJK draws and
    // Dalamud's default font lacks.
    internal static bool IsCjk(char c)
        => c is >= 'ᄀ' and <= 'ᇿ'
            or >= '⺀' and <= '鿿'
            or >= 'ꥠ' and <= '꥿'
            or >= '가' and <= '퟿'
            or >= '豈' and <= '﫿'
            or >= '︰' and <= '﹏'
            or >= '＀' and <= '￯';

    // One range per run of consecutive characters, zero terminated, or null when there is none.
    internal static ushort[]? RangesOf(IEnumerable<string> texts)
    {
        var chars = new SortedSet<char>();

        foreach (var text in texts)
        {
            if (text == null)
                continue;

            foreach (var c in text)
            {
                if (IsCjk(c))
                    chars.Add(c);
            }
        }

        if (chars.Count == 0)
            return null;

        var built = new List<ushort>();
        var start = -1;
        var previous = -1;

        foreach (var c in chars)
        {
            if (start >= 0 && c == previous + 1)
            {
                previous = c;
                continue;
            }

            if (start >= 0)
            {
                built.Add((ushort)start);
                built.Add((ushort)previous);
            }

            start = previous = c;
        }

        built.Add((ushort)start);
        built.Add((ushort)previous);
        built.Add(0);
        return built.ToArray();
    }

    private static void Refresh()
    {
        var revision = NoireLanguages.Revision;
        var registry = NoireLanguages.RegistryVersion;
        var extras = Volatile.Read(ref extrasVersion);

        if (revision == Volatile.Read(ref seenRevision) && registry == Volatile.Read(ref seenRegistry) && extras == Volatile.Read(ref seenExtras))
            return;

        lock (SyncRoot)
        {
            extras = extrasVersion;

            if (revision == seenRevision && registry == seenRegistry && extras == seenExtras)
                return;

            var next = RangesOf(LoadedTexts(NoireLanguages.Localizer).Concat(Extras.Values.SelectMany(static texts => texts)));
            var count = 0;

            if (next != null)
            {
                for (var index = 0; index + 1 < next.Length; index += 2)
                    count += next[index + 1] - next[index] + 1;
            }

            Volatile.Write(ref glyphCount, count);

            Volatile.Write(ref seenRevision, revision);
            Volatile.Write(ref seenRegistry, registry);
            Volatile.Write(ref seenExtras, extras);

            if (next == null ? ranges == null : ranges != null && next.AsSpan().SequenceEqual(ranges))
                return;

            Volatile.Write(ref ranges, next);
            Interlocked.Increment(ref generation);
        }
    }

    private static IEnumerable<string> LoadedTexts(NoireLocalizer? localizer)
    {
        if (localizer == null)
            yield break;

        foreach (var language in localizer.Languages)
            yield return language.NativeName;

        if (!currentLanguageOnly)
        {
            foreach (var locale in localizer.GetLocales())
            {
                foreach (var text in localizer.GetLocaleTranslations(locale).Values)
                    yield return text;
            }

            yield break;
        }

        foreach (var locale in LocalesToLoad(localizer))
        {
            foreach (var text in localizer.GetLocaleTranslations(locale).Values)
                yield return text;
        }
    }

    private static List<string> LocalesToLoad(NoireLocalizer localizer)
    {
        var wanted = new List<string>(Prepared.Count + 1) { localizer.CurrentLocale };

        foreach (var locale in Prepared)
        {
            if (!wanted.Contains(locale, StringComparer.OrdinalIgnoreCase))
                wanted.Add(locale);
        }

        var loaded = localizer.GetLocales();
        var result = new List<string>(wanted.Count);

        foreach (var locale in loaded)
        {
            foreach (var want in wanted)
            {
                if (locale.Equals(want, StringComparison.OrdinalIgnoreCase)
                    || locale.StartsWith(want + "-", StringComparison.OrdinalIgnoreCase)
                    || want.StartsWith(locale + "-", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(locale);
                    break;
                }
            }
        }

        return result;
    }
}
