using NoireLib.Configuration;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.TweakManager;

/// <summary>
/// Configuration storage for the <see cref="NoireTweakManager"/> module.<br/>
/// Persists tweak enabled states, serialized per-tweak configs, and key migration mappings.
/// </summary>
[NoireConfig("TweakManagerConfig")]
public class TweakManagerConfigInstance : NoireConfigBase
{
    /// <inheritdoc/>
    public override int Version { get; set; } = 1;

    /// <inheritdoc/>
    public override string GetConfigFileName() => "TweakManagerConfig";

    /// <summary>
    /// Dictionary mapping tweak <see cref="TweakBase.InternalKey"/> to its persisted configuration.
    /// </summary>
    [AutoSave]
    public Dictionary<string, TweakConfigEntry> TweakConfigs { get; set; } = new();

    /// <summary>
    /// The list of tweak internal keys favorited by the user.
    /// </summary>
    [AutoSave]
    public HashSet<string> FavoriteTweaks { get; set; } = [];

    /// <summary>
    /// Dictionary mapping old tweak keys to new tweak keys for migration purposes.<br/>
    /// When a tweak's <see cref="TweakBase.InternalKey"/> changes, add the old key as the dictionary key
    /// and the new key as the value to preserve configuration data.
    /// </summary>
    [AutoSave]
    public Dictionary<string, string> KeyMigrations { get; set; } = new();

    /// <summary>
    /// Gets the configuration entry for a specific tweak by its internal key.
    /// </summary>
    /// <param name="internalKey">The tweak's internal key.</param>
    /// <returns>The <see cref="TweakConfigEntry"/> if found; otherwise, <see langword="null"/>.</returns>
    public TweakConfigEntry? GetTweakConfig(string internalKey)
    {
        return TweakConfigs.GetValueOrDefault(internalKey);
    }

    /// <summary>
    /// Determines whether a tweak is marked as a favorite.
    /// </summary>
    /// <param name="internalKey">The tweak internal key.</param>
    /// <returns><see langword="true"/> if the tweak is favorited; otherwise, <see langword="false"/>.</returns>
    public bool IsFavorite(string internalKey)
    {
        return FavoriteTweaks.Contains(internalKey);
    }

    /// <summary>
    /// Sets the favorite state for a tweak.
    /// </summary>
    /// <param name="internalKey">The tweak internal key.</param>
    /// <param name="isFavorite">Whether the tweak should be favorited.</param>
    [AutoSave]
    public void SetFavorite(string internalKey, bool isFavorite)
    {
        if (isFavorite)
            FavoriteTweaks.Add(internalKey);
        else
            FavoriteTweaks.Remove(internalKey);
    }

    /// <summary>
    /// Gets all favorite tweak keys.
    /// </summary>
    /// <returns>A read-only list of favorited tweak internal keys.</returns>
    public IReadOnlyList<string> GetFavoriteTweaks()
    {
        return FavoriteTweaks.ToList();
    }

    /// <summary>
    /// Sets or updates the configuration entry for a specific tweak.
    /// </summary>
    /// <param name="internalKey">The tweak's internal key.</param>
    /// <param name="entry">The configuration entry to set.</param>
    [AutoSave]
    public void SetTweakConfig(string internalKey, TweakConfigEntry entry)
    {
        TweakConfigs[internalKey] = entry;
    }

    /// <summary>
    /// Removes a tweak's configuration entry by its internal key.
    /// </summary>
    /// <param name="internalKey">The tweak's internal key.</param>
    /// <returns><see langword="true"/> if the entry was removed; otherwise, <see langword="false"/>.</returns>
    [AutoSave]
    public bool RemoveTweakConfig(string internalKey)
    {
        return TweakConfigs.Remove(internalKey);
    }

    /// <summary>
    /// Executes key migrations, moving configuration entries from old keys to new keys.<br/>
    /// This ensures no data is lost when a tweak's <see cref="TweakBase.InternalKey"/> is changed.
    /// </summary>
    /// <returns>The number of migrations executed.</returns>
    [AutoSave]
    public int ExecuteKeyMigrations()
    {
        int migratedCount = 0;
        var completedMigrations = new List<string>();

        foreach (var (oldKey, newKey) in KeyMigrations)
        {
            if (TweakConfigs.ContainsKey(oldKey) && !TweakConfigs.ContainsKey(newKey))
            {
                TweakConfigs[newKey] = TweakConfigs[oldKey];
                TweakConfigs.Remove(oldKey);
                completedMigrations.Add(oldKey);
                migratedCount++;

                NoireLogger.LogInfo<TweakManagerConfigInstance>(
                    $"Migrated tweak config from key '{oldKey}' to '{newKey}'.");
            }
        }

        foreach (var completedKey in completedMigrations)
            KeyMigrations.Remove(completedKey);

        return migratedCount;
    }

    /// <summary>
    /// Registers a key migration mapping from an old key to a new key.
    /// </summary>
    /// <param name="oldKey">The old tweak internal key.</param>
    /// <param name="newKey">The new tweak internal key.</param>
    [AutoSave]
    public void AddKeyMigration(string oldKey, string newKey)
    {
        KeyMigrations[oldKey] = newKey;
    }

    /// <summary>
    /// Gets all currently persisted tweak keys.
    /// </summary>
    /// <returns>A read-only list of tweak internal keys.</returns>
    public IReadOnlyList<string> GetAllTweakKeys()
    {
        return TweakConfigs.Keys.ToList();
    }

    /// <summary>
    /// Clears all tweak configurations.
    /// </summary>
    [AutoSave]
    public void ClearAllTweakConfigs()
    {
        TweakConfigs.Clear();
    }
}
