using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Localizer;

// An edited changelog text has a new key. A similar old translation of the same version moves to it, outdated. The
// build and the localizer both use this: the game shows the same result before the files catch up.
internal static class ChangelogCarry
{
    private const string Prefix = "changelog.";

    // How alike an edited changelog text and its old version must be for the old translation to follow it.
    private const double MinimumSimilarity = 0.5;

    // Moves what it can in table and basis; returns how many translations moved.
    public static int Carry(IReadOnlyDictionary<string, string> sources, Dictionary<string, string> table, Dictionary<string, string> basis)
    {
        var orphans = table.Keys
            .Where(key => key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && !sources.ContainsKey(key) && basis.ContainsKey(key))
            .ToList();

        if (orphans.Count == 0)
            return 0;

        var open = sources.Keys
            .Where(key => key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && !table.ContainsKey(key))
            .ToList();

        var pairs = new List<(double Score, string Orphan, string Key)>();

        foreach (var orphan in orphans)
        {
            var version = VersionOf(orphan);

            foreach (var key in open)
            {
                if (!string.Equals(VersionOf(key), version, StringComparison.OrdinalIgnoreCase))
                    continue;

                var score = Similarity(basis[orphan], sources[key]);

                if (score >= MinimumSimilarity)
                    pairs.Add((score, orphan, key));
            }
        }

        var moved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, orphan, key) in pairs.OrderByDescending(static pair => pair.Score))
        {
            if (moved.Contains(orphan) || moved.Contains(key))
                continue;

            moved.Add(orphan);
            moved.Add(key);
            table[key] = table[orphan];
            basis[key] = basis[orphan];
            table.Remove(orphan);
            basis.Remove(orphan);
        }

        return moved.Count / 2;
    }

    // "changelog.2.3.0.0." for "changelog.2.3.0.0.1a2b3c4d".
    private static string VersionOf(string key) => key[..(key.LastIndexOf('.') + 1)];

    // One minus the edit distance over the longer length: 1 for equal texts, near 0 for unrelated ones.
    internal static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
            return 1d;

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return 1d - ((double)previous[b.Length] / Math.Max(a.Length, b.Length));
    }
}
