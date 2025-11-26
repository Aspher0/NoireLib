using NoireLib.Core.Modules;
using NoireLib.EventBus;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Changelog;

/// <summary>
/// A module that manages and displays changelogs for a plugin.<br/>
/// Includes a fully automatic version handling, as well as manual management methods.<br/>
/// Publishes events via <see cref="EventBus"/> for changelog actions.
/// </summary>
public class NoireChangelogManager : NoireModuleWithWindowBase<NoireChangelogManager, ChangelogWindow>
{
    private readonly Dictionary<Version, ChangelogVersion> changelogs = new();

    /// <summary>
    /// The associated EventBus instance for publishing changelog events.<br/>
    /// If <see langword="null"/>, no events will be published.
    /// </summary>
    public NoireEventBus? EventBus { get; set; } = null;

    /// <summary>
    /// The default constructor needed for internal purposes.
    /// </summary>
    public NoireChangelogManager() : base() { }

    /// <summary>
    /// Creates a new instance of the <see cref="NoireChangelogManager"/> module.
    /// </summary>
    /// <param name="moduleId">The optional module identifier.</param>
    /// <param name="active">Whether the module should be active upon creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="shouldAutomaticallyShowChangelog">Defines whether the changelog window should automatically show when a new version is detected.</param>
    /// <param name="versions">A list of changelog versions to initialize the manager with.</param>
    /// <param name="eventBus">Optional EventBus instance to publish changelog events. If null, no event will be published.</param>
    public NoireChangelogManager(
        string? moduleId = null,
        bool active = true,
        bool enableLogging = true,
        bool shouldAutomaticallyShowChangelog = false,
        List<ChangelogVersion>? versions = null,
        NoireEventBus? eventBus = null)
            : base(moduleId, active, enableLogging, shouldAutomaticallyShowChangelog, versions, eventBus) { }

    /// <summary>
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>.<br/>
    /// Only used for internal module management.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    internal NoireChangelogManager(ModuleId? moduleId, bool active = true, bool enableLogging = true)
        : base(moduleId, active, enableLogging) { }

    /// <summary>
    /// Initializes the module with optional initialization parameters.
    /// </summary>
    /// <param name="args">The initialization parameters</param>
    protected override void InitializeModule(params object?[] args)
    {
        if (args.Length > 0 && args[0] is bool autoShow)
            shouldAutomaticallyShowChangelog = autoShow;

        if (args.Length > 1 && args[1] is List<ChangelogVersion> versions)
            AddVersions(versions);

        if (args.Length > 2 && args[2] is NoireEventBus eventBus)
            EventBus = eventBus;

        RegisterWindow(new ChangelogWindow(this));

        if (changelogs.Count == 0)
            LoadVersionsFromAssembly();

        if (EnableLogging)
            NoireLogger.LogInfo(this, $"Changelog Manager initialized.");
    }

    /// <summary>
    /// Called when the module is activated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> false to true.
    /// </summary>
    protected override void OnActivated()
    {
        AutomaticallyCheckChangelogAndShowIfNewVersion();

        if (EnableLogging)
            NoireLogger.LogInfo(this, $"Changelog Manager activated.");
    }

    /// <summary>
    /// Called when the module is deactivated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> true to false.
    /// </summary>
    protected override void OnDeactivated()
    {
        if (ModuleWindow!.IsOpen)
            ModuleWindow.IsOpen = false;

        if (EnableLogging)
            NoireLogger.LogInfo(this, $"Changelog Manager deactivated.");
    }

    private bool shouldAutomaticallyShowChangelog = false;
    /// <summary>
    /// If true, the changelog window will automatically show when a new version is detected.
    /// </summary>
    public bool ShouldAutomaticallyShowChangelog
    {
        get => shouldAutomaticallyShowChangelog;
        set
        {
            shouldAutomaticallyShowChangelog = value;
            if (shouldAutomaticallyShowChangelog)
                AutomaticallyCheckChangelogAndShowIfNewVersion();
        }
    }

    /// <summary>
    /// Sets the value of <see cref="ShouldAutomaticallyShowChangelog"/>.
    /// </summary>
    /// <param name="shouldAutomaticallyShowChangelog">Whether the module should automatically show the changelog window.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager SetAutomaticallyShowChangelog(bool shouldAutomaticallyShowChangelog)
    {
        ShouldAutomaticallyShowChangelog = shouldAutomaticallyShowChangelog;
        return this;
    }

    #region EventBus Integration

    /// <summary>
    /// Publishes a changelog event to the EventBus if available.
    /// </summary>
    private void PublishEvent<TEvent>(TEvent eventData)
    {
        EventBus?.Publish(eventData);
    }

    #endregion

    /// <summary>
    /// Toggles the changelog window.
    /// </summary>
    /// <param name="show">Set to null to toggle the state, true to force show, false to force hide.</param>
    /// <param name="version">The version to show in the changelog window. If null, shows the latest version.</param>
    public NoireChangelogManager ShowWindow(bool? show = null, Version? version = null)
    {
        if (!IsActive)
            return this;

        if (show == false || (show == null && ModuleWindow!.IsOpen == true))
            ModuleWindow!.CloseWindow();
        else
            ModuleWindow!.ShowChangelogForVersion(version);

        return this;
    }

    // Just an override to use ShowWindow method
    /// <inheritdoc cref="NoireModuleWithWindowBase{TModule, TWindow}.ToggleWindow"/>
    public override NoireChangelogManager ToggleWindow()
        => ShowWindow();

    // Just an override to use ShowWindow method
    /// <inheritdoc cref="ShowWindow(bool?, Version?)"/>
    public override NoireChangelogManager ShowWindow()
        => ShowWindow();

    /// <summary>
    /// Internal method called by ChangelogWindow when the window is opened.
    /// </summary>
    internal void OnWindowOpened(Version version)
    {
        PublishEvent(new ChangelogWindowOpenedEvent(version));
    }

    /// <summary>
    /// Internal method called by ChangelogWindow when the window is closed.
    /// </summary>
    internal void OnWindowClosed()
    {
        PublishEvent(new ChangelogWindowClosedEvent());
    }

    /// <summary>
    /// Internal method called by ChangelogWindow when the selected version changes.
    /// </summary>
    internal void OnVersionChanged(Version? oldVersion, Version newVersion)
    {
        PublishEvent(new ChangelogVersionChangedEvent(oldVersion, newVersion));
    }

    private void AutomaticallyCheckChangelogAndShowIfNewVersion()
    {
        if (!IsActive)
            return;

        var latestVersion = GetLatestVersion();
        if (latestVersion == null)
            return;

        var lastSeenVersion = ChangelogManagerConfig.Instance.LastSeenChangelogVersion;

        NoireLogger.LogDebug(this, $"Latest version: {latestVersion}, Last seen version: {lastSeenVersion}");

        if (lastSeenVersion == null || lastSeenVersion != latestVersion)
        {
            if (ShouldAutomaticallyShowChangelog)
                ModuleWindow!.ShowChangelogForVersion(latestVersion);

            // Update the last seen version, even if should not show automatically to avoid showing it automatically when enabling the option
            ChangelogManagerConfig.Instance.UpdateLastSeenVersion(latestVersion);
        }
    }

    #region Version Management

    /// <summary>
    /// Clears the last seen changelog version, causing the changelog window to show again on the next check if a version is available.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager ClearLastSeenVersion()
    {
        ChangelogManagerConfig.Instance.ClearLastSeenVersion();
        PublishEvent(new ChangelogLastSeenVersionClearedEvent());
        return this;
    }

    /// <summary>
    /// Sets the last seen changelog version to the latest available version.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager ForceLastSeenVersionToLatest()
    {
        var latestVersion = GetLatestVersion();
        if (latestVersion != null)
        {
            ChangelogManagerConfig.Instance.UpdateLastSeenVersion(latestVersion);
            PublishEvent(new ChangelogLastSeenVersionUpdatedEvent(latestVersion));
        }
        return this;
    }

    /// <summary>
    /// Sets the last seen changelog version.
    /// </summary>
    /// <param name="version">The version to set as last seen.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager SetLastSeenVersion(Version version)
    {
        ChangelogManagerConfig.Instance.UpdateLastSeenVersion(version);
        PublishEvent(new ChangelogLastSeenVersionUpdatedEvent(version));
        return this;
    }

    /// <summary>
    /// Retrieves all changelog versions, ordered from newest to oldest.
    /// </summary>
    /// <returns>The list of all changelog versions.</returns>
    public IReadOnlyList<ChangelogVersion> GetAllVersions()
    {
        return changelogs.Values
            .OrderByDescending(v => v.Version)
            .ToList();
    }

    /// <summary>
    /// Retrieves a specific changelog version by its Version object.
    /// </summary>
    /// <param name="version">The Version object to retrieve.</param>
    /// <returns>The corresponding <see cref="ChangelogVersion"/> if found; otherwise, null.</returns>
    public ChangelogVersion? GetVersion(Version version)
    {
        return changelogs.GetValueOrDefault(version);
    }

    /// <summary>
    /// Retrieves the latest changelog version.
    /// </summary>
    /// <returns>The latest version if available; otherwise, null.</returns>
    public Version? GetLatestVersion()
    {
        var versions = GetAllVersions();
        return versions.FirstOrDefault()?.Version;
    }

    /// <summary>
    /// Adds or updates a changelog version.<br/>
    /// </summary>
    /// <param name="version">The <see cref="ChangelogVersion"/> to add or update.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager AddVersion(ChangelogVersion version)
    {
        changelogs[version.Version] = version;
        ModuleWindow!.UpdateVersions();
        PublishEvent(new ChangelogVersionAddedEvent(version.Version));
        return this;
    }

    /// <summary>
    /// Adds or updates multiple changelog versions.<br/>
    /// </summary>
    /// <param name="versions">The list of <see cref="ChangelogVersion"/> to add or update.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager AddVersions(List<ChangelogVersion> versions)
    {
        foreach (var version in versions)
            AddVersion(version);
        ModuleWindow!.UpdateVersions();
        return this;
    }

    /// <summary>
    /// Removes a changelog version by its Version object.
    /// </summary>
    /// <param name="version">The Version object to remove.</param>
    /// <returns>True if the version was successfully removed; otherwise, false.</returns>
    public bool RemoveVersion(Version version)
    {
        var removed = changelogs.Remove(version);
        if (removed)
            PublishEvent(new ChangelogVersionRemovedEvent(version));
        ModuleWindow!.UpdateVersions();
        return removed;
    }

    /// <summary>
    /// Removes multiple changelog versions by their Version objects.
    /// </summary>
    /// <param name="versions">The list of Version objects to remove.</param>
    /// <returns>The number of versions successfully removed.</returns>
    public int RemoveVersions(List<Version> versions)
    {
        int removedAmount = 0;
        foreach (var version in versions)
        {
            if (changelogs.Remove(version))
            {
                removedAmount++;
                PublishEvent(new ChangelogVersionRemovedEvent(version));
            }
        }

        ModuleWindow!.UpdateVersions();

        return removedAmount;
    }

    /// <summary>
    /// Clears all changelog versions.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager ClearVersions()
    {
        changelogs.Clear();
        ModuleWindow!.UpdateVersions();
        PublishEvent(new ChangelogVersionsClearedEvent());
        return this;
    }

    #endregion

    /// <summary>
    /// Automatically loads changelog versions from the plugin assembly using reflection.
    /// </summary>
    private void LoadVersionsFromAssembly()
    {
        try
        {
            if (!NoireService.IsInitialized())
            {
                if (EnableLogging)
                    NoireLogger.LogError(this, "NoireLib was not initialized. Please, initialize NoireLib in your Plugin constructor.");
                return;
            }

            var assembly = NoireService.PluginInstance!.GetType().Assembly;

            var versionTypes = assembly.GetTypes()
                .Where(t => typeof(IChangelogVersion).IsAssignableFrom(t) &&
                            !t.IsAbstract &&
                            !t.IsInterface)
                .ToList();

            foreach (var type in versionTypes)
            {
                try
                {
                    if (Activator.CreateInstance(type) is IChangelogVersion versionInstance)
                    {
                        var versions = versionInstance.GetVersions();
                        foreach (var version in versions)
                            AddVersion(version);
                    }
                }
                catch (Exception ex)
                {
                    if (EnableLogging)
                        NoireLogger.LogError(this, ex, $"Failed to load changelog version from type {type.Name}");
                }
            }
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, $"Failed to load changelog versions from assembly.");
        }
    }

    /// <summary>
    /// Internal dispose method called when the module is disposed.
    /// </summary>
    protected override void DisposeInternal()
    {
        changelogs.Clear();

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Changelog Manager disposed.");
    }
}
