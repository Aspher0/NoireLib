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
    private TConfig config = new();

    /// <summary>
    /// Links the default configuration back to this tweak. A tweak whose configuration is never deserialized, because
    /// it has no stored entry yet, still reaches persistence through <see cref="TweakConfigBase.Save"/>, which throws
    /// when the parent link is absent. The field initializer cannot reference the tweak, so the link is made here,
    /// where every other construction path (the <see cref="Config"/> setter and deserialization) also makes it.
    /// </summary>
    protected TweakBase()
    {
        config.Parent = this;
    }

    /// <summary>
    /// The typed configuration instance for this tweak.<br/>
    /// Modify properties on this object and call <see cref="TweakBase.MarkConfigDirty"/>
    /// to persist changes.
    /// The parent reference on the config is populated automatically when this property is set.
    /// </summary>
    public TConfig Config
    {
        get => config;
        internal set
        {
            config = value ?? new TConfig();
            config.Parent = this;
        }
    }

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
        Config.Parent = this;
    }
}
