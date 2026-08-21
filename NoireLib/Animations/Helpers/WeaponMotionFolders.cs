using Dalamud.Utility;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace NoireLib.Animations.Helpers;

/// <summary>
/// The per-weapon animation folders battle motions are served from, and the order to try them in when a
/// character's own folder holds no copy of the animation being asked for. Nothing here reads character state.
/// </summary>
public static class WeaponMotionFolders
{
    /// <summary> Every per-weapon folder name starts with this. </summary>
    public const string FolderPrefix = "bt_";

    /// <summary>
    /// The folder to fall back to when nothing closer serves an animation: the only one carrying every
    /// battle-motion key on c0101, where every skeleton chain ends.
    /// </summary>
    public const string ReferenceFolder = "bt_swd_sld";

    /// <summary> The value the game's tables use for an empty slot. </summary>
    private const string EmptyMarker = "*";

    private const int CodeLength = 3;

    /// <summary> The folder two motion codes name, such as "swd" and "sld" naming "bt_swd_sld". </summary>
    /// <param name="mainCode">The main hand's motion code.</param>
    /// <param name="offCode">The off hand's motion code.</param>
    /// <returns>The folder name.</returns>
    public static string Compose(string mainCode, string offCode)
        => FolderPrefix + mainCode + "_" + offCode;

    /// <summary> The main hand's code inside a folder name, or null when the name is not one of ours. </summary>
    /// <param name="folder">The folder name.</param>
    /// <returns>The three-letter code, or null.</returns>
    public static string? MainCodeOf(string? folder)
        => folder != null && folder.Length == FolderPrefix.Length + CodeLength * 2 + 1
            && folder.StartsWith(FolderPrefix, StringComparison.Ordinal)
            ? folder.Substring(FolderPrefix.Length, CodeLength)
            : null;

    /// <summary>
    /// The folders to try for a character whose own folder is <paramref name="ownFolder"/>, closest first: the
    /// folder itself, then the folders sharing its main hand, then the folders the game groups with it.
    /// </summary>
    /// <param name="ownFolder">The character's own folder, or null when it could not be read.</param>
    /// <param name="groupedFolders">The game's folder groups, one list per group, empty entries already dropped.</param>
    /// <returns>The folders to try, most specific first, without repeats.</returns>
    public static IReadOnlyList<string> LadderFrom(string? ownFolder,
        IReadOnlyList<IReadOnlyList<string>> groupedFolders)
    {
        var ladder = new List<string>();

        if (string.IsNullOrEmpty(ownFolder))
            return ladder;

        ladder.Add(ownFolder);

        if (groupedFolders == null)
            return ladder;

        // A folder built from the same weapon is the closest stand-in, so those come before the game's groups.
        if (MainCodeOf(ownFolder) is { } mainCode)
        {
            foreach (var group in groupedFolders)
            {
                foreach (var folder in group)
                {
                    if (string.Equals(MainCodeOf(folder), mainCode, StringComparison.Ordinal)
                        && !ladder.Contains(folder, StringComparer.Ordinal))
                    {
                        ladder.Add(folder);
                    }
                }
            }
        }

        // Then whatever the game itself lists alongside this folder, which pairs a weapon with its off-hands.
        foreach (var group in groupedFolders)
        {
            if (!group.Contains(ownFolder, StringComparer.Ordinal))
                continue;

            foreach (var folder in group)
            {
                if (!ladder.Contains(folder, StringComparer.Ordinal))
                    ladder.Add(folder);
            }
        }

        return ladder;
    }

    /// <summary> The folders to try for a character's own folder, against the game's own folder groups. </summary>
    /// <param name="ownFolder">The character's own folder, or null when it could not be read.</param>
    /// <returns>The folders to try, most specific first.</returns>
    public static IReadOnlyList<string> LadderFor(string? ownFolder)
        => LadderFrom(ownFolder, GroupedFolders());

    private static IReadOnlyList<IReadOnlyList<string>>? groupedFolders;

    /// <summary>
    /// The folder groups the game ships, read once. Racing callers build the same lists from an immutable
    /// sheet, so a loser's copy is wasted rather than wrong.
    /// </summary>
    /// <returns>One list per group, in sheet order, with empty entries dropped.</returns>
    public static IReadOnlyList<IReadOnlyList<string>> GroupedFolders()
    {
        var built = Volatile.Read(ref groupedFolders);
        if (built != null)
            return built;

        built = ReadGroupedFolders();
        Volatile.Write(ref groupedFolders, built);

        return built;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadGroupedFolders()
    {
        var groups = new List<IReadOnlyList<string>>();

        try
        {
            var sheet = ExcelSheetHelper.GetSheet<ResidentMotionType>();
            if (sheet == null)
                return groups;

            // The sheet's columns carry no names of their own, so they are read positionally.
            var columns = typeof(ResidentMotionType)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.PropertyType == typeof(ReadOnlySeString))
                .ToList();

            foreach (var row in sheet)
            {
                var folders = new List<string>();

                foreach (var column in columns)
                {
                    if (column.GetValue(row) is not ReadOnlySeString value)
                        continue;

                    var folder = value.ExtractText();

                    if (!string.IsNullOrEmpty(folder) && folder != EmptyMarker
                        && folder.StartsWith(FolderPrefix, StringComparison.Ordinal)
                        && !folders.Contains(folder, StringComparer.Ordinal))
                    {
                        folders.Add(folder);
                    }
                }

                if (folders.Count > 0)
                    groups.Add(folders);
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Could not read the weapon motion folder groups; the ladder is left bare.",
                "[WeaponMotionFolders] ");
        }

        return groups;
    }
}
