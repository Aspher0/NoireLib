using System;
using System.Reflection;

namespace NoireLib.Helpers;

/// <summary>
/// One plugin Dalamud has installed, reached by reflection. It carries what Dalamud knows about the plugin and,
/// when the plugin is loaded, a handle on the plugin object itself.
/// <br/>
/// The handle is taken once and does not survive the plugin reloading. A long-lived consumer looks the plugin up
/// again. It never holds this across a reload.
/// </summary>
public sealed class ReflectedPlugin
{
    private readonly ReflectedObject local;

    internal ReflectedPlugin(ReflectedObject local)
    {
        this.local = local;
    }

    /// <summary>The plugin's display name.</summary>
    public string Name => local.Get<string>("Name") ?? string.Empty;

    /// <summary>The plugin's internal name.</summary>
    public string InternalName => local.Get<string>("InternalName") ?? string.Empty;

    /// <summary>
    /// The only identifier that is unique per installation. Several dev directories can hold builds of the same
    /// assembly, and several repositories can offer the same plugin. A name cannot tell those apart. This does.
    /// </summary>
    public Guid Id => local.Get<Guid>("EffectiveWorkingPluginId");

    /// <summary>Where the plugin was loaded from.</summary>
    public string Path => local.Into("DllFile")?.Get<string>("FullName") ?? string.Empty;

    /// <summary>The name a dev plugin was given in Dalamud's settings, empty for a plugin from a repository.</summary>
    public string Nickname => local.Get<string>("Nickname") ?? string.Empty;

    /// <summary>The author the plugin's manifest names.</summary>
    public string Author => Manifest?.Get<string>("Author") ?? string.Empty;

    /// <summary>The repository the plugin came from, empty for a dev plugin and for the official repository.</summary>
    public string RepositoryUrl => Manifest?.Get<string>("RepoUrl") ?? string.Empty;

    /// <summary>Whether a profile wants the plugin loaded, shown as enabled in the installer. A wanted plugin can fail to load.</summary>
    public bool IsEnabled => local.Get<bool>("IsWantedByAnyProfile");

    /// <summary>Where the plugin is in its load cycle, such as Loaded, Unloaded, or LoadError.</summary>
    public string State => local.Get("State")?.ToString() ?? string.Empty;

    /// <summary>The plugin's manifest, for the fields this wrapper does not name.</summary>
    public ReflectedObject? Manifest => local.Into("Manifest");

    /// <summary>The version Dalamud loaded, the testing version when opted into testing.</summary>
    public Version? Version => local.Get<Version>("EffectiveVersion");

    /// <summary>Whether the plugin is loaded. A plugin that is installed but disabled is not.</summary>
    public bool IsLoaded => local.Get<bool>("IsLoaded");

    /// <summary>Whether the plugin was loaded from a dev directory.</summary>
    public bool IsDev => local.Get<bool>("IsDev");

    /// <summary>Whether the plugin came from a repository other than the official one.</summary>
    public bool IsThirdParty => local.Get<bool>("IsThirdParty");

    /// <summary>Whether the plugin is on its testing track.</summary>
    public bool IsTesting => local.Get<bool>("IsTesting");

    /// <summary>Whether the plugin is built against an older API level than the running Dalamud.</summary>
    public bool IsOutdated => local.Get<bool>("IsOutdated");

    /// <summary>Whether Dalamud has banned the plugin.</summary>
    public bool IsBanned => local.Get<bool>("IsBanned");

    /// <summary>The plugin's assembly, or null when it is not loaded.</summary>
    public Assembly? Assembly => local.Get<Assembly>("Assembly");

    /// <summary>
    /// A handle on the plugin object itself, the one implementing Dalamud's plugin interface, or null when the
    /// plugin is not loaded. Everything the plugin holds hangs off this.
    /// </summary>
    public ReflectedObject? Instance
    {
        get
        {
            var value = local.Get(DalamudInternals.PluginInstanceField);
            return value == null ? null : new ReflectedObject(value);
        }
    }

    /// <summary>Dalamud's own record for this plugin, for anything this wrapper does not expose.</summary>
    public ReflectedObject Local => local;

    /// <summary>A handle on one of the plugin's types, for its static state.</summary>
    /// <param name="typeName">The type's full name, namespace included.</param>
    /// <returns>A handle on the type's static members, or null when the plugin carries no such type.</returns>
    public ReflectedObject? Static(string typeName)
    {
        var type = SafeExecutor.ExecuteSafely(() => Assembly?.GetType(typeName, throwOnError: false), null);
        return type == null ? null : new ReflectedObject(type);
    }

    /// <summary>
    /// Builds a typed facade over the plugin object. Its members are reached through an interface the compiler
    /// checks, never through names spelled at each call site. Each member of
    /// <typeparamref name="TContract"/> is routed to the plugin member of the same name.
    /// </summary>
    /// <typeparam name="TContract">The interface describing what the plugin is expected to carry.</typeparam>
    /// <param name="strict">
    /// True to throw a <see cref="MissingMemberException"/> when a member of the contract is not on the plugin,
    /// false to have it read as the default and a call return the default.
    /// </param>
    /// <returns>The facade, or null when the plugin is not loaded.</returns>
    /// <exception cref="ArgumentException">If <typeparamref name="TContract"/> is not an interface.</exception>
    public TContract? Bind<TContract>(bool strict = false) where TContract : class
    {
        if (!typeof(TContract).IsInterface)
            throw new ArgumentException("A contract must be an interface.", nameof(TContract));

        var instance = Instance;
        return instance == null ? null : (TContract)ReflectedProxy.Create(typeof(TContract), instance, strict);
    }

    /// <summary>
    /// Builds a typed facade over one of the plugin's types, for a plugin whose entry points are static.
    /// </summary>
    /// <typeparam name="TContract">The interface describing what the type is expected to carry.</typeparam>
    /// <param name="typeName">The type's full name, namespace included.</param>
    /// <param name="strict">True to throw when a member of the contract is missing.</param>
    /// <returns>The facade, or null when the plugin carries no such type.</returns>
    /// <exception cref="ArgumentException">If <typeparamref name="TContract"/> is not an interface.</exception>
    public TContract? BindStatic<TContract>(string typeName, bool strict = false) where TContract : class
    {
        if (!typeof(TContract).IsInterface)
            throw new ArgumentException("A contract must be an interface.", nameof(TContract));

        var type = Static(typeName);
        return type == null ? null : (TContract)ReflectedProxy.Create(typeof(TContract), type, strict);
    }
}
