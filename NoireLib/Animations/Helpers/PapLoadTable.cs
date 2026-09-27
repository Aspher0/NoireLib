using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;

namespace NoireLib.Animations.Helpers;

/// <summary>
/// The game's pap load table, naming per skeleton and per file whose copy of an animation the game loads.
/// The fallback chains of <see cref="EmotePathHelper"/> are derived from it.
/// </summary>
public sealed class PapLoadTable
{
    private const string LogPrefix = "[PapLoadTable] ";

    /// <summary>The game file the table is read from.</summary>
    public const string GamePath = "chara/xls/animation/papLoadTable.plt";

    private const int HeaderLength = 0x10;
    private const int FileLength = 12;
    private const int EntryLength = 168;
    private const int EntryBitsOffset = 8;
    private const int EntryWordCount = (EntryLength - EntryBitsOffset) / 8;
    private const int OverrideLength = 8;
    private const ushort BaseVariant = 1;
    private const int PlayableBodyModulo = 100;

    private static readonly Lazy<PapLoadTable?> Loaded = new(Load, LazyThreadSafetyMode.PublicationOnly);

    private readonly byte[] file;
    private readonly uint folderBase;
    private readonly int overrideBase;
    private readonly string[] keys;
    private readonly string[] folders;
    private readonly Dictionary<string, int> files;
    private readonly Dictionary<ushort, int> humanEntries;
    private readonly Func<string, bool> fileExists;
    private readonly Dictionary<ushort, byte[]> ships;
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> fallbackOrders = new(StringComparer.OrdinalIgnoreCase);

    private PapLoadTable(byte[] file, uint folderBase, int overrideBase, string[] keys, string[] folders,
        Dictionary<string, int> files, Dictionary<ushort, int> humanEntries, Func<string, bool> fileExists)
    {
        this.file = file;
        this.folderBase = folderBase;
        this.overrideBase = overrideBase;
        this.keys = keys;
        this.folders = folders;
        this.files = files;
        this.humanEntries = humanEntries;
        this.fileExists = fileExists;
        ships = humanEntries.Keys.ToDictionary(modelId => modelId, _ => new byte[keys.Length]);

        HumanSkeletons = [.. humanEntries.Keys
            .Where(modelId => modelId % PlayableBodyModulo == 1)
            .Order()
            .Select(modelId => EmotePathHelper.NormalizeHumanSkeletonId(modelId))];
    }

    /// <summary>The table read from the game's files on first access, or null when it cannot be read.</summary>
    public static PapLoadTable? Current => Loaded.Value;

    /// <summary>How many animation files the table lists.</summary>
    public int FileCount => files.Count;

    /// <summary>The playable human skeletons the table has an a0001 entry for, in id order.</summary>
    public IReadOnlyList<string> HumanSkeletons { get; }

    /// <summary>Parses the table's bytes.</summary>
    /// <param name="file">The file's bytes.</param>
    /// <param name="fileExists">The check for whether the game ships a full game path.</param>
    /// <returns>The parsed table, or null for an unknown layout.</returns>
    public static PapLoadTable? Parse(byte[] file, Func<string, bool> fileExists)
    {
        if (file.Length < HeaderLength)
            return null;

        var fileCount = BitConverter.ToUInt16(file, 0);
        var entryCount = BitConverter.ToUInt16(file, 2);
        var folderBase = BitConverter.ToUInt32(file, 8);
        var nameBase = BitConverter.ToUInt32(file, 12);
        var entryBase = HeaderLength + fileCount * FileLength;
        var overrideBase = entryBase + entryCount * EntryLength;

        if (fileCount == 0 || entryCount == 0 || overrideBase > file.Length || folderBase >= file.Length
            || nameBase >= file.Length)
        {
            return null;
        }

        var keys = new string[fileCount];
        var folders = new string[fileCount];
        var files = new Dictionary<string, int>(fileCount, StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < fileCount; index++)
        {
            var offset = HeaderLength + index * FileLength;

            if (ReadString(file, folderBase, BitConverter.ToUInt32(file, offset + 4)) is not { } folder
                || ReadString(file, nameBase, BitConverter.ToUInt32(file, offset + 8)) is not { } name)
            {
                return null;
            }

            folders[index] = folder;
            keys[index] = $"{folder}/{name}";
            files.TryAdd(keys[index], index);
        }

        var humanEntries = new Dictionary<ushort, int>();

        for (var index = 0; index < entryCount; index++)
        {
            var offset = entryBase + index * EntryLength;
            var code = BitConverter.ToUInt32(file, offset);

            if (code >> 16 == 0 && BitConverter.ToUInt16(file, offset + 4) == BaseVariant)
                humanEntries.TryAdd((ushort)code, offset);
        }

        return new PapLoadTable(file, folderBase, overrideBase, keys, folders, files, humanEntries, fileExists);
    }

    /// <summary>Reads the table and derives every fallback chain now, off the frame thread.</summary>
    public static void Warm()
    {
        if (Current is not { } table)
            return;

        foreach (var skeletonId in table.HumanSkeletons)
            table.FallbackOrderFor(skeletonId);
    }

    /// <summary>The skeleton whose copy of an animation the game loads for a human skeleton.</summary>
    /// <param name="skeletonId">The human skeleton id, such as "c0401".</param>
    /// <param name="relativePath">The path under the skeleton's a0001 folder, such as "bt_common/emote/hum.pap".</param>
    /// <returns>The skeleton id, or null when the table does not list the file or moves it to another folder.</returns>
    public string? SkeletonFor(string skeletonId, string relativePath)
    {
        if (ModelIdOf(skeletonId) is not { } modelId || !humanEntries.TryGetValue(modelId, out var entry))
            return null;

        if (KeyOf(relativePath) is not { } key || !files.TryGetValue(key, out var fileIndex))
            return null;

        return TargetOf(entry, fileIndex) is { } target ? EmotePathHelper.NormalizeHumanSkeletonId(target) : null;
    }

    /// <summary>
    /// The skeletons to try an animation the table does not list on, the skeleton itself first.<br/>
    /// The first call per skeleton checks thousands of game paths; <see cref="Warm"/> pays that off the frame thread.
    /// </summary>
    /// <param name="skeletonId">The human skeleton id.</param>
    /// <returns>The chain, or null when the table has no a0001 entry for the skeleton.</returns>
    public IReadOnlyList<string>? FallbackOrderFor(string skeletonId)
    {
        if (fallbackOrders.TryGetValue(skeletonId, out var chain))
            return chain;

        if (ModelIdOf(skeletonId) is not { } self || !humanEntries.ContainsKey(self))
            return null;

        return fallbackOrders.GetOrAdd(skeletonId, _ => DeriveFallbackOrder(self));
    }

    // Other skeletons rank by how often the table picks them over each other when both ship the same file.
    private IReadOnlyList<string> DeriveFallbackOrder(ushort self)
    {
        var entry = humanEntries[self];
        var targets = new List<(int FileIndex, ushort Target)>();
        var counts = new Dictionary<ushort, int> { [self] = 0 };

        for (var fileIndex = 0; fileIndex < keys.Length; fileIndex++)
        {
            if (TargetOf(entry, fileIndex) is not { } target || !Ships(target, fileIndex))
                continue;

            targets.Add((fileIndex, target));
            counts[target] = counts.GetValueOrDefault(target) + 1;
        }

        var candidates = counts.Keys.ToList();
        var beats = new Dictionary<(ushort, ushort), int>();

        foreach (var (fileIndex, target) in targets)
        {
            foreach (var other in candidates)
            {
                if (other != target && Ships(other, fileIndex))
                    beats[(target, other)] = beats.GetValueOrDefault((target, other)) + 1;
            }
        }

        int Wins(ushort candidate)
            => candidates.Count(other => other != candidate
                && beats.GetValueOrDefault((candidate, other)) > beats.GetValueOrDefault((other, candidate)));

        return [EmotePathHelper.NormalizeHumanSkeletonId(self), .. candidates
            .Where(candidate => candidate != self)
            .OrderByDescending(Wins)
            .ThenByDescending(candidate => counts[candidate])
            .ThenBy(candidate => candidate)
            .Select(candidate => EmotePathHelper.NormalizeHumanSkeletonId(candidate))];
    }

    // 0 unknown, 1 shipped, 2 missing. Concurrent writers store the same answer.
    private bool Ships(ushort modelId, int fileIndex)
    {
        if (!ships.TryGetValue(modelId, out var known))
            return false;

        if (known[fileIndex] == 0)
        {
            known[fileIndex] = fileExists(EmotePathHelper.GetSkeletonPath(
                EmotePathHelper.NormalizeHumanSkeletonId(modelId), keys[fileIndex] + ".pap")) ? (byte)1 : (byte)2;
        }

        return known[fileIndex] == 1;
    }

    // One bit covers four consecutive files; a set bit points at four override records, one per file.
    private ushort? TargetOf(int entry, int fileIndex)
    {
        var word = fileIndex >> 8;
        var bit = (fileIndex & 0xFF) >> 2;

        if (word >= EntryWordCount)
            return null;

        var bits = BitConverter.ToUInt64(file, entry + EntryBitsOffset + word * 8);

        if (((bits >> bit) & 1) == 0)
            return (ushort)BitConverter.ToUInt32(file, entry);

        var before = BitOperations.PopCount(bits & ((1UL << bit) - 1));

        for (var previous = 0; previous < word; previous++)
            before += BitOperations.PopCount(BitConverter.ToUInt64(file, entry + EntryBitsOffset + previous * 8));

        var first = BitConverter.ToUInt16(file, entry + 6);
        var record = overrideBase + OverrideLength * ((fileIndex & 3) + 4 * (before + first));

        if (record < overrideBase || record + OverrideLength > file.Length)
            return null;

        var code = BitConverter.ToUInt32(file, record);

        if (code >> 16 != 0 || BitConverter.ToUInt16(file, record + 4) != BaseVariant)
            return null;

        var folder = ReadString(file, folderBase, BitConverter.ToUInt16(file, record + 6));

        return string.Equals(folder, folders[fileIndex], StringComparison.OrdinalIgnoreCase) ? (ushort)code : null;
    }

    private static ushort? ModelIdOf(string skeletonId)
        => skeletonId.Length == 5 && (skeletonId[0] is 'c' or 'C') && ushort.TryParse(skeletonId.AsSpan(1), out var id)
            ? id
            : null;

    private static string? KeyOf(string relativePath)
    {
        var key = relativePath.Replace('\\', '/');

        if (key.EndsWith(".pap", StringComparison.OrdinalIgnoreCase))
            key = key[..^4];

        return key.IndexOf('/') > 0 ? key : null;
    }

    private static string? ReadString(byte[] file, uint table, uint offset)
    {
        var start = (long)table + offset;

        if (start >= file.Length)
            return null;

        var end = Array.IndexOf(file, (byte)0, (int)start);

        return end < 0 ? null : Encoding.ASCII.GetString(file, (int)start, end - (int)start);
    }

    private static PapLoadTable? Load()
    {
        try
        {
            if (NoireService.DataManager.GetFile(GamePath)?.Data is not { Length: > 0 } bytes)
            {
                NoireLogger.LogWarning($"Failed to read '{GamePath}'. Animation paths use the drawn skeleton only", LogPrefix);
                return null;
            }

            if (Parse(bytes, NoireService.DataManager.FileExists) is not { } table)
            {
                NoireLogger.LogWarning($"Unknown layout for '{GamePath}' ({bytes.Length} bytes). Animation paths use the drawn skeleton only", LogPrefix);
                return null;
            }

            NoireLogger.LogDebug($"Read {table.FileCount} animation files for {table.HumanSkeletons.Count} human skeletons.", LogPrefix);
            return table;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to read '{GamePath}'. Animation paths use the drawn skeleton only", LogPrefix);
            return null;
        }
    }
}
