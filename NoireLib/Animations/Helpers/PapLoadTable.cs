using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;

namespace NoireLib.Animations.Helpers;

/// <summary>
/// The game's pap load table, naming for each skeleton which skeleton's copy of an animation file it loads.
/// The choice is made file by file. The per-skeleton fallback chains are derived from it.
/// </summary>
public sealed class PapLoadTable
{
    private const string LogPrefix = "[PapLoadTable] ";

    /// <summary> The game file the table is read from. </summary>
    public const string GamePath = "chara/xls/animation/papLoadTable.plt";

    private const int HeaderLength = 0x10;
    private const int FileLength = 12;
    private const int EntryLength = 168;
    private const int EntryBitsOffset = 8;
    private const int EntryWordCount = (EntryLength - EntryBitsOffset) / 8;
    private const int OverrideLength = 8;
    private const ushort BaseVariant = 1;
    private const int PlayableBodyModulo = 100;

    private readonly byte[] _file;
    private readonly uint _folderBase;
    private readonly int _overrideBase;
    private readonly string[] _keys;
    private readonly string[] _folders;
    private readonly Dictionary<string, int> _files;
    private readonly Dictionary<ushort, int> _humanEntries;
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _fallbackOrders =
        new Dictionary<string, IReadOnlyList<string>>();

    private PapLoadTable(byte[] file, uint folderBase, int overrideBase, string[] keys, string[] folders,
        Dictionary<string, int> files, Dictionary<ushort, int> humanEntries)
    {
        _file = file;
        _folderBase = folderBase;
        _overrideBase = overrideBase;
        _keys = keys;
        _folders = folders;
        _files = files;
        _humanEntries = humanEntries;

        HumanSkeletons = [.. humanEntries.Keys
            .Where(modelId => modelId % PlayableBodyModulo == 1)
            .Order()
            .Select(modelId => EmotePathHelper.NormalizeHumanSkeletonId(modelId))];
    }

    /// <summary> How many animation files the table lists. </summary>
    public int FileCount => _files.Count;

    /// <summary> The playable human skeletons the table has an a0001 entry for, in id order. </summary>
    public IReadOnlyList<string> HumanSkeletons { get; }

    /// <summary> Parses the table's bytes. </summary>
    /// <param name="file">The file's bytes.</param>
    /// <returns>The parsed table, or null when the bytes are not in a layout this reads.</returns>
    public static PapLoadTable? Parse(byte[] file)
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

        return new PapLoadTable(file, folderBase, overrideBase, keys, folders, files, humanEntries);
    }

    /// <summary>
    /// The skeleton whose copy of an animation the game loads for a human skeleton, read from its a0001 entry.
    /// </summary>
    /// <param name="skeletonId">The human skeleton id, such as "c0401".</param>
    /// <param name="relativePath">The path under the skeleton's a0001 folder, such as "bt_common/emote/hum.pap".</param>
    /// <returns>The skeleton id, or null when the table does not list the file or sends it to another folder.</returns>
    public string? SkeletonFor(string skeletonId, string relativePath)
    {
        if (ModelIdOf(skeletonId) is not { } modelId || !_humanEntries.TryGetValue(modelId, out var entry))
            return null;

        if (KeyOf(relativePath) is not { } key || !_files.TryGetValue(key, out var fileIndex))
            return null;

        return TargetOf(entry, fileIndex) is { } target ? EmotePathHelper.NormalizeHumanSkeletonId(target) : null;
    }

    /// <summary>
    /// The skeletons to try an animation on when the table does not list it, the skeleton itself first.
    /// </summary>
    /// <param name="skeletonId">The human skeleton id.</param>
    /// <returns>The chain, or null when the table has none for that skeleton.</returns>
    public IReadOnlyList<string>? FallbackOrderFor(string skeletonId)
        => _fallbackOrders.TryGetValue(skeletonId, out var chain) ? chain : null;

    /// <summary>
    /// Derives every playable skeleton's fallback chain: the skeleton itself, then the skeletons the table sends its
    /// files to, ordered by which one the table picks when both ship a copy of the same file.
    /// </summary>
    /// <param name="fileExists">Whether the game ships a given full game path.</param>
    public void BuildFallbackOrders(Func<string, bool> fileExists)
    {
        var exists = new Dictionary<(ushort, int), bool>();

        bool Ships(ushort modelId, int fileIndex)
        {
            if (!exists.TryGetValue((modelId, fileIndex), out var ships))
            {
                ships = fileExists(EmotePathHelper.GetSkeletonPath(
                    EmotePathHelper.NormalizeHumanSkeletonId(modelId), _keys[fileIndex] + ".pap"));
                exists[(modelId, fileIndex)] = ships;
            }

            return ships;
        }

        var orders = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var skeletonId in HumanSkeletons)
        {
            var self = ModelIdOf(skeletonId)!.Value;
            var entry = _humanEntries[self];
            var targets = new List<(int FileIndex, ushort Target)>();
            var counts = new Dictionary<ushort, int> { [self] = 0 };

            for (var fileIndex = 0; fileIndex < _keys.Length; fileIndex++)
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

            orders[skeletonId] = [skeletonId, .. candidates
                .Where(candidate => candidate != self)
                .OrderByDescending(Wins)
                .ThenByDescending(candidate => counts[candidate])
                .ThenBy(candidate => candidate)
                .Select(candidate => EmotePathHelper.NormalizeHumanSkeletonId(candidate))];
        }

        _fallbackOrders = orders;
    }

    private ushort? TargetOf(int entry, int fileIndex)
    {
        var word = fileIndex >> 8;
        var bit = (fileIndex & 0xFF) >> 2;

        if (word >= EntryWordCount)
            return null;

        var bits = BitConverter.ToUInt64(_file, entry + EntryBitsOffset + word * 8);

        if (((bits >> bit) & 1) == 0)
            return (ushort)BitConverter.ToUInt32(_file, entry);

        var before = BitOperations.PopCount(bits & ((1UL << bit) - 1));

        for (var previous = 0; previous < word; previous++)
            before += BitOperations.PopCount(BitConverter.ToUInt64(_file, entry + EntryBitsOffset + previous * 8));

        var first = BitConverter.ToUInt16(_file, entry + 6);
        var record = _overrideBase + OverrideLength * ((fileIndex & 3) + 4 * (before + first));

        if (record < _overrideBase || record + OverrideLength > _file.Length)
            return null;

        var code = BitConverter.ToUInt32(_file, record);

        if (code >> 16 != 0 || BitConverter.ToUInt16(_file, record + 4) != BaseVariant)
            return null;

        var folder = ReadString(_file, _folderBase, BitConverter.ToUInt16(_file, record + 6));

        return string.Equals(folder, _folders[fileIndex], StringComparison.OrdinalIgnoreCase) ? (ushort)code : null;
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

    private static PapLoadTable? current;

    /// <summary> The table read from the game's files, or null while it has not been read or could not be parsed. </summary>
    public static PapLoadTable? Current => Volatile.Read(ref current);

    /// <summary>
    /// Reads the table and derives the fallback chains, off the frame thread. Until this has run,
    /// <see cref="EmotePathHelper"/> only knows each skeleton itself.
    /// </summary>
    public static void Warm()
    {
        if (Volatile.Read(ref current) != null)
            return;

        try
        {
            if (NoireService.DataManager.GetFile(GamePath)?.Data is not { Length: > 0 } bytes)
            {
                NoireLogger.LogWarning($"Failed to read '{GamePath}'. Animation paths use the drawn skeleton only", LogPrefix);
                return;
            }

            if (Parse(bytes) is not { } table)
            {
                NoireLogger.LogWarning($"Unknown layout for '{GamePath}' ({bytes.Length} bytes). Animation paths use the drawn skeleton only", LogPrefix);
                return;
            }

            table.BuildFallbackOrders(NoireService.DataManager.FileExists);

            Volatile.Write(ref current, table);
            NoireLogger.LogDebug($"Read {table.FileCount} animation files for {table.HumanSkeletons.Count} human skeletons.", LogPrefix);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to read '{GamePath}'. Animation paths use the drawn skeleton only", LogPrefix);
        }
    }
}
