using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace NoireLib.TweakManager;

/// <summary>
/// Abstract base class for all tweaks managed by the <see cref="NoireTweakManager"/>, with its own lifecycle
/// management, error tracking, and optional UI. For tweaks that require persistent configuration, use
/// <see cref="TweakBase{TConfig}"/> instead.
/// </summary>
public abstract class TweakBase : IDisposable
{
    private bool disposed;
    private bool enabled;
    private Exception? lastError;
    private bool hasError;

    /// <summary>
    /// The unique internal key used to identify this tweak in configuration and persistence; must be unique
    /// across all tweaks in a <see cref="NoireTweakManager"/> instance.
    /// </summary>
    public abstract string InternalKey { get; }

    /// <summary>
    /// The display name of the tweak shown in the UI.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// A description of what this tweak does, shown in the details panel.
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// Whether this tweak should be visible in the tweak list UI; return <see langword="false"/> to hide it from the user entirely.
    /// </summary>
    public virtual bool ShouldShow => true;

    /// <summary>
    /// Optional tags for categorization or filtering in the UI.
    /// </summary>
    public virtual IReadOnlyList<string> Tags => [];

    /// <summary>
    /// Whether this tweak is currently enabled by the user.
    /// </summary>
    [JsonIgnore]
    public bool Enabled
    {
        get => enabled;
        internal set => enabled = value;
    }

    /// <summary>
    /// Whether this tweak is globally disabled via <see cref="TweakDisabledAttribute"/>; when true, it cannot be
    /// enabled. Populated automatically by the <see cref="NoireTweakManager"/> during registration.
    /// </summary>
    [JsonIgnore]
    public bool IsGloballyDisabled { get; internal set; }

    /// <summary>
    /// Whether this globally-disabled tweak should still be visible in the tweak list: true shows it with a red
    /// name and a tooltip explaining the reason, non-interactive. Populated from
    /// <see cref="TweakDisabledAttribute.ShowInList"/> during registration.
    /// </summary>
    [JsonIgnore]
    public bool ShowWhenDisabled { get; internal set; }

    /// <summary>
    /// The reason why this tweak is globally disabled, if any; populated from <see cref="TweakDisabledAttribute.Reason"/> during registration.
    /// </summary>
    [JsonIgnore]
    public string? GloballyDisabledReason { get; internal set; }

    /// <summary>
    /// Whether this tweak is currently in an error state.
    /// </summary>
    [JsonIgnore]
    public bool HasError
    {
        get => hasError;
        internal set => hasError = value;
    }

    /// <summary>
    /// The last exception that occurred during tweak operation, if any.
    /// </summary>
    [JsonIgnore]
    public Exception? LastError
    {
        get => lastError;
        internal set => lastError = value;
    }

    /// <summary>
    /// The parent <see cref="NoireTweakManager"/> that manages this tweak.
    /// </summary>
    [JsonIgnore]
    public NoireTweakManager? Manager { get; internal set; }

    /// <summary>
    /// Whether this tweak has a typed configuration class derived from <see cref="TweakConfigBase"/>: true for
    /// <see cref="TweakBase{TConfig}"/> instances, false otherwise.
    /// </summary>
    [JsonIgnore]
    public virtual bool HasConfig => false;

    /// <summary>
    /// Whether this tweak exposes a configuration UI in the tweak manager details panel.
    /// </summary>
    [JsonIgnore]
    public virtual bool HasConfigurationUi => HasConfig;

    /// <summary>
    /// Called when the tweak is enabled by the user or automatically on startup, to set up hooks, register event
    /// listeners, subscribe to addon events, etc.
    /// </summary>
    protected abstract void OnEnable();

    /// <summary>
    /// Called when the tweak is disabled by the user or on shutdown, to remove hooks, unregister event listeners,
    /// unsubscribe from addon events, etc.
    /// </summary>
    protected abstract void OnDisable();

    /// <summary>
    /// Draws the configuration UI for this tweak in the details panel; override to provide tweak-specific
    /// settings controls. The default implementation draws nothing.
    /// </summary>
    public virtual void DrawConfigUI() { }

    /// <summary>
    /// Signals the <see cref="NoireTweakManager"/> that this tweak's configuration has changed and should be
    /// persisted; call after modifying config values in <see cref="TweakBase{TConfig}.Config"/>.
    /// </summary>
    public void MarkConfigDirty()
    {
        Manager?.RecordTweakConfig(InternalKey);
    }

    /// <summary>
    /// Gets the <see cref="Type"/> of this tweak's configuration class, if any.
    /// </summary>
    /// <returns>The config type for <see cref="TweakBase{TConfig}"/> instances; <see langword="null"/> for configless tweaks.</returns>
    internal virtual Type? GetConfigType() => null;

    /// <summary>
    /// Gets the current configuration instance, if any.
    /// </summary>
    /// <returns>The <see cref="TweakConfigBase"/> instance for <see cref="TweakBase{TConfig}"/> instances; <see langword="null"/> for configless tweaks.</returns>
    internal virtual TweakConfigBase? GetConfigInstance() => null;

    /// <summary>
    /// Serializes the tweak's configuration to a JSON string for storage.
    /// </summary>
    /// <returns>The JSON string, or <see langword="null"/> if this tweak has no config.</returns>
    internal virtual string? SerializeConfig() => null;

    /// <summary>
    /// Deserializes configuration from a JSON string, applying any necessary migrations.
    /// </summary>
    /// <param name="json">The JSON to deserialize from.</param>
    /// <param name="storedVersion">The version of the stored config data.</param>
    internal virtual void DeserializeConfig(string? json, int storedVersion) { }

    /// <summary>
    /// Enables the tweak, invoking <see cref="OnEnable"/> and handling errors.
    /// </summary>
    /// <returns><see langword="true"/> if the tweak was enabled successfully; otherwise, <see langword="false"/>.</returns>
    internal bool Enable()
    {
        if (Enabled)
            return true;

        try
        {
            OnEnable();
            Enabled = true;
            HasError = false;
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            HasError = true;
            LastError = ex;
            Enabled = false;
            NoireLogger.LogError(this, ex, $"Failed to enable tweak '{Name}' ({InternalKey}).");
            return false;
        }
    }

    /// <summary>
    /// Disables the tweak, invoking <see cref="OnDisable"/> and handling errors.
    /// </summary>
    /// <returns><see langword="true"/> if the tweak was disabled successfully; otherwise, <see langword="false"/>.</returns>
    internal bool Disable()
    {
        if (!Enabled)
            return true;

        try
        {
            OnDisable();
            Enabled = false;
            return true;
        }
        catch (Exception ex)
        {
            HasError = true;
            LastError = ex;
            Enabled = false;
            NoireLogger.LogError(this, ex, $"Failed to disable tweak '{Name}' ({InternalKey}).");
            return false;
        }
    }

    /// <summary>
    /// Clears the error state of this tweak.
    /// </summary>
    internal void ClearError()
    {
        HasError = false;
        LastError = null;
    }

    /// <summary>
    /// Disposes the tweak, disabling it first if enabled and releasing any resources.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        try
        {
            if (Enabled)
                Disable();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"Error while disposing tweak '{Name}' ({InternalKey}).");
        }

        try
        {
            DisposeManaged();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"Error while disposing managed resources for tweak '{Name}' ({InternalKey}).");
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Override to dispose any managed resources held by the tweak; called after <see cref="OnDisable"/> during disposal.
    /// </summary>
    protected virtual void DisposeManaged() { }
}
