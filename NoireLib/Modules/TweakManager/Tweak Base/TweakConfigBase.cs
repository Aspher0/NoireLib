using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NoireLib.Configuration;
using NoireLib.Configuration.Migrations;
using System;
using System.Globalization;
using System.IO;
using System.Text;

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
    /// <summary>
    /// Reads and writes the JSON a tweak configuration is stored as. It is built with
    /// <see cref="JsonSerializer.Create(JsonSerializerSettings)"/>, which resolves every setting from
    /// <see cref="NoireConfigBase.JsonSettings"/> alone. The <see cref="JsonConvert"/> overloads and
    /// <see cref="JsonSerializer.CreateDefault(JsonSerializerSettings)"/> instead merge in
    /// <see cref="JsonConvert.DefaultSettings"/>, a process-global that any other code loaded into this process can
    /// assign, and only overlay the properties the given settings object actually sets. Anything the settings leave
    /// unmentioned, such as the converters, the contract resolver or the null handling, would then be decided by
    /// unrelated code and the stored format would drift with it.
    /// </summary>
    private static readonly JsonSerializer TweakConfigSerializer = CreateTweakConfigSerializer();

    private static JsonSerializer CreateTweakConfigSerializer()
    {
        var serializer = JsonSerializer.Create(JsonSettings);

        // A stored tweak configuration is exactly one JSON document, so anything after it means the stored value is
        // corrupt. JsonConvert.DeserializeObject turns this on for its callers implicitly; setting it explicitly keeps
        // that rejection in place now that the read below goes through a serializer instance instead.
        serializer.CheckAdditionalContent = true;
        return serializer;
    }

    /// <summary>
    /// The owning tweak instance for this configuration, if any.
    /// This is populated automatically by <see cref="TweakBase{TConfig}"/> when the tweak is created.
    /// </summary>
    [JsonIgnore]
    public TweakBase? Parent { get; internal set; }

    /// <inheritdoc/>
    public sealed override string GetConfigFileName() => string.Empty;

    /// <summary>
    /// Tweak configs are not loaded from disk on initialization.<br/>
    /// The <see cref="NoireTweakManager"/> manages all persistence.
    /// </summary>
    [JsonIgnore]
    public sealed override bool LoadFromDiskOnInitialization => false;

    /// <summary>
    /// Signals that this tweak configuration has changed and requests the owning tweak to persist the change.
    /// When a <see cref="TweakBase"/> parent is attached, this method will call <see cref="TweakBase.MarkConfigDirty"/>
    /// on the parent which in turn asks the manager to save the configuration.
    /// </summary>
    /// <returns><see langword="true"/> when the parent tweak was notified; otherwise, this method throws when no parent is attached.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no parent tweak is attached to receive the save request.</exception>
    public sealed override bool Save()
    {
        if (Parent == null)
            throw new InvalidOperationException("Tweak config has no parent tweak to notify. Use the TweakManager to persist configs or ensure the config is attached to its parent tweak.");

        Parent.MarkConfigDirty();
        return true;
    }

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
        return SerializeTweakConfigToJson();
    }

    /// <summary>
    /// Serializes this configuration to a JSON string for internal storage by the manager.
    /// </summary>
    /// <returns>A JSON string representing the current configuration.</returns>
    internal string SerializeToJson()
    {
        return SerializeTweakConfigToJson();
    }

    /// <summary>
    /// Serializes this instance to the JSON a tweak configuration is stored as.
    /// </summary>
    /// <returns>The JSON representation of this instance, serialized as its concrete type.</returns>
    private string SerializeTweakConfigToJson()
    {
        var builder = new StringBuilder(256);

        using (var stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture))
        using (var jsonWriter = new JsonTextWriter(stringWriter))
        {
            TweakConfigSerializer.Serialize(jsonWriter, this, GetType());
        }

        return builder.ToString();
    }

    /// <summary>
    /// Deserializes stored tweak configuration JSON into a new instance of the given type.
    /// </summary>
    /// <typeparam name="T">The concrete tweak configuration type to materialize.</typeparam>
    /// <param name="json">The stored JSON.</param>
    /// <returns>The deserialized instance, or null when the JSON holds a bare null.</returns>
    /// <exception cref="JsonException">The JSON is malformed or carries content after the configuration object.</exception>
    private static T? DeserializeTweakConfigFromJson<T>(string json) where T : TweakConfigBase
    {
        using var stringReader = new StringReader(json);
        using var jsonReader = new JsonTextReader(stringReader);

        return TweakConfigSerializer.Deserialize<T>(jsonReader);
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

            var deserialized = DeserializeTweakConfigFromJson<T>(json);
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
    /// <returns>The version number if found; otherwise, 0.</returns>
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
