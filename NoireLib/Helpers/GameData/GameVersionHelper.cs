namespace NoireLib.Helpers;

/// <summary>
/// Reads the installed client's build, the key anything caching derived game data must stamp its stored copies
/// with so a patch invalidates them.
/// </summary>
public static class GameVersionHelper
{
    /// <summary> The repository holding the base game's own files, as opposed to an expansion's. </summary>
    private const string BaseRepositoryKey = "ffxiv";

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
