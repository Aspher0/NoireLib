using System;

namespace NoireLib.TweakManager;

/// <summary>
/// Abstract base class for tweaks that have a typed configuration class.<br/>
/// The <typeparamref name="TConfig"/> is automatically created, serialized, deserialized,
/// and migrated by the <see cref="NoireTweakManager"/>.<br/>
/// </summary>
/// <typeparam name="TConfig">
/// The configuration type, which must extend <see cref="TweakConfigBase"/>.
/// </typeparam>
public abstract class TweakBase<TConfig> : TweakBase where TConfig : TweakConfigBase, new()
{
    /// <summary>
    /// The typed configuration instance for this tweak.<br/>
    /// Modify properties on this object and call <see cref="TweakBase.MarkConfigDirty"/>
    /// to persist changes.
    /// </summary>
    public TConfig Config { get; internal set; } = new();

    /// <inheritdoc/>
    public sealed override bool HasConfig => true;

    /// <inheritdoc/>
    internal sealed override Type? GetConfigType() => typeof(TConfig);

    /// <inheritdoc/>
    internal sealed override TweakConfigBase? GetConfigInstance() => Config;

    /// <inheritdoc/>
    internal sealed override string? SerializeConfig()
    {
        return Config.SerializeToJson();
    }

    /// <inheritdoc/>
    internal sealed override void DeserializeConfig(string? json, int storedVersion)
    {
        Config = TweakConfigBase.DeserializeFromJson<TConfig>(json, storedVersion);
    }
}
