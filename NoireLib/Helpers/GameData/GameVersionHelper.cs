using System;
using System.Linq;
namespace NoireLib.Helpers;

/// <summary>
/// Reads the installed client's build, the key anything caching derived game data must stamp its stored copies
/// with so a patch invalidates them.
/// </summary>
public static class GameVersionHelper
{
    // The base game's repository.
    private const string BaseRepositoryKey = "ffxiv";

    // bgcommon holds the shared models and bg holds the zones.
    private static readonly string[] GeometryCategories = ["01", "02"];

    /// <summary>
    /// An identity for the files navmesh geometry is built from: the length and write time of every bgcommon and bg index.<br/>
    /// A patch that leaves the ground alone keeps it.
    /// </summary>
    /// <param name="fallback">Returned when the files cannot be read, such as before the data manager is up.</param>
    /// <returns>A short identity string, or <paramref name="fallback"/>.</returns>
    public static string GeometryBuild(string fallback = "")
    {
        if (!NoireService.IsInitialized())
            return fallback;

        return SafeExecutor.ExecuteSafely(() =>
        {
            var root = NoireService.DataManager.GameData.DataPath;
            if (root is not { Exists: true })
                return fallback;

            // FNV-1a over the files in a fixed order.
            var hash = 0xCBF29CE484222325UL;

            void Mix(ulong value)
            {
                hash ^= value;
                hash *= 0x100000001B3UL;
            }

            var seen = 0;
            foreach (var repository in root.GetDirectories().OrderBy(directory => directory.Name, StringComparer.Ordinal))
            {
                foreach (var index in repository.GetFiles("*.win32.index").OrderBy(file => file.Name, StringComparer.Ordinal))
                {
                    if (index.Name.Length < 2 || !GeometryCategories.Contains(index.Name[..2]))
                        continue;

                    seen++;
                    foreach (var character in repository.Name)
                        Mix(character);

                    foreach (var character in index.Name)
                        Mix(character);

                    Mix((ulong)index.Length);
                    Mix((ulong)index.LastWriteTimeUtc.Ticks);
                }
            }

            return seen == 0 ? fallback : "geo-" + hash.ToString("X16");
        }, fallback) ?? fallback;
    }

    /// <summary>Reads the base game repository's version string.</summary>
    /// <param name="fallback">What to return when the version cannot be read, such as before the data manager is up.</param>
    /// <returns>The version string, or <paramref name="fallback"/>.</returns>
    public static string CurrentGameVersion(string fallback = "")
    {
        if (!NoireService.IsInitialized())
            return fallback;

        return SafeExecutor.ExecuteSafely(() =>
        {
            var repositories = NoireService.DataManager.GameData.Repositories;

            return repositories.TryGetValue(BaseRepositoryKey, out var repository)
                ? repository.Version
                : fallback;
        }, fallback) ?? fallback;
    }
}
