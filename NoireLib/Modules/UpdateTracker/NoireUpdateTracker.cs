using Dalamud.Game.Text;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Utility;
using Newtonsoft.Json;
using NoireLib.Core.Modules;
using NoireLib.EventBus;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.UpdateTracker;

/// <summary>
/// A module that tracks updates for the plugin by checking a JSON repository URL at regular intervals.
/// </summary>
public class NoireUpdateTracker : NoireModuleBase<NoireUpdateTracker>
{
    /// <summary>The EventBus instance to publish events to; <see langword="null"/> publishes nothing.</summary>
    public NoireEventBus? EventBus { get; set; }

    // Built with Create: no process-global DefaultSettings leak in. TypeNameHandling stays None: a response never names a type.
    private static readonly JsonSerializer RepositoryReader = CreateRepositoryReader();

    private static JsonSerializer CreateRepositoryReader()
    {
        var serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
        });

        serializer.CheckAdditionalContent = true;
        return serializer;
    }

    internal static List<RepoEntry>? ParseRepositoryResponse(string json)
    {
        using var stringReader = new StringReader(json);
        using var jsonReader = new JsonTextReader(stringReader);

        return RepositoryReader.Deserialize<List<RepoEntry>>(jsonReader);
    }

    private readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };

    // Cancelled first on teardown: a check in flight never resumes against a disposed httpClient.
    private readonly CancellationTokenSource disposalTokenSource = new();

    // Latched first on teardown: IsActive only clears once teardown has finished.
    private volatile bool disposed;

    private readonly object timerLock = new();
    private Timer? updateCheckTimer;

    /// <summary>The default constructor needed for internal purposes.</summary>
    public NoireUpdateTracker() : base() { }

    /// <summary>Creates a new instance of the <see cref="NoireUpdateTracker"/> module. Messages accept <see cref="UpdateTrackerTextTags"/>.</summary>
    /// <param name="moduleId">The optional module identifier.</param>
    /// <param name="active">Whether the module is active on creation.</param>
    /// <param name="enableLogging">Whether this module logs.</param>
    /// <param name="repoUrl">The JSON repository to check.</param>
    /// <param name="shouldPrintMessageInChatOnUpdate">Whether an update prints a chat message.</param>
    /// <param name="shouldShowNotificationOnUpdate">Whether an update shows a notification.</param>
    /// <param name="message">The chat message.</param>
    /// <param name="notificationTitle">The notification title.</param>
    /// <param name="notificationMessage">The notification text.</param>
    /// <param name="notificationDurationMs">How long the notification shows, in milliseconds.</param>
    /// <param name="eventBus">The EventBus the detection is published to.</param>
    /// <param name="shouldStopNotifyingAfterFirstNotification">Whether checking stops once an update reached a channel.</param>
    public NoireUpdateTracker(
        string? moduleId = null,
        bool active = true,
        bool enableLogging = true,
        string? repoUrl = null,
        bool shouldPrintMessageInChatOnUpdate = true,
        bool shouldShowNotificationOnUpdate = true,
        string? message = null,
        string? notificationTitle = null,
        string? notificationMessage = null,
        int notificationDurationMs = 30000,
        NoireEventBus? eventBus = null,
        bool shouldStopNotifyingAfterFirstNotification = true)
        : base(moduleId,
               active,
               enableLogging,
               repoUrl,
               shouldPrintMessageInChatOnUpdate,
               shouldShowNotificationOnUpdate,
               message,
               notificationTitle,
               notificationMessage,
               notificationDurationMs,
               eventBus,
               shouldStopNotifyingAfterFirstNotification)
    { }

    internal NoireUpdateTracker(ModuleId? moduleId, bool active = true, bool enableLogging = true) : base(moduleId, active, enableLogging) { }

    /// <summary>Initializes the module with optional initialization parameters.</summary>
    /// <param name="args">The initialization parameters</param>
    protected override void InitializeModule(params object?[] args)
    {
        // Only the check needs Dalamud, and it declines while NoireLib is uninitialized.
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

        if (args.Length > 8 && args[8] is bool shouldStopNotifyingAfterFirstNotification)
            ShouldStopNotifyingAfterFirstNotification = shouldStopNotifyingAfterFirstNotification;

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Update Tracker initialized.");
    }

    /// <summary>
    /// Called when the module is activated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> false to true.
    /// </summary>
    protected override void OnActivated()
    {
        StartUpdateCheckTimer();

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Update Tracker activated.");
    }

    /// <summary>
    /// Called when the module is deactivated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> true to false.
    /// </summary>
    protected override void OnDeactivated()
    {
        StopUpdateCheckTimer();

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Update Tracker deactivated.");
    }

    private string? repoUrl = null;

    /// <summary>
    /// The JSON repository to check. Empty stops the checks. A different URL reopens the notification gate and delays
    /// the next check by <see cref="CheckStartDelayMs"/>.
    /// </summary>
    public string? RepoUrl
    {
        get => repoUrl;
        set
        {
            if (string.Equals(repoUrl, value, StringComparison.Ordinal))
                return;

            repoUrl = value;
            HasShownUpdateNotification = false;

            if (IsActive)
                StartUpdateCheckTimer();
        }
    }

    /// <summary>Sets the repository URL to check for updates.</summary>
    /// <param name="repoUrl">The URL of the JSON repository.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireUpdateTracker SetRepoUrl(string repoUrl)
    {
        RepoUrl = repoUrl;
        return this;
    }

    /// <summary>Whether to print a message in chat when an update is detected.</summary>
    public bool ShouldPrintMessageInChatOnUpdate { get; set; } = true;

    /// <summary>Sets whether to print a message in chat when an update is detected.</summary>
    /// <param name="shouldPrint">Whether to print the message in chat.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireUpdateTracker SetShouldPrintMessageInChatOnUpdate(bool shouldPrint)
    {
        ShouldPrintMessageInChatOnUpdate = shouldPrint;
        return this;
    }

    /// <summary>Whether to show a notification when an update is detected.</summary>
    public bool ShouldShowNotificationOnUpdate { get; set; } = true;

    /// <summary>Sets whether to show a notification when an update is detected.</summary>
    /// <param name="shouldShow">Whether to show the notification.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireUpdateTracker SetShouldShowNotificationOnUpdate(bool shouldShow)
    {
        ShouldShowNotificationOnUpdate = shouldShow;
        return this;
    }

    /// <summary>
    /// Whether checking stops once a detected update reached a notification, a chat message or an EventBus event. With
    /// every channel off, checks keep running.
    /// </summary>
    public bool ShouldStopNotifyingAfterFirstNotification { get; set; } = true;

    /// <summary>
    /// Sets whether to stop notifying after the first shown update; see
    /// <see cref="ShouldStopNotifyingAfterFirstNotification"/> for what counts as shown.
    /// </summary>
    /// <param name="shouldStop">Whether to stop notifying after the first notification.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireUpdateTracker SetShouldStopNotifyingAfterFirstNotification(bool shouldStop)
    {
        ShouldStopNotifyingAfterFirstNotification = shouldStop;
        return this;
    }

    /// <summary>Whether a detected update reached a channel, closing the gate <see cref="ShouldStopNotifyingAfterFirstNotification"/> sets.</summary>
    public bool HasShownUpdateNotification { get; private set; } = false;

    /// <summary>Reopens the notification gate. Checks continue on the existing schedule.</summary>
    /// <returns>This module.</returns>
    public NoireUpdateTracker ResetUpdateNotification()
    {
        HasShownUpdateNotification = false;
        return this;
    }

    /// <summary>The chat message printed on an update, with <see cref="UpdateTrackerTextTags"/>. Null uses the default.</summary>
    public string? Message { get; set; } = null;

    /// <summary>Sets <see cref="Message"/>.</summary>
    /// <param name="message">The message, or null for the default.</param>
    /// <returns>This module.</returns>
    public NoireUpdateTracker SetMessage(string? message)
    {
        Message = message;
        return this;
    }

    /// <summary>The notification title, with <see cref="UpdateTrackerTextTags"/>. Null uses the default.</summary>
    public string? NotificationTitle { get; set; } = null;

    /// <summary>Sets <see cref="NotificationTitle"/>.</summary>
    /// <param name="title">The title, or null for the default.</param>
    /// <returns>This module.</returns>
    public NoireUpdateTracker SetNotificationTitle(string? title)
    {
        NotificationTitle = title;
        return this;
    }

    /// <summary>The notification text, with <see cref="UpdateTrackerTextTags"/>. Null uses the default.</summary>
    public string? NotificationMessage { get; set; } = null;

    /// <summary>Sets <see cref="NotificationMessage"/>.</summary>
    /// <param name="notificationMessage">The text, or null for the default.</param>
    /// <returns>This module.</returns>
    public NoireUpdateTracker SetNotificationMessage(string? notificationMessage)
    {
        NotificationMessage = notificationMessage;
        return this;
    }

    /// <summary>How long the notification shows, in milliseconds. Default 30000.</summary>
    public int NotificationDurationMs { get; set; } = 30000;

    /// <summary>Sets <see cref="NotificationDurationMs"/>.</summary>
    /// <param name="durationMs">The duration, in milliseconds.</param>
    /// <returns>This module.</returns>
    public NoireUpdateTracker SetNotificationDurationMs(int durationMs)
    {
        NotificationDurationMs = durationMs;
        return this;
    }

    private int checkIntervalMinutes = 30;

    /// <summary>Minutes between checks. Default 30. Changing it while active restarts the timer.</summary>
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

    /// <summary>Sets <see cref="CheckIntervalMinutes"/>.</summary>
    /// <param name="minutes">The interval, in minutes.</param>
    /// <returns>This module.</returns>
    public NoireUpdateTracker SetCheckIntervalMinutes(int minutes)
    {
        CheckIntervalMinutes = minutes;
        return this;
    }

    private int checkStartDelayMs = 2000;

    /// <summary>
    /// Milliseconds from the timer starting to its first check. Default 2000. A burst of configuration changes costs one
    /// check. A new value applies the next time the timer starts.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">When the value is negative.</exception>
    public int CheckStartDelayMs
    {
        get => checkStartDelayMs;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(CheckStartDelayMs), "Check start delay cannot be negative.");

            checkStartDelayMs = value;
        }
    }

    /// <summary>Sets <see cref="CheckStartDelayMs"/>.</summary>
    /// <param name="delayMs">The delay, in milliseconds.</param>
    /// <returns>This module.</returns>
    public NoireUpdateTracker SetCheckStartDelayMs(int delayMs)
    {
        CheckStartDelayMs = delayMs;
        return this;
    }

    private void StopUpdateCheckTimer()
    {
        Timer? timer;

        lock (timerLock)
        {
            timer = updateCheckTimer;
            updateCheckTimer = null;
        }

        DisposeTimerAndWait(timer);
    }

    // The callback only starts a check: this never waits on the network. False from Dispose means nothing will signal.
    private static void DisposeTimerAndWait(Timer? timer)
    {
        if (timer == null)
            return;

        using var timerDrained = new ManualResetEvent(false);

        if (timer.Dispose(timerDrained))
            timerDrained.WaitOne();
    }

    private void StartUpdateCheckTimer()
    {
        if (disposed)
        {
            if (EnableLogging)
                NoireLogger.LogDebug(this, "The update check timer stays stopped. The module is disposed.");

            return;
        }

        if (!IsActive)
        {
            NoireLogger.LogWarning(this, "Cannot start the update check timer. Module is deactivated.");
            return;
        }

        if (RepoUrl.IsNullOrWhitespace())
        {
            StopUpdateCheckTimer();

            if (EnableLogging)
                NoireLogger.LogDebug(this, "No repository URL is configured. The update check timer stays stopped.");

            return;
        }

        Timer? replaced;

        lock (timerLock)
        {
            // Under the lock the stops take: a start racing them leaves no timer behind.
            if (disposed || !IsActive)
                return;

            replaced = updateCheckTimer;
            updateCheckTimer = new Timer(static state => ((NoireUpdateTracker)state!).RunScheduledCheck(),
                this,
                TimeSpan.FromMilliseconds(CheckStartDelayMs),
                TimeSpan.FromMinutes(CheckIntervalMinutes));
        }

        DisposeTimerAndWait(replaced);
    }

    private void RunScheduledCheck() => RunScheduledCheck(CheckForUpdateAsync);

    // An exception escaping a timer callback terminates the process. The returned task never faults.
    internal Task RunScheduledCheck(Func<Task> check)
    {
        Task task;

        try
        {
            task = check();
        }
        catch (Exception ex)
        {
            task = Task.FromException(ex);
        }

        return task.ContinueWith(static (completed, state) =>
            {
                if (completed.IsFaulted)
                    NoireLogger.LogError((NoireUpdateTracker)state!, completed.Exception!.GetBaseException(), "Scheduled update check failed.");
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    #region EventBus Integration

    private void PublishEvent<TEvent>(TEvent eventData)
    {
        EventBus?.Publish(eventData);
    }

    #endregion

    /// <summary>
    /// Checks for an update now. Does nothing when disposed, inactive, without <see cref="RepoUrl"/> or NoireLib, or once
    /// the notification gate closed. The task never faults.
    /// </summary>
    /// <returns>A task completing once the check and its notifications finish.</returns>
    public Task CheckForUpdatesNowAsync() => CheckForUpdateAsync();

    private async Task CheckForUpdateAsync()
    {
        try
        {
            if (disposed)
            {
                if (EnableLogging)
                    NoireLogger.LogDebug(this, "Cannot check for updates. The module is disposed.");

                return;
            }

            if (!IsActive || RepoUrl.IsNullOrWhitespace() || (ShouldStopNotifyingAfterFirstNotification && HasShownUpdateNotification))
                return;

            if (!NoireService.IsInitialized())
            {
                NoireLogger.LogWarning(this, "Cannot check for updates: NoireLib is not initialized.");
                return;
            }

            var disposalToken = disposalTokenSource.Token;

            using var req = new HttpRequestMessage(HttpMethod.Get, RepoUrl);
            using var resp = await httpClient.SendAsync(req, disposalToken).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(disposalToken).ConfigureAwait(false);

            if (disposalToken.IsCancellationRequested)
                return;

            var entries = ParseRepositoryResponse(json);

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

            if (currentVersion >= remoteVersion)
                return;

            if (disposalToken.IsCancellationRequested)
                return;

            await AsyncHelper.RunOnFrameworkThreadAsync(() => ApplyUpdateDetected(currentVersion, remoteVersion)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Disposed while the check was in flight.
        }
        catch (ObjectDisposedException)
        {
            // Teardown landed between the check above and this call.
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, "Failed to check for updates.");
        }
    }

    // Framework thread only: reaches notifications and chat, and runs event subscribers inline.
    internal void ApplyUpdateDetected(Version currentVersion, Version remoteVersion)
    {
        if (EventBus != null)
            PublishEvent(new NewPluginVersionDetectedEvent(currentVersion, remoteVersion));

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
                XivChatType.Debug,
                ParseMessageTemplate(message, currentVersion.ToString(), remoteVersion.ToString()),
                ColorHelper.HexToVector3("#FCC203"));
        }

        if (DetectionReachesAChannel(EventBus != null, ShouldShowNotificationOnUpdate, ShouldPrintMessageInChatOnUpdate))
            HasShownUpdateNotification = true;
    }

    internal static bool DetectionReachesAChannel(bool hasEventBus, bool showsNotification, bool printsInChat)
        => hasEventBus || showsNotification || printsInChat;

    private string ParseMessageTemplate(string template, string currentVersion, string newVersion)
    {
        return template
            .Replace(UpdateTrackerTextTags.PluginInternalName, NoireService.PluginInterface.InternalName)
            .Replace(UpdateTrackerTextTags.CurrentVersion, currentVersion)
            .Replace(UpdateTrackerTextTags.NewVersion, newVersion);
    }

    /// <summary>Stops the checks. A second call does nothing.</summary>
    protected override void DisposeInternal()
    {
        if (disposed)
            return;

        disposed = true;
        disposalTokenSource.Cancel();

        StopUpdateCheckTimer();
        httpClient.Dispose();
        disposalTokenSource.Dispose();

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Update Tracker disposed.");
    }
}
