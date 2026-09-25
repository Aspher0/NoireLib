using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Windowing;
using NoireLib.Core.Modules;
using NoireLib.EventBus;
using NoireLib.Localizer;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Changelog;

/// <summary>Manages and displays a plugin's changelogs, with automatic version handling and EventBus events.</summary>
public class NoireChangelogManager : NoireModuleWithWindowBase<NoireChangelogManager, ChangelogWindow, ChangelogManagerConfigInstance>
{
    private readonly Dictionary<Version, ChangelogVersion> changelogs = new();
    private ChangelogVersion[] sortedVersions = [];
    private ChangelogVersion? selectedVersion;
    private NoireChangelogWindow? skinnedWindow;
    private readonly ChangelogTexts texts = new();

    /// <summary>The EventBus changelog events are published to, or null for none.</summary>
    public NoireEventBus? EventBus { get; set; } = null;

    /// <summary>The default constructor needed for internal purposes.</summary>
    public NoireChangelogManager() : base() { }

    /// <summary>Creates a new instance of the <see cref="NoireChangelogManager"/> module.</summary>
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

    internal NoireChangelogManager(ModuleId? moduleId, bool active = true, bool enableLogging = true)
        : base(moduleId, active, enableLogging) { }

    /// <summary>Initializes the module with optional initialization parameters.</summary>
    /// <param name="args">The initialization parameters</param>
    protected override void InitializeModule(params object?[] args)
    {
        if (args.Length > 0 && args[0] is bool autoShow)
            shouldAutomaticallyShowChangelog = autoShow;

        if (args.Length > 2 && args[2] is NoireEventBus eventBus)
            EventBus = eventBus;

        // Window and event bus must exist before any version is added.
        RegisterWindow(new ChangelogWindow(this));

        if (args.Length > 1 && args[1] is List<ChangelogVersion> versions)
            AddVersions(versions);

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
        if (DisplayedWindow?.IsOpen == true)
            DisplayedWindow.IsOpen = false;

        if (EnableLogging)
            NoireLogger.LogInfo(this, $"Changelog Manager deactivated.");
    }

    private bool shouldAutomaticallyShowChangelog = false;
    /// <summary>If true, the changelog window will automatically show when a new version is detected.</summary>
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

    /// <summary>Sets the value of <see cref="ShouldAutomaticallyShowChangelog"/>.</summary>
    /// <param name="shouldAutomaticallyShowChangelog">Whether the module should automatically show the changelog window.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager SetAutomaticallyShowChangelog(bool shouldAutomaticallyShowChangelog)
    {
        ShouldAutomaticallyShowChangelog = shouldAutomaticallyShowChangelog;
        return this;
    }

    #region Window Display

    /// <summary>Opens the changelog on a version, the latest when null or unknown. Without any version it stays closed.</summary>
    /// <param name="version">The version to show.</param>
    /// <returns>This module.</returns>
    public NoireChangelogManager ShowChangelogForVersion(Version? version = null)
    {
        var window = DisplayedWindow;
        if (window == null)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, "This module does not have an associated window.");

            return this;
        }

        if (!PrepareDisplay(version))
            return this;

        window.IsOpen = true;
        OnWindowOpened(selectedVersion!.Version);
        return this;
    }

    /// <summary>
    /// Closes the displayed changelog window and publishes <see cref="ChangelogWindowClosedEvent"/> if it was open.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager CloseWindow()
    {
        var window = DisplayedWindow;
        if (window?.IsOpen == true)
        {
            window.IsOpen = false;
            OnWindowClosed();
        }

        return this;
    }

    /// <summary>
    /// The plugin's own changelog window, shown in place of the built-in <see cref="ChangelogWindow"/>, or <see langword="null"/>.
    /// </summary>
    public Window? CustomWindow => CustomDisplayWindow;

    // Once the plugin registers skins, the changelog opens as a skinned window; a custom window still wins.
    private protected override Window? SkinnedWindow
    {
        get
        {
            if (skinnedWindow != null || NoireSkins.All.Count == 0 || NoireService.NoireWindowSystem is not { } windows)
                return skinnedWindow;

            skinnedWindow = new NoireChangelogWindow(this);
            windows.AddWindow(skinnedWindow);
            return skinnedWindow;
        }
    }

    /// <summary>Opens <paramref name="window"/> wherever the changelog would open. Null restores the built-in window.</summary>
    /// <param name="window">The plugin's window, drawn from <see cref="Versions"/> and <see cref="SelectedVersion"/>.</param>
    /// <returns>This module.</returns>
    public NoireChangelogManager SetCustomWindow(Window? window)
    {
        SetCustomDisplayWindow(window);
        return this;
    }

    #endregion

    #region Selection

    /// <summary>Every version, newest first, in the active language. Cached: reading it every frame allocates nothing.</summary>
    public IReadOnlyList<ChangelogVersion> Versions => texts.Shown(sortedVersions);

    /// <summary>
    /// The version the changelog window shows, the same instance <see cref="Versions"/> holds, or <see langword="null"/>
    /// when no version is available.
    /// </summary>
    public ChangelogVersion? SelectedVersion => texts.ShownOf(sortedVersions, selectedVersion);

    /// <summary>
    /// Selects the version the changelog window shows, publishing <see cref="ChangelogVersionChangedEvent"/> when it changes.
    /// </summary>
    /// <param name="version">The version to select.</param>
    /// <returns><see langword="true"/> if the manager holds <paramref name="version"/>.</returns>
    public bool SelectVersion(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        var target = changelogs.GetValueOrDefault(version);
        if (target == null)
            return false;

        var oldVersion = selectedVersion?.Version;
        selectedVersion = target;

        if (oldVersion != target.Version)
            OnVersionChanged(oldVersion, target.Version);

        return true;
    }

    internal bool PrepareDisplay(Version? version)
    {
        if (sortedVersions.Length == 0)
        {
            NoireService.NotificationManager.AddNotification(new Notification
            {
                Content = "There are no changelogs available",
                Title = "No changelog available",
                InitialDuration = TimeSpan.FromMilliseconds(3000),
                Type = NotificationType.Warning,
            });
            return false;
        }

        selectedVersion = (version != null ? changelogs.GetValueOrDefault(version) : null) ?? sortedVersions[0];
        return true;
    }

    internal void RebuildVersions()
    {
        // The versions' texts are declared now: an edited one gets its old translation back.
        NoireLanguages.Localizer?.CarryChangelogTexts();

        sortedVersions = changelogs.Values.OrderByDescending(v => v.Version).ToArray();
        selectedVersion = sortedVersions.Length > 0 ? sortedVersions[0] : null;

        if (sortedVersions.Length == 0)
            CloseWindow();
    }

    #endregion

    #region EventBus Integration

    private void PublishEvent<TEvent>(TEvent eventData)
    {
        EventBus?.Publish(eventData);
    }

    #endregion

    internal void OnWindowOpened(Version version)
    {
        PublishEvent(new ChangelogWindowOpenedEvent(version));
    }

    internal void OnWindowClosed()
    {
        PublishEvent(new ChangelogWindowClosedEvent());
    }

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

        var lastSeenVersion = ChangelogManagerConfig.LastSeenChangelogVersion;

        NoireLogger.LogDebug(this, $"Latest version: {latestVersion}, Last seen version: {lastSeenVersion}");

        if (lastSeenVersion == null || lastSeenVersion != latestVersion)
        {
            if (ShouldAutomaticallyShowChangelog)
                ShowChangelogForVersion(latestVersion);

            // Updated whatever the show setting. Re-enabling it must not show this version again.
            ChangelogManagerConfig.UpdateLastSeenVersion(latestVersion);
        }
    }

    #region Version Management

    /// <summary>
    /// Clears the last seen changelog version, causing the changelog window to show again on the next check if a version is available.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager ClearLastSeenVersion()
    {
        ChangelogManagerConfig.ClearLastSeenVersion();
        PublishEvent(new ChangelogLastSeenVersionClearedEvent());
        return this;
    }

    /// <summary>Sets the last seen changelog version to the latest available version.</summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager ForceLastSeenVersionToLatest()
    {
        var latestVersion = GetLatestVersion();
        if (latestVersion != null)
        {
            ChangelogManagerConfig.UpdateLastSeenVersion(latestVersion);
            PublishEvent(new ChangelogLastSeenVersionUpdatedEvent(latestVersion));
        }
        return this;
    }

    /// <summary>Sets the last seen changelog version.</summary>
    /// <param name="version">The version to set as last seen.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager SetLastSeenVersion(Version version)
    {
        ChangelogManagerConfig.UpdateLastSeenVersion(version);
        PublishEvent(new ChangelogLastSeenVersionUpdatedEvent(version));
        return this;
    }

    /// <summary>Retrieves all changelog versions, ordered from newest to oldest.</summary>
    /// <returns>The list of all changelog versions.</returns>
    public IReadOnlyList<ChangelogVersion> GetAllVersions()
    {
        return changelogs.Values
            .OrderByDescending(v => v.Version)
            .ToList();
    }

    /// <summary>Retrieves a specific changelog version by its Version object.</summary>
    /// <param name="version">The Version object to retrieve.</param>
    /// <returns>The corresponding <see cref="ChangelogVersion"/> if found, or null.</returns>
    public ChangelogVersion? GetVersion(Version version)
    {
        return changelogs.GetValueOrDefault(version);
    }

    /// <summary>Retrieves the latest changelog version.</summary>
    /// <returns>The latest version if available, or null.</returns>
    public Version? GetLatestVersion()
    {
        return changelogs.Keys.Max();
    }

    /// <summary>Adds or updates a changelog version.</summary>
    /// <param name="version">The <see cref="ChangelogVersion"/> to add or update.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager AddVersion(ChangelogVersion version)
    {
        AddVersionInternal(version);
        RebuildVersions();
        return this;
    }

    /// <summary>Adds or updates multiple changelog versions.</summary>
    /// <param name="versions">The list of <see cref="ChangelogVersion"/> to add or update.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager AddVersions(List<ChangelogVersion> versions)
    {
        foreach (var version in versions)
            AddVersionInternal(version);

        // Once for the whole list. Per version would keep resetting the selection.
        RebuildVersions();
        return this;
    }

    private void AddVersionInternal(ChangelogVersion version)
    {
        changelogs[version.Version] = version;

        // Declared now: the translation editor lists them before the changelog is ever opened.
        texts.Localize(version);
        PublishEvent(new ChangelogVersionAddedEvent(version.Version));
    }

    /// <summary>Removes a changelog version by its Version object.</summary>
    /// <param name="version">The Version object to remove.</param>
    /// <returns>True if the version was successfully removed.</returns>
    public bool RemoveVersion(Version version)
    {
        var removed = changelogs.Remove(version);
        if (removed)
            PublishEvent(new ChangelogVersionRemovedEvent(version));
        RebuildVersions();
        return removed;
    }

    /// <summary>Removes multiple changelog versions by their Version objects.</summary>
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

        RebuildVersions();

        return removedAmount;
    }

    /// <summary>Clears all changelog versions.</summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager ClearVersions()
    {
        changelogs.Clear();
        RebuildVersions();
        PublishEvent(new ChangelogVersionsClearedEvent());
        return this;
    }

    #endregion

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
                            AddVersionInternal(version);
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

        // Once for the whole scan. Per version would keep resetting the selection.
        RebuildVersions();
    }

    /// <summary>Internal dispose method called when the module is disposed.</summary>
    protected override void DisposeInternal()
    {
        changelogs.Clear();
        sortedVersions = [];
        selectedVersion = null;

        if (skinnedWindow != null)
        {
            NoireService.NoireWindowSystem?.RemoveWindow(skinnedWindow);
            skinnedWindow.Dispose();
            skinnedWindow = null;
        }

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Changelog Manager disposed.");
    }
}
