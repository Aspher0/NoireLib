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
/// Base class for tweak-specific configurations. It inherits the versioning and <see cref="ConfigMigrationAttribute"/>
/// migration support of <see cref="NoireConfigBase"/> but seals every file-based operation, since only
/// <see cref="NoireTweakManager"/> persists a tweak configuration; <see cref="ToJson"/> gives a read-only snapshot.
/// </summary>
[Serializable]
public abstract class TweakConfigBase : NoireConfigBase
{
    // Reads and writes the JSON a tweak configuration is stored as. Built with Create(JsonSerializerSettings), which
    // resolves every setting from JsonSettings alone; the JsonConvert overloads and
    // CreateDefault(JsonSerializerSettings) would merge in the process-global DefaultSettings and let unrelated code
    // decide the stored format.
    private static readonly JsonSerializer TweakConfigSerializer = CreateTweakConfigSerializer();

    private static JsonSerializer CreateTweakConfigSerializer()
    {
        var serializer = JsonSerializer.Create(JsonSettings);

        // A stored tweak configuration is exactly one JSON document, so anything after it means the value is corrupt.
        // A serializer instance does not enable this rejection on its own, unlike JsonConvert.DeserializeObject.
        serializer.CheckAdditionalContent = true;
        return serializer;
    }

    /// <summary>The owning tweak instance, set by <see cref="TweakBase{TConfig}"/> when the tweak is created.</summary>
    [JsonIgnore]
    public TweakBase? Parent { get; internal set; }

    /// <inheritdoc/>
    public sealed override string GetConfigFileName() => string.Empty;

    /// <summary>Always false, since <see cref="NoireTweakManager"/> owns all persistence for a tweak configuration.</summary>
    [JsonIgnore]
    public sealed override bool LoadFromDiskOnInitialization => false;

    /// <summary>Marks this configuration dirty on the owning tweak, which asks the manager to persist it.</summary>
    /// <returns><see langword="true"/> once the parent tweak has been notified.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no parent tweak is attached to receive the save request.</exception>
    public sealed override bool Save()
    {
        if (Parent == null)
            throw new InvalidOperationException("Tweak config has no parent tweak to notify. Use the TweakManager to persist configs or ensure the config is attached to its parent tweak.");

        Parent.MarkConfigDirty();
        return true;
    }

    /// <summary>Redirects the debounced save path onto <see cref="Save"/>, since a tweak configuration is not file-backed.</summary>
    public sealed override void RequestSave() => Save();

    /// <summary>Always throws, since <see cref="NoireTweakManager"/> owns loading.</summary>
    /// <returns>This method never returns normally.</returns>
    /// <exception cref="InvalidOperationException">Always thrown.</exception>
    public sealed override bool Load()
        => throw new InvalidOperationException(
            "Tweak configs cannot be loaded directly. The TweakManager handles config loading.");

    /// <summary>Always throws, since <see cref="NoireTweakManager"/> owns deletion.</summary>
    /// <returns>This method never returns normally.</returns>
    /// <exception cref="InvalidOperationException">Always thrown.</exception>
    public sealed override bool Delete()
        => throw new InvalidOperationException(
            "Tweak configs cannot be deleted directly. The TweakManager handles config management.");

    /// <summary>Always returns false, since a tweak configuration is not file-backed.</summary>
    /// <returns><see langword="false"/>.</returns>
    public sealed override bool Exists() => false;

    /// <summary>Serializes this configuration to a read-only JSON snapshot.</summary>
    /// <returns>The JSON representation of the current configuration state.</returns>
    public string ToJson()
    {
        return SerializeTweakConfigToJson();
    }

    internal string SerializeToJson()
    {
        return SerializeTweakConfigToJson();
    }

    // Serializes this instance as its concrete type to the JSON a tweak configuration is stored as.
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

    // Deserializes stored tweak configuration JSON into a new instance of the given type.
    private static T? DeserializeTweakConfigFromJson<T>(string json) where T : TweakConfigBase
    {
        using var stringReader = new StringReader(json);
        using var jsonReader = new JsonTextReader(stringReader);

        return TweakConfigSerializer.Deserialize<T>(jsonReader);
    }

    // Deserializes a tweak configuration from JSON, running any migrations the stored version needs first.
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

    // Extracts the stored version number from a configuration JSON string.
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
