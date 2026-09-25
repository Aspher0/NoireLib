using System;
using System.Collections.Generic;

namespace NoireLib.Localizer;

// Keyed by text and values, dropped when the language revision moves: a label drawn every frame is built once.
internal static class FormatCache
{
    internal const int Capacity = 2048;

    private static readonly Dictionary<Key, string> Filled = new();
    private static int revision = -1;

    internal static int Count
    {
        get
        {
            lock (Filled)
                return Filled.Count;
        }
    }

    internal static string Format(NoireString text, string name, string value)
    {
        var key = new Key(text, name, value, null, null, 0);

        lock (Filled)
        {
            if (TryGet(key, out var hit))
                return hit;
        }

        return Store(key, Fill(text.Text, name, value));
    }

    internal static string Format(NoireString text, string name1, string value1, string name2, string value2)
    {
        var key = new Key(text, name1, value1, name2, value2, 0);

        lock (Filled)
        {
            if (TryGet(key, out var hit))
                return hit;
        }

        return Store(key, Fill(Fill(text.Text, name1, value1), name2, value2));
    }

    internal static string Plural(NoirePlural text, int count)
    {
        var key = new Key(text, null, null, null, null, count);

        lock (Filled)
        {
            if (TryGet(key, out var hit))
                return hit;
        }

        return Store(key, Fill(text.FormFor(count), "count", NoireLanguages.Number(count)));
    }

    internal static string Fill(string template, string name, string value)
    {
        if (template.IndexOf('{') < 0)
            return template;

        return template.Replace("{" + name + "}", value, StringComparison.Ordinal);
    }

    // The caller holds the lock.
    private static bool TryGet(Key key, out string filled)
    {
        var current = NoireLanguages.Revision;

        if (revision != current)
        {
            Filled.Clear();
            revision = current;
        }

        return Filled.TryGetValue(key, out filled!);
    }

    private static string Store(Key key, string filled)
    {
        lock (Filled)
        {
            if (Filled.Count >= Capacity)
                Filled.Clear();

            Filled[key] = filled;
        }

        return filled;
    }

    private readonly record struct Key(object Text, string? Name1, string? Value1, string? Name2, string? Value2, int Count);
}
