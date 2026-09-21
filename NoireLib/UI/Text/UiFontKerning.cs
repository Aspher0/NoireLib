using System;
using System.Collections.Generic;

namespace NoireLib.UI;

// Read from the GPOS 'kern' feature or the legacy 'kern' table. ImGui does not kern.
internal sealed class UiFontKerning
{
    private readonly byte[] data;
    private readonly int[] pairSubtables;
    private readonly int legacyPairs;
    private readonly int legacyCount;
    private readonly Dictionary<long, int> resolved = new();

    private UiFontKerning(byte[] data, int[] pairSubtables, int legacyPairs, int legacyCount)
    {
        this.data = data;
        this.pairSubtables = pairSubtables;
        this.legacyPairs = legacyPairs;
        this.legacyCount = legacyCount;
    }

    internal static UiFontKerning? Create(byte[] data, int gpos, int legacy)
    {
        var subtables = gpos >= 0 ? ReadPairSubtables(data, gpos) : null;
        var (pairs, count) = legacy >= 0 ? ReadLegacyPairs(data, legacy) : (0, 0);

        if ((subtables == null || subtables.Length == 0) && count == 0)
            return null;

        return new UiFontKerning(data, subtables ?? [], pairs, count);
    }

    internal int Units(int left, int right)
    {
        var key = ((long)left << 32) | (uint)right;

        if (resolved.TryGetValue(key, out var units))
            return units;

        units = Lookup(left, right);
        resolved[key] = units;

        return units;
    }

    private int Lookup(int left, int right)
    {
        foreach (var subtable in pairSubtables)
        {
            var value = ReadPair(data, subtable, left, right);

            if (value != 0)
                return value;
        }

        return legacyCount > 0 ? ReadLegacyPair(data, legacyPairs, legacyCount, left, right) : 0;
    }

    #region GPOS

    private static int[] ReadPairSubtables(byte[] data, int gpos)
    {
        if (gpos + 10 > data.Length)
            return [];

        var features = gpos + U16(data, gpos + 6);
        var lookups = gpos + U16(data, gpos + 8);

        if (features + 2 > data.Length || lookups + 2 > data.Length)
            return [];

        var wanted = new HashSet<int>();
        var featureCount = U16(data, features);

        for (var index = 0; index < featureCount; index++)
        {
            var record = features + 2 + (index * 6);

            if (record + 6 > data.Length)
                break;

            if (data[record] != (byte)'k' || data[record + 1] != (byte)'e' || data[record + 2] != (byte)'r' || data[record + 3] != (byte)'n')
                continue;

            var feature = features + U16(data, record + 4);

            if (feature + 4 > data.Length)
                continue;

            var count = U16(data, feature + 2);

            for (var slot = 0; slot < count; slot++)
            {
                var at = feature + 4 + (slot * 2);

                if (at + 2 <= data.Length)
                    wanted.Add(U16(data, at));
            }
        }

        if (wanted.Count == 0)
            return [];

        var found = new List<int>();
        var lookupCount = U16(data, lookups);

        foreach (var index in wanted)
        {
            if (index >= lookupCount)
                continue;

            var lookup = lookups + U16(data, lookups + 2 + (index * 2));

            if (lookup + 6 > data.Length)
                continue;

            var type = U16(data, lookup);
            var subtables = U16(data, lookup + 4);

            for (var slot = 0; slot < subtables; slot++)
            {
                var at = lookup + 6 + (slot * 2);

                if (at + 2 > data.Length)
                    break;

                var subtable = lookup + U16(data, at);

                // An extension subtable jumps to the real one past 16 bit offsets.
                if (type == 9)
                {
                    if (subtable + 8 > data.Length || U16(data, subtable + 2) != 2)
                        continue;

                    subtable += (int)U32(data, subtable + 4);
                }
                else if (type != 2)
                {
                    continue;
                }

                if (subtable + 4 <= data.Length)
                    found.Add(subtable);
            }
        }

        return found.ToArray();
    }

    private static int ReadPair(byte[] data, int subtable, int left, int right)
    {
        var format = U16(data, subtable);
        var coverage = CoverageIndex(data, subtable + U16(data, subtable + 2), left);

        if (coverage < 0)
            return 0;

        var valueFormat1 = U16(data, subtable + 4);
        var valueFormat2 = U16(data, subtable + 6);
        var advanceOffset = AdvanceOffset(valueFormat1);

        if (advanceOffset < 0)
            return 0;

        var recordSize = ValueSize(valueFormat1) + ValueSize(valueFormat2);

        if (format == 1)
        {
            var sets = U16(data, subtable + 8);

            if (coverage >= sets)
                return 0;

            var set = subtable + U16(data, subtable + 10 + (coverage * 2));

            if (set + 2 > data.Length)
                return 0;

            var pairs = U16(data, set);
            var entry = 2 + recordSize;

            var low = 0;
            var high = pairs - 1;

            while (low <= high)
            {
                var middle = (low + high) / 2;
                var at = set + 2 + (middle * entry);

                if (at + entry > data.Length)
                    return 0;

                var second = U16(data, at);

                if (second == right)
                    return advanceOffset >= ValueSize(valueFormat1) ? 0 : S16(data, at + 2 + advanceOffset);

                if (second < right)
                    low = middle + 1;
                else
                    high = middle - 1;
            }

            return 0;
        }

        if (format != 2 || subtable + 16 > data.Length)
            return 0;

        var firstClass = ClassOf(data, subtable + U16(data, subtable + 8), left);
        var secondClass = ClassOf(data, subtable + U16(data, subtable + 10), right);
        var firstCount = U16(data, subtable + 12);
        var secondCount = U16(data, subtable + 14);

        if (firstClass >= firstCount || secondClass >= secondCount)
            return 0;

        var record = subtable + 16 + (((firstClass * secondCount) + secondClass) * recordSize);

        return record + recordSize <= data.Length ? S16(data, record + advanceOffset) : 0;
    }

    private static int AdvanceOffset(int valueFormat)
    {
        if ((valueFormat & 0x0004) == 0)
            return -1;

        var offset = 0;

        if ((valueFormat & 0x0001) != 0)
            offset += 2;

        if ((valueFormat & 0x0002) != 0)
            offset += 2;

        return offset;
    }

    private static int ValueSize(int valueFormat)
    {
        var size = 0;

        for (var bit = 0; bit < 8; bit++)
        {
            if ((valueFormat & (1 << bit)) != 0)
                size += 2;
        }

        return size;
    }

    private static int CoverageIndex(byte[] data, int coverage, int glyph)
    {
        if (coverage + 4 > data.Length)
            return -1;

        var format = U16(data, coverage);
        var count = U16(data, coverage + 2);

        if (format == 1)
        {
            var low = 0;
            var high = count - 1;

            while (low <= high)
            {
                var middle = (low + high) / 2;
                var at = coverage + 4 + (middle * 2);

                if (at + 2 > data.Length)
                    return -1;

                var current = U16(data, at);

                if (current == glyph)
                    return middle;

                if (current < glyph)
                    low = middle + 1;
                else
                    high = middle - 1;
            }

            return -1;
        }

        if (format != 2)
            return -1;

        for (var index = 0; index < count; index++)
        {
            var at = coverage + 4 + (index * 6);

            if (at + 6 > data.Length)
                return -1;

            var start = U16(data, at);
            var end = U16(data, at + 2);

            if (glyph < start)
                return -1;

            if (glyph <= end)
                return U16(data, at + 4) + (glyph - start);
        }

        return -1;
    }

    private static int ClassOf(byte[] data, int classDef, int glyph)
    {
        if (classDef + 4 > data.Length)
            return 0;

        var format = U16(data, classDef);

        if (format == 1)
        {
            var start = U16(data, classDef + 2);
            var count = U16(data, classDef + 4);

            if (glyph < start || glyph >= start + count)
                return 0;

            var at = classDef + 6 + ((glyph - start) * 2);

            return at + 2 <= data.Length ? U16(data, at) : 0;
        }

        if (format != 2)
            return 0;

        var ranges = U16(data, classDef + 2);

        for (var index = 0; index < ranges; index++)
        {
            var at = classDef + 4 + (index * 6);

            if (at + 6 > data.Length)
                return 0;

            var first = U16(data, at);
            var last = U16(data, at + 2);

            if (glyph < first)
                return 0;

            if (glyph <= last)
                return U16(data, at + 4);
        }

        return 0;
    }

    #endregion

    #region Legacy kern table

    private static (int Pairs, int Count) ReadLegacyPairs(byte[] data, int kern)
    {
        if (kern + 4 > data.Length)
            return (0, 0);

        var subtables = U16(data, kern + 2);
        var at = kern + 4;

        for (var index = 0; index < subtables; index++)
        {
            if (at + 14 > data.Length)
                break;

            var length = U16(data, at + 2);
            var coverage = U16(data, at + 4);

            // Horizontal, no minimum, no cross-stream, format 0.
            if ((coverage & 0x0F) == 0x01 && (coverage >> 8) == 0)
                return (at + 14, U16(data, at + 6));

            at += length > 0 ? length : 14;
        }

        return (0, 0);
    }

    private static int ReadLegacyPair(byte[] data, int pairs, int count, int left, int right)
    {
        var target = ((uint)left << 16) | (uint)right;
        var low = 0;
        var high = count - 1;

        while (low <= high)
        {
            var middle = (low + high) / 2;
            var at = pairs + (middle * 6);

            if (at + 6 > data.Length)
                return 0;

            var current = ((uint)U16(data, at) << 16) | (uint)U16(data, at + 2);

            if (current == target)
                return S16(data, at + 4);

            if (current < target)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return 0;
    }

    #endregion

    private static int U16(byte[] data, int at) => (data[at] << 8) | data[at + 1];

    private static short S16(byte[] data, int at) => (short)U16(data, at);

    private static uint U32(byte[] data, int at)
        => ((uint)data[at] << 24) | ((uint)data[at + 1] << 16) | ((uint)data[at + 2] << 8) | data[at + 3];
}
