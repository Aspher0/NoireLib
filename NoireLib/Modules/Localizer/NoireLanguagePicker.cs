using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.Threading;

namespace NoireLib.Localizer;

public static class NoireLanguagePicker
{
    private static readonly object SyncRoot = new();
    private static readonly List<NoireLanguageInfo> Offered = new();

    private static IReadOnlyList<NoireLanguageInfo>? source;
    private static bool refreshedWithPseudo;
    private static string[] names = [];
    private static string? pending;
    private static bool includePseudo;

    public static bool IncludePseudo
    {
        get => includePseudo;
        set => includePseudo = value;
    }

    public static string? Pending => Volatile.Read(ref pending);

    public static IReadOnlyList<NoireLanguageInfo> Languages
    {
        get
        {
            lock (SyncRoot)
            {
                RefreshLocked();
                return Offered;
            }
        }
    }

    public static string[] Names
    {
        get
        {
            lock (SyncRoot)
            {
                RefreshLocked();
                return names;
            }
        }
    }

    public static int Active
    {
        get
        {
            lock (SyncRoot)
            {
                RefreshLocked();

                for (var index = 0; index < Offered.Count; index++)
                {
                    if (pending == null ? Offered[index].IsActive : string.Equals(Offered[index].Code, pending, StringComparison.OrdinalIgnoreCase))
                        return index;
                }

                return 0;
            }
        }
    }

    public static void Pick(int index)
    {
        string? code;

        lock (SyncRoot)
        {
            RefreshLocked();

            if ((uint)index >= (uint)Offered.Count)
                return;

            code = Offered[index].IsActive ? null : Offered[index].Code;
        }

        Pick(code);
    }

    public static void Pick(string? code)
    {
        string? dropped;
        string? next = null;

        lock (SyncRoot)
        {
            dropped = pending;

            if (code != null && !string.Equals(NoireLanguages.Localizer?.CurrentLocale, code, StringComparison.OrdinalIgnoreCase))
                next = code;

            pending = next;
        }

        if (dropped != null && !string.Equals(dropped, code, StringComparison.OrdinalIgnoreCase))
            NoireScriptFonts.Unprepare(dropped);

        if (next == null)
            return;

        NoireScriptFonts.Prepare(next);
        UiFontPump.Ensure();
        Commit();
    }

    internal static void Commit()
    {
        if (Volatile.Read(ref pending) == null || !NoireFont.GlyphsApplied)
            return;

        string? code;

        lock (SyncRoot)
        {
            code = pending;
            pending = null;
        }

        if (code == null)
            return;

        NoireLanguages.Localizer?.SetCurrentLocale(code);
        NoireScriptFonts.Unprepare(code);
    }

    private static void RefreshLocked()
    {
        var languages = NoireLanguages.Localizer?.Languages;

        if (ReferenceEquals(languages, source) && refreshedWithPseudo == includePseudo)
            return;

        source = languages;
        refreshedWithPseudo = includePseudo;
        Offered.Clear();

        if (languages != null)
        {
            foreach (var language in languages)
            {
                if (!includePseudo && NoireLanguages.IsPseudo(language.Code))
                    continue;

                Offered.Add(language);
            }
        }

        names = new string[Offered.Count];

        for (var index = 0; index < Offered.Count; index++)
            names[index] = Offered[index].NativeName;
    }
}
