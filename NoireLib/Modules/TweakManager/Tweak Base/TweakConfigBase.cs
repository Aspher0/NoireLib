using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NoireLib.Configuration;
using NoireLib.Configuration.Migrations;
using System;

namespace NoireLib.TweakManager;

/// <summary>
/// Base class for tweak-specific configurations.<br/>
/// Extends <see cref="NoireConfigBase"/> to inherit versioning and migration support
/// via <see cref="ConfigMigrationAttribute"/>, but seals all file-based operations.<br/>
/// Only the <see cref="NoireTweakManager"/> controls persistence.
/// Tweaks and consumers cannot save or load config files directly.<br/>
/// Use <see cref="ToJson"/> to get a read-only JSON snapshot of the current configuration.
/// </summary>
[Serializable]
public abstract class TweakConfigBase : NoireConfigBase
{
    /// <inheritdoc/>
    public sealed override string GetConfigFileName() => string.Empty;

    /// <summary>
    /// Tweak configs are not loaded from disk on initialization.<br/>
    /// The <see cref="NoireTweakManager"/> manages all persistence.
    /// </summary>
    [JsonIgnore]
    public sealed override bool LoadFromDiskOnInitialization => false;

    /// <summary>
    /// Saving is managed exclusively by <see cref="NoireTweakManager"/>.<br/>
    /// Calling this method directly is not supported and will throw <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <returns>This method never returns normally.</returns>
    /// <exception cref="InvalidOperationException">Always thrown. Use <see cref="TweakBase.MarkConfigDirty"/> instead.</exception>
    public sealed override bool Save()
        => throw new InvalidOperationException(
            "Tweak configs cannot be saved directly. Use MarkConfigDirty() on the tweak instead.");

    /// <summary>
    /// Loading is managed exclusively by <see cref="NoireTweakManager"/>.<br/>
    /// Calling this method directly is not supported and will throw <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <returns>This method never returns normally.</returns>
    /// <exception cref="InvalidOperationException">Always thrown.</exception>
    public sealed override bool Load()
        => throw new InvalidOperationException(
            "Tweak configs cannot be loaded directly. The TweakManager handles config loading.");

    /// <summary>
    /// Deletion is managed exclusively by <see cref="NoireTweakManager"/>.<br/>
    /// Calling this method directly is not supported and will throw <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <returns>This method never returns normally.</returns>
    /// <exception cref="InvalidOperationException">Always thrown.</exception>
    public sealed override bool Delete()
        => throw new InvalidOperationException(
            "Tweak configs cannot be deleted directly. The TweakManager handles config management.");

    /// <summary>
    /// Tweak configs are not file-backed. Always returns <see langword="false"/>.
    /// </summary>
    /// <returns><see langword="false"/> always.</returns>
    public sealed override bool Exists() => false;

    /// <summary>
    /// Returns a read-only JSON snapshot of this configuration.<br/>
    /// Consumers may use this for display, export, or custom persistence logic.
    /// </summary>
    /// <returns>A JSON string representing the current configuration state.</returns>
    public string ToJson()
    {
        return JsonConvert.SerializeObject(this, GetType(), JsonSettings);
    }

    /// <summary>
    /// Serializes this configuration to a JSON string for internal storage by the manager.
    /// </summary>
    /// <returns>A JSON string representing the current configuration.</returns>
    internal string SerializeToJson()
    {
        return JsonConvert.SerializeObject(this, GetType(), JsonSettings);
    }

    /// <summary>
    /// Deserializes a tweak config from JSON, executing any necessary migrations
    /// if the stored version is older than the target version.
    /// </summary>
    /// <typeparam name="T">The tweak config type to deserialize.</typeparam>
    /// <param name="json">The JSON string to deserialize from. If <see langword="null"/> or empty, a default instance is returned.</param>
    /// <param name="storedVersion">The version of the stored JSON data.</param>
    /// <returns>A deserialized and optionally migrated config instance, or a new default instance if deserialization fails.</returns>
    internal static T DeserializeFromJson<T>(string? json, int storedVersion) where T : TweakConfigBase, new()
    {
        if (string.IsNullOrEmpty(json))
            return new T();

        try
        {
            var targetVersion = new T().Version;

            if (storedVersion < targetVersion)
            {
                var migratedJson = MigrationExecutor.ExecuteMigrations(typeof(T), json, storedVersion, targetVersion);
                if (migratedJson != null)
                    json = migratedJson;
            }

            var deserialized = JsonConvert.DeserializeObject<T>(json, JsonSettings);
            return deserialized ?? new T();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<TweakConfigBase>(ex, $"Failed to deserialize tweak config of type {typeof(T).Name}. Using default values.");
            return new T();
        }
    }

    /// <summary>
    /// Extracts the version number from a JSON string.
    /// </summary>
    /// <param name="json">The JSON string to extract the version from.</param>
    /// <returns>The version number if found; otherwise, <c>0</c>.</returns>
    internal static int ExtractVersionFromJson(string json)
    {
        try
        {
            var obj = JObject.Parse(json);
            return obj["Version"]?.Value<int>() ?? 0;
        }
        catch
        {
            return 0;
        }
    }
}
