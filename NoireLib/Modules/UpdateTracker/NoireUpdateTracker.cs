using Dalamud.Game.Text;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Utility;
using NoireLib.Core.Modules;
using NoireLib.EventBus;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.UpdateTracker;

/// <summary>
/// A module that tracks updates for the plugin by checking a JSON repository URL at regular intervals.
/// </summary>
public class NoireUpdateTracker : NoireModuleBase
{
    public NoireEventBus? EventBus { get; set; }

    private readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    private Timer? updateCheckTimer;
    private bool updateNotificationShown = false;

    public NoireUpdateTracker() : base() { }

    /// <summary>
    /// Creates a new instance of the <see cref="NoireUpdateTracker"/> module.<br/>
    /// See <see cref="UpdateTrackerTextTags"/> to add dynamic content to messages and notifications.
    /// </summary>
    /// <param name="active">Whether the module should be active upon creation.</param>
    /// <param name="moduleId">The optional module identifier.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="repoUrl">The URL of the JSON repository to check for updates.</param>
    /// <param name="shouldPrintMessageInChatOnUpdate">Whether to print a message in chat when an update is detected.</param>
    /// <param name="shouldShowNotificationOnUpdate">Whether to show a notification when an update is detected.</param>
    /// <param name="message">The message to print in chat when an update is detected.<br/>Can use dynamic content tags.</param>
    /// <param name="notificationTitle">The title of the notification to show when an update is detected.<br/>Can use dynamic content tags.</param>
    /// <param name="notificationMessage">The message content of the notification to show when an update is detected.<br/>Can use dynamic content tags.</param>
    /// <param name="notificationDurationMs">The duration in milliseconds for which the update notification will be displayed.</param>
    /// <param name="eventBus">Optional EventBus instance to publish events. If null, no event will be published.</param>
    public NoireUpdateTracker(
        bool active = true,
        string? moduleId = null,
        bool enableLogging = true,
        string? repoUrl = null,
        bool shouldPrintMessageInChatOnUpdate = true,
        bool shouldShowNotificationOnUpdate = true,
        string? message = null,
        string? notificationTitle = null,
        string? notificationMessage = null,
        int notificationDurationMs = 30000,
        NoireEventBus? eventBus = null)
        : base(active,
            moduleId,
            enableLogging,
            repoUrl,
            shouldPrintMessageInChatOnUpdate,
            shouldShowNotificationOnUpdate,
            message,
            notificationTitle,
            notificationMessage,
            notificationDurationMs,
            eventBus)
    { }

    /// <summary>
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>.<br/>
    /// Only used for internal module management.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    public NoireUpdateTracker(ModuleId? moduleId, bool active = true, bool enableLogging = true) : base(moduleId, active, enableLogging) { }

    protected override void InitializeModule(params object?[] args)
    {
        if (NoireService.PluginInterface == null)
            throw new InvalidOperationException("NoireLib was not initialized.");

        if (args.Length > 0 && args[0] is string repoUrl)
            RepoUrl = repoUrl;

        if (args.Length > 1 && args[1] is bool shouldPrintMessageInChatOnUpdate)
            ShouldPrintMessageInChatOnUpdate = shouldPrintMessageInChatOnUpdate;

        if (args.Length > 2 && args[2] is bool shouldShowNotificationOnUpdate)
            ShouldShowNotificationOnUpdate = shouldShowNotificationOnUpdate;

        if (args.Length > 3 && args[3] is string message)
            Message = message;

        if (args.Length > 4 && args[4] is string notificationTitle)
            NotificationTitle = notificationTitle;

        if (args.Length > 5 && args[5] is string notificationMessage)
            NotificationMessage = notificationMessage;

        if (args.Length > 6 && args[6] is int notificationDurationMs)
            NotificationDurationMs = notificationDurationMs;

        if (args.Length > 7 && args[7] is NoireEventBus eventBus)
            EventBus = eventBus;

        NoireLogger.LogInfo(this, $"Update Tracker initialized.");
    }

    protected override void OnActivated()
    {
        StartUpdateCheckTimer();
        NoireLogger.LogInfo(this, $"Update Tracker activated.");
    }

    protected override void OnDeactivated()
    {
        StopUpdateCheckTimer();
        NoireLogger.LogInfo(this, $"Update Tracker deactivated.");
    }

    /// <summary>
    /// The URL of the JSON repository to check for updates.
    /// </summary>
    public string? RepoUrl { get; set; } = null;

    /// <summary>
    /// Sets the repository URL to check for updates.
    /// </summary>
    /// <param name="repoUrl">The URL of the JSON repository.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireUpdateTracker SetRepoUrl(string repoUrl)
    {
        RepoUrl = repoUrl;
        return this;
    }

    /// <summary>
    /// Whether to print a message in chat when an update is detected.
    /// </summary>
    public bool ShouldPrintMessageInChatOnUpdate { get; set; } = true;

    /// <summary>
    /// Sets whether to print a message in chat when an update is detected.
    /// </summary>
    /// <param name="shouldPrint">Whether to print the message in chat.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireUpdateTracker SetShouldPrintMessageInChatOnUpdate(bool shouldPrint)
    {
        ShouldPrintMessageInChatOnUpdate = shouldPrint;
        return this;
    }

    /// <<summary>
    /// Whether to show a notification when an update is detected.
    /// </summary>>
    public bool ShouldShowNotificationOnUpdate { get; set; } = true;

    /// <summary>
    /// Sets whether to show a notification when an update is detected.
    /// </summary>
    /// <param name="shouldShow">Whether to show the notification.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireUpdateTracker SetShouldShowNotificationOnUpdate(bool shouldShow)
    {
        ShouldShowNotificationOnUpdate = shouldShow;
        return this;
    }

    /// <summary>
    /// Whether to stop notifying after the first update notification has been shown.
    /// </summary>
    public bool ShouldStopNotifyingAfterFirstNotification { get; set; } = true;

    /// <summary>
    /// Sets whether to stop notifying after the first update notification has been shown.
    /// </summary>
    /// <param name="shouldStop">Whether to stop notifying after the first notification.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireUpdateTracker SetShouldStopNotifyingAfterFirstNotification(bool shouldStop)
    {
        ShouldStopNotifyingAfterFirstNotification = shouldStop;
        return this;
    }

    /// <summary>
    /// The message to print in chat when an update is detected.<br/>
    /// Use <see cref="UpdateTrackerTextTags"/> tags for dynamic content.<br/>
    /// Example: $"[{UpdateTrackerTextTags.PluginInternalName}] A new update is available. Current version: {UpdateTrackerTextTags.CurrentVersion} - New version: {UpdateTrackerTextTags.NewVersion}."<br/>
    /// Set to <see langword="null"/> to use the default content.
    /// </summary>
    public string? Message { get; set; } = null;

    /// <summary>
    /// Sets the message to print in chat when an update is detected.<br/>
    /// Use <see cref="UpdateTrackerTextTags"/> tags for dynamic content.<br/>
    /// Example: $"[{UpdateTrackerTextTags.PluginInternalName}] A new update is available. Current version: {UpdateTrackerTextTags.CurrentVersion} - New version: {UpdateTrackerTextTags.NewVersion}."<br/>
    /// Set to <see langword="null"/> to use the default content.
    /// </summary>
    /// <param name="message">The message content.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireUpdateTracker SetMessage(string? message)
    {
        Message = message;
        return this;
    }

    /// <summary>
    /// The title of the notification to show when an update is detected.<br/>
    /// Use <see cref="UpdateTrackerTextTags"/> tags for dynamic content.<br/>
    /// Example: $"{UpdateTrackerTextTags.PluginInternalName} - Update Available"<br/>
    /// Set to <see langword="null"/> to use the default content.
    /// </summary>
    public string? NotificationTitle { get; set; } = null;

    /// <summary>
    /// Sets the title of the notification to show when an update is detected.<br/>
    /// Use <see cref="UpdateTrackerTextTags"/> tags for dynamic content.<br/>
    /// Example: $"{UpdateTrackerTextTags.PluginInternalName} - Update Available"<br/>
    /// Set to <see langword="null"/> to use the default content.
    /// </summary>
    /// <param name="title"></param>
    /// <returns></returns>
    public NoireUpdateTracker SetNotificationTitle(string? title)
    {
        NotificationTitle = title;
        return this;
    }

    /// <summary>
    /// The message content of the notification to show when an update is detected.<br/>
    /// Use <see cref="UpdateTrackerTextTags"/> tags for dynamic content.<br/>
    /// Example: $"{UpdateTrackerTextTags.PluginInternalName} has a new update available.\nCurrent version: {UpdateTrackerTextTags.CurrentVersion}\nNew version: {UpdateTrackerText.NewVersion}"<br/>
    /// Set to <see langword="null"/> to use the default content.
    /// </summary>
    public string? NotificationMessage { get; set; } = null;

    /// <summary>
    /// Sets the message content of the notification to show when an update is detected.<br/>
    /// Use <see cref="UpdateTrackerTextTags"/> tags for dynamic content.<br/>
    /// Example: $"{UpdateTrackerTextTags.PluginInternalName} has a new update available.\nCurrent version: {UpdateTrackerTextTags.CurrentVersion}\nNew version: {UpdateTrackerText.NewVersion}"<br/>
    /// Set to <see langword="null"/> to use the default content.
    /// </summary>
    /// <param name="message">The notification message content.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireUpdateTracker SetNotificationMessage(string? notificationMessage)
    {
        NotificationMessage = notificationMessage;
        return this;
    }

    /// <summary>
    /// The duration in milliseconds for which the update notification will be displayed.<br/>
    /// By default, the notification will be shown for 30000 ms (30 seconds).
    /// </summary>
    public int NotificationDurationMs { get; set; } = 30000;

    /// <summary>
    /// Sets the duration in milliseconds for which the update notification will be displayed.<br/>
    /// By default, the notification will be shown for 30000 ms (30 seconds).
    /// </summary>
    /// <param name="durationMs">The duration in milliseconds.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireUpdateTracker SetNotificationDurationMs(int durationMs)
    {
        NotificationDurationMs = durationMs;
        return this;
    }

    private int checkIntervalMinutes = 30;

    /// <summary>
    /// The interval in minutes at which to check for updates.<br/>
    /// Default is 30 minutes.
    /// </summary>
    public int CheckIntervalMinutes
    {
        get => checkIntervalMinutes;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(CheckIntervalMinutes), "Check interval must be greater than zero.");

            checkIntervalMinutes = value;

            if (IsActive)
                StartUpdateCheckTimer();
        }
    }

    /// <summary>
    /// Sets the interval in minutes at which to check for updates.<br/>
    /// Default is 30 minutes.
    /// </summary>
    /// <param name="minutes">The interval in minutes.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireUpdateTracker SetCheckIntervalMinutes(int minutes)
    {
        CheckIntervalMinutes = minutes;
        return this;
    }

    private void StopUpdateCheckTimer()
    {
        updateCheckTimer?.Dispose();
        updateCheckTimer = null;
    }

    private void StartUpdateCheckTimer()
    {
        if (!IsActive)
        {
            NoireLogger.LogWarning(this, "Cannot start the update check timer. Module is deactivated.");
            return;
        }

        var wasTimerRunning = updateCheckTimer != null;
        var dueTime = wasTimerRunning ? TimeSpan.FromMinutes(CheckIntervalMinutes) : TimeSpan.Zero;

        StopUpdateCheckTimer();

        updateCheckTimer = new Timer(async _ => await CheckForUpdateAsync(),
            null,
            dueTime,
            TimeSpan.FromMinutes(CheckIntervalMinutes));
    }

    #region EventBus Integration

    /// <summary>
    /// Publishes events to the EventBus if available.
    /// </summary>
    private void PublishEvent<TEvent>(TEvent eventData)
    {
        EventBus?.Publish(eventData);
    }

    #endregion

    private async Task CheckForUpdateAsync()
    {
        if (!IsActive || RepoUrl.IsNullOrWhitespace() || (ShouldStopNotifyingAfterFirstNotification && updateNotificationShown))
            return;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, RepoUrl);
            using var resp = await httpClient.SendAsync(req).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var entries = JsonSerializer.Deserialize<List<RepoEntry>>(json, jsonOptions);
            if (entries is null || entries.Count == 0)
            {
                NoireLogger.LogWarning(this, "The JSON repository fetch returned no entries.");
                return;
            }

            var remote = entries.FirstOrDefault(e => string.Equals(e.InternalName, NoireService.PluginInterface.InternalName, StringComparison.OrdinalIgnoreCase));

            if (remote == null || string.IsNullOrWhiteSpace(remote.AssemblyVersion))
            {
                NoireLogger.LogWarning(this, $"No matching internal name entry found in the repository or the assembly version is missing. Looking for internal name: {NoireService.PluginInterface.InternalName}.");
                return;
            }

            Version? remoteVersion;

            try
            {
                remoteVersion = new Version(remote.AssemblyVersion);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(this, ex, $"Failed to parse the version string from the JSON repository url, version string found: {remote.AssemblyVersion}");
                return;
            }

            if (remoteVersion == null)
                return;

            var currentVersion = NoireService.PluginInstance?.GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

            if (currentVersion < remoteVersion)
            {
                PublishEvent(new NewPluginVersionDetectedEvent(currentVersion, remoteVersion));

                updateNotificationShown = true;

                if (ShouldShowNotificationOnUpdate)
                {
                    var notificationMessage = NotificationMessage ?? $"{UpdateTrackerTextTags.PluginInternalName} has a new update available.\nCurrent version: {UpdateTrackerTextTags.CurrentVersion}\nNew version: {UpdateTrackerTextTags.NewVersion}";
                    var notificationTitle = NotificationTitle ?? $"{UpdateTrackerTextTags.PluginInternalName} Update Available";

                    NoireService.NotificationManager.AddNotification(new()
                    {
                        Content = ParseMessageTemplate(notificationMessage, currentVersion.ToString(), remoteVersion.ToString()),
                        Title = ParseMessageTemplate(notificationTitle, currentVersion.ToString(), remoteVersion.ToString()),
                        Type = NotificationType.Info,
                        InitialDuration = TimeSpan.FromMilliseconds(NotificationDurationMs),
                    });
                }

                if (ShouldPrintMessageInChatOnUpdate)
                {
                    var message = Message ?? $"[{UpdateTrackerTextTags.PluginInternalName}] A new update is available. Please update the plugin in /xlplugins. Current version: {UpdateTrackerTextTags.CurrentVersion} - New version: {UpdateTrackerTextTags.NewVersion}.";

                    NoireLogger.PrintToChat(
                        XivChatType.Echo,
                        ParseMessageTemplate(message, currentVersion.ToString(), remoteVersion.ToString()),
                        ColorHelper.HexToVector3("#FCC203"));
                }
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, "Failed to check for updates.");
        }
    }

    private string ParseMessageTemplate(string template, string currentVersion, string newVersion)
    {
        return template
            .Replace(UpdateTrackerTextTags.PluginInternalName, NoireService.PluginInterface.InternalName)
            .Replace(UpdateTrackerTextTags.CurrentVersion, currentVersion)
            .Replace(UpdateTrackerTextTags.NewVersion, newVersion);
    }

    public override void Dispose()
    {
        StopUpdateCheckTimer();
        httpClient.Dispose();
        NoireLogger.LogInfo(this, $"Update Tracker disposed.");
    }
}
