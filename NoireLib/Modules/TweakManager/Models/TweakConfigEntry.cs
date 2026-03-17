using Newtonsoft.Json;

namespace NoireLib.TweakManager;

/// <summary>
/// Represents the persisted configuration for a single tweak.<br/>
/// Stores the enabled state, serialized config JSON, and config schema version.
/// </summary>
public class TweakConfigEntry
{
    /// <summary>
    /// Whether the tweak is enabled by the user.
    /// </summary>
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// The serialized JSON of the tweak's <see cref="TweakConfigBase"/> instance.<br/>
    /// <see langword="null"/> if the tweak has no configuration or uses default values.
    /// </summary>
    [JsonProperty("configJson")]
    public string? ConfigJson { get; set; }

    /// <summary>
    /// The schema version of the stored <see cref="ConfigJson"/>.<br/>
    /// Used by <see cref="TweakConfigBase"/> migration support to determine
    /// whether migrations are needed during deserialization.
    /// </summary>
    [JsonProperty("configVersion")]
    public int ConfigVersion { get; set; }

    /// <summary>
    /// Creates a new <see cref="TweakConfigEntry"/> with default values.
    /// </summary>
    public TweakConfigEntry() { }

    /// <summary>
    /// Creates a new <see cref="TweakConfigEntry"/> with the specified enabled state.
    /// </summary>
    /// <param name="enabled">Whether the tweak is enabled.</param>
    public TweakConfigEntry(bool enabled)
    {
        Enabled = enabled;
    }

    /// <summary>
    /// Creates a new <see cref="TweakConfigEntry"/> with the specified enabled state, config JSON, and config version.
    /// </summary>
    /// <param name="enabled">Whether the tweak is enabled.</param>
    /// <param name="configJson">The serialized config JSON string.</param>
    /// <param name="configVersion">The schema version of the config.</param>
    public TweakConfigEntry(bool enabled, string? configJson, int configVersion)
    {
        Enabled = enabled;
        ConfigJson = configJson;
        ConfigVersion = configVersion;
    }
}
