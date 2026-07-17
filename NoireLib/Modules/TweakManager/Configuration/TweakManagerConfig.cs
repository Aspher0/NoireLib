using NoireLib.Configuration;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.TweakManager;

/// <summary>
/// Configuration storage for the <see cref="NoireTweakManager"/> module.<br/>
/// Persists tweak enabled states, serialized per-tweak configs, favorites, and key migration mappings.
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
    /// and the new key as the value to preserve everything the old key holds, which is the entry in
    /// <see cref="TweakConfigs"/> and the <see cref="FavoriteTweaks"/> membership.
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
    /// Moves everything a tweak has persisted under <paramref name="oldKey"/> to <paramref name="newKey"/>.<br/>
    /// This covers the <see cref="TweakConfigs"/> entry and the <see cref="FavoriteTweaks"/> membership, which are
    /// keyed by the same <see cref="TweakBase.InternalKey"/> and describe one tweak between them. They move together
    /// or not at all, so a rename can never strand a favorite on a key no tweak answers to any more.<br/>
    /// Nothing moves when the new key already holds data of its own, because that data belongs to a tweak that is
    /// already using the new key and must not be overwritten by a leftover.
    /// </summary>
    /// <param name="oldKey">The internal key the data is currently stored under.</param>
    /// <param name="newKey">The internal key the data should be stored under.</param>
    /// <returns><see langword="true"/> if data was moved; otherwise, <see langword="false"/>.</returns>
    [AutoSave]
    public bool MigrateTweakKey(string oldKey, string newKey)
    {
        if (oldKey == newKey)
            return false;

        var oldEntry = TweakConfigs.GetValueOrDefault(oldKey);
        var oldIsFavorite = FavoriteTweaks.Contains(oldKey);

        if (oldEntry == null && !oldIsFavorite)
            return false;

        if (TweakConfigs.ContainsKey(newKey) || FavoriteTweaks.Contains(newKey))
            return false;

        if (oldEntry != null)
        {
            TweakConfigs[newKey] = oldEntry;
            TweakConfigs.Remove(oldKey);
        }

        if (oldIsFavorite)
        {
            FavoriteTweaks.Add(newKey);
            FavoriteTweaks.Remove(oldKey);
        }

        return true;
    }

    /// <summary>
    /// Executes key migrations, moving persisted tweak data from old keys to new keys.<br/>
    /// This ensures no data is lost when a tweak's <see cref="TweakBase.InternalKey"/> is changed.<br/>
    /// A mapping whose old key holds nothing to move is kept, so it still applies if that data appears later.
    /// </summary>
    /// <returns>The number of migrations executed.</returns>
    [AutoSave]
    public int ExecuteKeyMigrations()
    {
        int migratedCount = 0;
        var completedMigrations = new List<string>();

        foreach (var (oldKey, newKey) in KeyMigrations)
        {
            if (MigrateTweakKey(oldKey, newKey))
            {
                completedMigrations.Add(oldKey);
                migratedCount++;

                NoireLogger.LogInfo<TweakManagerConfigInstance>(
                    $"Migrated tweak data from key '{oldKey}' to '{newKey}'.");
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
