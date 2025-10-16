using System;
using System.Collections.Generic;
using System.Linq;
using static Dalamud.Interface.Windowing.Window;

namespace NoireLib.Changelog;

/// <summary>
/// A module that manages and displays changelogs for a plugin.<br/>
/// Includes a fully automatic version handling, as well as manual management methods.
/// </summary>
public class NoireChangelogManager : NoireModuleBase
{
    private ChangelogWindow ChangelogWindow { get; set; } = null!;

    private readonly Dictionary<Version, ChangelogVersion> changelogs = new();

    public NoireChangelogManager() : base() { }
    public NoireChangelogManager(
        bool active = true,
        string? moduleId = null,
        bool shouldAutomaticallyShowChangelog = false,
        List<ChangelogVersion>? versions = null) : base(active, moduleId)
    {
        ShouldAutomaticallyShowChangelog = shouldAutomaticallyShowChangelog;
        
        if (versions != null)
        {
            ClearVersions();
            AddVersions(versions);
        }
    }

    /// <summary>
    /// Only used for internal module management.
    /// </summary>
    public NoireChangelogManager(ModuleId moduleId, bool active = true) : base(moduleId, active) { }

    protected override void InitializeModule()
    {
        ChangelogWindow = new ChangelogWindow(this);
        NoireService.NoireWindowSystem.AddWindow(ChangelogWindow);

        if (changelogs.Count == 0)
            LoadVersionsFromAssembly();

        NoireLogger.LogInfo(this, $"Changelog Manager initialized.");
    }

    protected override void OnActivated()
    {
        if (ShouldAutomaticallyShowChangelog)
            AutomaticallyCheckChangelogAndShowIfNewVersion();
    }

    protected override void OnDeactivated()
    {
        if (ChangelogWindow.IsOpen)
            ChangelogWindow.IsOpen = false;
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


    private string windowName = "Changelog";
    /// <summary>
    /// The name displayed in the title bar of the changelog window.
    /// </summary>
    public string WindowName
    {
        get => windowName;
        set
        {
            windowName = value;
            ChangelogWindow.UpdateWindowName();
        }
    }

    /// <summary>
    /// Sets the window name of the changelog window.
    /// </summary>
    /// <param name="windowName">The name of the window.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager SetWindowName(string windowName)
    {
        WindowName = windowName;
        return this;
    }

    /// <summary>
    /// Gets the full window name of the changelog window, including hidden IDs.
    /// </summary>
    /// <returns>The full window name.</returns>
    public string GetFullWindowName() => ChangelogWindow.WindowName;

    #region Title Bar Buttons

    /// <summary>
    /// Do not add buttons directly to this list, use the provided methods instead.<br/>
    /// <see cref="AddTitleBarButton"/>, <see cref="RemoveTitleBarButton"/>, <see cref="SetTitleBarButtons"/>, <see cref="ClearTitleBarButtons"/>
    /// </summary>
    public List<TitleBarButton> TitleBarButtons { get; private set; } = new();

    /// <summary>
    /// Adds a button to the title bar of the changelog window.
    /// </summary>
    /// <param name="titleBarButton">The title bar button to add.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager AddTitleBarButton(TitleBarButton titleBarButton)
    {
        if (titleBarButton == null)
            return this;

        TitleBarButtons.Add(titleBarButton);

        if (ChangelogWindow != null)
            ChangelogWindow.UpdateTitleBarButtons(TitleBarButtons);

        return this;
    }

    /// <summary>
    /// Removes a button from the title bar of the changelog window by its index.
    /// </summary>
    /// <param name="index">The index of the title bar button to remove.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager RemoveTitleBarButton(int index)
    {
        if (index < 0 || index >= TitleBarButtons.Count)
            return this;

        TitleBarButtons.RemoveAt(index);

        if (ChangelogWindow != null)
            ChangelogWindow.UpdateTitleBarButtons(TitleBarButtons);

        return this;
    }

    /// <summary>
    /// Sets the title bar buttons of the changelog window, replacing any existing buttons.
    /// </summary>
    /// <param name="titleBarButtons">The list of title bar buttons to set.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager SetTitleBarButtons(List<TitleBarButton> titleBarButtons)
    {
        TitleBarButtons = titleBarButtons ?? new();

        if (ChangelogWindow != null)
            ChangelogWindow.UpdateTitleBarButtons(TitleBarButtons);

        return this;
    }

    /// <summary>
    /// Clears all title bar buttons from the changelog window.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager ClearTitleBarButtons()
    {
        TitleBarButtons.Clear();

        if (ChangelogWindow != null)
            ChangelogWindow.UpdateTitleBarButtons(TitleBarButtons);

        return this;
    }

    #endregion


    /// <summary>
    /// Toggles the changelog window.
    /// </summary>
    /// <param name="show">Set to null to toggle the state, true to force show, false to force hide.</param>
    public NoireChangelogManager ShowChangelogWindow(bool? show = null, Version? version = null)
    {
        if (!IsActive)
            return this;

        if (show == false || (show == null && ChangelogWindow.IsOpen == true))
            ChangelogWindow.CloseWindow();
        else
            ChangelogWindow.ShowChangelogForVersion(version);

        return this;
    }

    private void AutomaticallyCheckChangelogAndShowIfNewVersion()
    {
        if (!IsActive || !ShouldAutomaticallyShowChangelog)
            return;

        var latestVersion = GetLatestVersion();
        if (latestVersion == null)
            return;

        //var lastSeenVersion = NoireService.Configuration.LastSeenChangelogVersion;
        Version? lastSeenVersion = null; // Temporary until configuration is implemented

        if (lastSeenVersion == null || lastSeenVersion != latestVersion)
        {
            ChangelogWindow.ShowChangelogForVersion(latestVersion);
            //NoireService.Configuration.UpdateConfiguration(() => Service.Configuration.LastSeenChangelogVersion = latestVersion);
        }
    }

    #region Version Management

    /// <summary>
    /// Clears the last seen changelog version, causing the changelog window to show again on the next check if a version is available.
    /// </summary>
    /// <returns></returns>
    public NoireChangelogManager ClearLastSeenVersion()
    {
        //NoireService.Configuration.UpdateConfiguration(() => Service.Configuration.LastSeenChangelogVersion = null);
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
        ChangelogWindow.UpdateVersions();
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

        ChangelogWindow.UpdateVersions();

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
        ChangelogWindow.UpdateVersions();
        return removed;
    }

    /// <summary>
    /// Removes multiple changelog versions by their Version objects.
    /// </summary>
    /// <param name="versions">The list of Version objects to remove.</param>
    /// <returns>True if all specified versions were successfully removed; otherwise, false.</returns>
    public bool RemoveVersions(List<Version> versions)
    {
        var removedAll = true;
        foreach (var version in versions)
        {
            var removed = changelogs.Remove(version);
            if (!removed)
                removedAll = false;
        }
        ChangelogWindow.UpdateVersions();
        return removedAll;
    }

    /// <summary>
    /// Clears all changelog versions.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireChangelogManager ClearVersions()
    {
        changelogs.Clear();
        ChangelogWindow.UpdateVersions();
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
            var assembly = NoireService.PluginInstance?.GetType().Assembly;

            if (assembly == null)
            {
                NoireLogger.LogError(this, "NoireLib was not initialized. Please, initialize NoireLib in your Plugin constructor.");
                return;
            }

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
                    NoireLogger.LogError(this, ex, $"Failed to load changelog version from type {type.Name}");
                }
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"Failed to load changelog versions from assembly.");
        }
    }

    public override void Dispose()
    {
        NoireService.NoireWindowSystem.RemoveWindow(ChangelogWindow);
        ChangelogWindow.Dispose();
    }
}
