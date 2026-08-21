using System;
using System.Threading;

namespace NoireLib.Animations.Helpers;

/// <summary>
/// The game's weapon-to-motion table, naming the three-letter motion code a weapon model set animates with.
/// Two of those codes compose the folder a battle motion is served from, through
/// <see cref="WeaponMotionFolders.Compose"/>. Nothing here reads character state.
/// </summary>
public sealed class WeaponMotionTable
{
    private const string LogPrefix = "[WeaponMotionTable] ";

    /// <summary> The game file the table is read from. </summary>
    public const string GamePath = "chara/xls/weapontype/motion.wtd";

    /// <summary> The code a hand with no weapon animates with. </summary>
    public const string EmptyCode = "emp";

    /// <summary> The only file layout this reader understands. </summary>
    private const ushort SupportedVersion = 1;

    private const int HeaderLength = 4;
    private const int EntryLength = 8;

    private readonly uint[] _setIds;
    private readonly string[] _codes;

    private WeaponMotionTable(uint[] setIds, string[] codes)
    {
        _setIds = setIds;
        _codes = codes;
    }

    /// <summary> How many entries the table holds. </summary>
    public int Count => _setIds.Length;

    /// <summary>
    /// Parses the table's bytes. The layout is a ushort version, a ushort entry count, then that many
    /// (uint weapon model set id, uint packed code) pairs in ascending set-id order, the code being three
    /// ASCII characters in the packed value's low 24 bits, most significant first.
    /// </summary>
    /// <param name="file">The file's bytes.</param>
    /// <returns>The parsed table, or null when the bytes are too short, of an unknown version, or not in
    /// ascending set-id order.</returns>
    public static WeaponMotionTable? Parse(ReadOnlySpan<byte> file)
    {
        if (file.Length < HeaderLength)
            return null;

        var version = BitConverter.ToUInt16(file[..2]);
        var count = BitConverter.ToUInt16(file.Slice(2, 2));

        if (version != SupportedVersion || count == 0 || file.Length < HeaderLength + count * EntryLength)
            return null;

        var setIds = new uint[count];
        var codes = new string[count];

        for (var index = 0; index < count; index++)
        {
            var entry = file.Slice(HeaderLength + index * EntryLength, EntryLength);

            setIds[index] = BitConverter.ToUInt32(entry[..4]);
            codes[index] = CodeOf(BitConverter.ToUInt32(entry.Slice(4, 4)));

            if (index > 0 && setIds[index] <= setIds[index - 1])
                return null;
        }

        return new WeaponMotionTable(setIds, codes);
    }

    /// <summary>
    /// The motion code a weapon model set animates with, taken from the highest entry the set id reaches. A
    /// set id below the first entry takes that entry's code, as the game's own lookup does.
    /// </summary>
    /// <param name="weaponModelSetId">The weapon model set id, or 0 for a hand holding nothing.</param>
    /// <returns>The three-letter code.</returns>
    public string CodeFor(ushort weaponModelSetId)
    {
        if (weaponModelSetId == 0)
            return EmptyCode;

        var low = 0;
        var high = _setIds.Length - 1;
        var found = 0;

        while (low <= high)
        {
            var middle = low + (high - low) / 2;

            if (_setIds[middle] <= weaponModelSetId)
            {
                found = middle;
                low = middle + 1;
                continue;
            }

            high = middle - 1;
        }

        return _codes[found];
    }

    private static string CodeOf(uint packed)
        => new([(char)((packed >> 16) & 0xFF), (char)((packed >> 8) & 0xFF), (char)(packed & 0xFF)]);

    private static WeaponMotionTable? current;

    /// <summary> The table read from the game's files, or null while it has not been read or could not be parsed. </summary>
    public static WeaponMotionTable? Current => Volatile.Read(ref current);

    /// <summary>
    /// Reads the table from the game's files, off the frame thread. Racing callers build the same table from an
    /// immutable file, so a loser's copy is wasted rather than wrong.
    /// </summary>
    public static void Warm()
    {
        if (Volatile.Read(ref current) != null)
            return;

        try
        {
            if (!NoireService.DataManager.FileExists(GamePath))
            {
                NoireLogger.LogDebug($"'{GamePath}' is not in the game's files; weapon motions stay unreadable.", LogPrefix);
                return;
            }

            if (NoireService.DataManager.GetFile(GamePath)?.Data is not { Length: > 0 } bytes)
            {
                NoireLogger.LogDebug($"'{GamePath}' read back empty; weapon motions stay unreadable.", LogPrefix);
                return;
            }

            if (Parse(bytes) is not { } table)
            {
                NoireLogger.LogDebug($"'{GamePath}' is not in a layout this reads ({bytes.Length} bytes); "
                    + "weapon motions stay unreadable.", LogPrefix);
                return;
            }

            Volatile.Write(ref current, table);
            NoireLogger.LogDebug($"Read {table.Count} weapon motion entries.", LogPrefix);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Could not read '{GamePath}'; weapon motions stay unreadable.", LogPrefix);
        }
    }
}
