using Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using NoireLib.Localizer;
using System;
using System.Collections.Generic;
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
    private static ushort[]? ranges;
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

    // Called from a font's pre-build step, after its own glyphs.
    internal static void Merge(IFontAtlasBuildToolkitPreBuild toolkit, ImFontPtr font, float sizePx)
    {
        if (font.IsNull || Ranges is not { } needed)
            return;

        toolkit.AddDalamudAssetFont(DalamudAsset.NotoSansCjkMedium, new SafeFontConfig
        {
            SizePx = sizePx,
            MergeFont = font,
            GlyphRanges = needed,
            PixelSnapH = true,
        });
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

        if (revision == Volatile.Read(ref seenRevision) && registry == Volatile.Read(ref seenRegistry))
            return;

        lock (SyncRoot)
        {
            if (revision == seenRevision && registry == seenRegistry)
                return;

            var next = RangesOf(LoadedTexts(NoireLanguages.Localizer));

            Volatile.Write(ref seenRevision, revision);
            Volatile.Write(ref seenRegistry, registry);

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

        foreach (var locale in localizer.GetLocales())
        {
            foreach (var text in localizer.GetLocaleTranslations(locale).Values)
                yield return text;
        }
    }
}
