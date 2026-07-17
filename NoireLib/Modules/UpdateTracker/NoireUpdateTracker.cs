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
    /// <summary>
    /// The EventBus instance to publish events to.<br/>
    /// If <see langword="null"/>, no events will be published.
    /// </summary>
    public NoireEventBus? EventBus { get; set; }

    /// <summary>
    /// Reads the plugin repository response. The body is remote input, so this is built with
    /// <see cref="JsonSerializer.Create(JsonSerializerSettings)"/>, which resolves every setting from the object below
    /// alone. The <see cref="JsonConvert"/> overloads and <see cref="JsonSerializer.CreateDefault(JsonSerializerSettings)"/>
    /// instead merge in <see cref="JsonConvert.DefaultSettings"/>, a process-global that any other code loaded into
    /// this process can assign, which would let unrelated code decide how a remote response is read.<br/>
    /// TypeNameHandling stays None so a response can never name a type into existence.
    /// </summary>
    private static readonly JsonSerializer RepositoryReader = CreateRepositoryReader();

    private static JsonSerializer CreateRepositoryReader()
    {
        var serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
        });

        // A repository response is exactly one JSON document; trailing content means the body is malformed.
        serializer.CheckAdditionalContent = true;
        return serializer;
    }

    /// <summary>
    /// Reads a plugin repository response body into its entries.
    /// </summary>
    /// <param name="json">The repository response body.</param>
    /// <returns>The parsed entries, or null when the body carries no array.</returns>
    /// <exception cref="JsonException">Thrown when the body is not a well-formed repository response.</exception>
    internal static List<RepoEntry>? ParseRepositoryResponse(string json)
    {
        using var stringReader = new StringReader(json);
        using var jsonReader = new JsonTextReader(stringReader);

        return RepositoryReader.Deserialize<List<RepoEntry>>(jsonReader);
    }

    private readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Cancelled at the start of teardown. A check suspended on the HTTP call would otherwise resume against a disposed
    /// <see cref="httpClient"/> and go on to touch a module, and a NoireLib, that no longer exist.
    /// </summary>
    private readonly CancellationTokenSource disposalTokenSource = new();

    /// <summary>
    /// Latched at the start of teardown, before anything it protects is released, so no path that starts a check or a
    /// timer can treat a disposed module as a working one. It is latched here rather than read from <see cref="IsActive"/>
    /// because active state is cleared only once teardown has finished, leaving a window where the module is disposed and
    /// still reports itself active.<br/>
    /// It is also what makes teardown itself run at most once, so that a module disposed twice does not tear down
    /// resources it has already released.
    /// </summary>
    private volatile bool disposed;

    private Timer? updateCheckTimer;

    /// <summary>
    /// The default constructor needed for internal purposes.
    /// </summary>
    public NoireUpdateTracker() : base() { }

    /// <summary>
    /// Creates a new instance of the <see cref="NoireUpdateTracker"/> module.<br/>
    /// See <see cref="UpdateTrackerTextTags"/> to add dynamic content to messages and notifications.
    /// </summary>
    /// <param name="moduleId">The optional module identifier.</param>
    /// <param name="active">Whether the module should be active upon creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="repoUrl">The URL of the JSON repository to check for updates.</param>
    /// <param name="shouldPrintMessageInChatOnUpdate">Whether to print a message in chat when an update is detected.</param>
    /// <param name="shouldShowNotificationOnUpdate">Whether to show a notification when an update is detected.</param>
    /// <param name="message">The message to print in chat when an update is detected.<br/>Can use dynamic content tags.</param>
    /// <param name="notificationTitle">The title of the notification to show when an update is detected.<br/>Can use dynamic content tags.</param>
    /// <param name="notificationMessage">The message content of the notification to show when an update is detected.<br/>Can use dynamic content tags.</param>
    /// <param name="notificationDurationMs">The duration in milliseconds for which the update notification will be displayed.</param>
    /// <param name="eventBus">Optional EventBus instance to publish events. If null, no event will be published.</param>
    /// <param name="shouldStopNotifyingAfterFirstNotification">Whether to stop checking once a detected update has reached a notification channel.<br/>Declared after <paramref name="eventBus"/> so that callers already passing the earlier parameters positionally keep binding them to the same options.</param>
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

    /// <summary>
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>.<br/>
    /// Only used for internal module management.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    internal NoireUpdateTracker(ModuleId? moduleId, bool active = true, bool enableLogging = true) : base(moduleId, active, enableLogging) { }

    /// <summary>
    /// Initializes the module with optional initialization parameters.
    /// </summary>
    /// <param name="args">The initialization parameters</param>
    protected override void InitializeModule(params object?[] args)
    {
        // Nothing here needs NoireLib to be initialized: recording how the module should behave requires no Dalamud
        // service. The update check is the part that genuinely does need one, and it declines and says so while NoireLib
        // is uninitialized, so construction order is a non-issue rather than something a consumer has to get right.
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
    /// The URL of the JSON repository to check for updates.<br/>
    /// While this is null or whitespace there is nothing to fetch, so the update check timer stays stopped instead of
    /// waking every <see cref="CheckIntervalMinutes"/> to do nothing. Assigning a URL while the module is active starts
    /// the timer, and clearing it stops the timer again.<br/>
    /// Assigning a different URL reopens the <see cref="ShouldStopNotifyingAfterFirstNotification"/> gate, and the
    /// first check against the new repository runs <see cref="CheckStartDelayMs"/> later rather than at the end of the
    /// current interval. Assigning the URL it already holds does neither, so a consumer that writes this every frame
    /// from its own configuration costs nothing.<br/>
    /// Assigning this on a disposed module records the URL but starts no timer, since there is nothing left to check
    /// with.
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

    /// <summary>
    /// Whether to show a notification when an update is detected.
    /// </summary>
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
    /// Whether to stop checking for updates once a detected update has been shown.<br/>
    /// An update counts as shown when the check carried it to at least one channel: a notification toast, a chat
    /// message, or a <see cref="NewPluginVersionDetectedEvent"/> handed to <see cref="EventBus"/> subscribers. The
    /// event bus counts because a subscriber receives the detection and decides what to present, which is the same
    /// role the two built-in channels play; a tracker configured to report only through the event bus would otherwise
    /// never satisfy this gate and would keep polling forever.<br/>
    /// Detecting a newer version while every channel is off (both <see cref="ShouldShowNotificationOnUpdate"/> and
    /// <see cref="ShouldPrintMessageInChatOnUpdate"/> disabled with no <see cref="EventBus"/> attached) shows nothing
    /// and therefore leaves this gate open, so that checks keep running and the next detection can still be reported
    /// once a channel is configured.<br/>
    /// The gate closes at most once per shown update, not once per session: <see cref="HasShownUpdateNotification"/>
    /// reports whether it is closed, assigning a different <see cref="RepoUrl"/> reopens it, and
    /// <see cref="ResetUpdateNotification"/> reopens it on demand.
    /// </summary>
    public bool ShouldStopNotifyingAfterFirstNotification { get; set; } = true;

    /// <summary>
    /// Sets whether to stop notifying after the first update notification has been shown.<br/>
    /// See <see cref="ShouldStopNotifyingAfterFirstNotification"/> for what counts as shown.
    /// </summary>
    /// <param name="shouldStop">Whether to stop notifying after the first notification.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireUpdateTracker SetShouldStopNotifyingAfterFirstNotification(bool shouldStop)
    {
        ShouldStopNotifyingAfterFirstNotification = shouldStop;
        return this;
    }

    /// <summary>
    /// Whether a detected update has already been carried to at least one notification channel, which is what
    /// <see cref="ShouldStopNotifyingAfterFirstNotification"/> gates further checks on. While this is true and that
    /// option is enabled, no check runs.<br/>
    /// See <see cref="ShouldStopNotifyingAfterFirstNotification"/> for what counts as shown. Assigning a different
    /// <see cref="RepoUrl"/> clears this, and <see cref="ResetUpdateNotification"/> clears it on demand.
    /// </summary>
    public bool HasShownUpdateNotification { get; private set; } = false;

    /// <summary>
    /// Reopens the <see cref="ShouldStopNotifyingAfterFirstNotification"/> gate, so that the next detected update is
    /// reported again even though an earlier one already reached a notification channel.<br/>
    /// Assigning a different <see cref="RepoUrl"/> already does this, since a notification shown for one repository
    /// says nothing about another. Call this when something else that the shown notification was about has changed, or
    /// to report a still-pending update again after the user has dismissed it.<br/>
    /// The automatic checks resume on their existing schedule. To check immediately instead, follow this with
    /// <see cref="CheckForUpdatesNowAsync"/>.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireUpdateTracker ResetUpdateNotification()
    {
        HasShownUpdateNotification = false;
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
    /// Example: $"{UpdateTrackerTextTags.PluginInternalName} has a new update available.\nCurrent version: {UpdateTrackerTextTags.CurrentVersion}\nNew version: {UpdateTrackerTextTags.NewVersion}"<br/>
    /// Set to <see langword="null"/> to use the default content.
    /// </summary>
    public string? NotificationMessage { get; set; } = null;

    /// <summary>
    /// Sets the message content of the notification to show when an update is detected.<br/>
    /// Use <see cref="UpdateTrackerTextTags"/> tags for dynamic content.<br/>
    /// Example: $"{UpdateTrackerTextTags.PluginInternalName} has a new update available.\nCurrent version: {UpdateTrackerTextTags.CurrentVersion}\nNew version: {UpdateTrackerTextTags.NewVersion}"<br/>
    /// Set to <see langword="null"/> to use the default content.
    /// </summary>
    /// <param name="notificationMessage">The notification message content.</param>
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
    /// Default is 30 minutes.<br/>
    /// Assigning this while the module is active restarts the timer, so the new interval applies from the next check
    /// rather than from the end of the one already in flight. That next check runs <see cref="CheckStartDelayMs"/>
    /// later.<br/>
    /// Assigning this on a disposed module records the interval but starts no timer, since there is nothing left to
    /// check with.
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

    private int checkStartDelayMs = 2000;

    /// <summary>
    /// The delay in milliseconds between the update check timer starting and the first check it runs.<br/>
    /// Every path that starts the timer restarts this delay: activating the module, and assigning
    /// <see cref="RepoUrl"/> or <see cref="CheckIntervalMinutes"/> while it is active. A run of configuration changes
    /// therefore costs one check once the values settle rather than one request per change, which is what a URL
    /// assigned from a text field that fires on every keystroke would otherwise produce.<br/>
    /// It is also what makes reconfiguration take effect promptly: the first check against a newly assigned repository
    /// runs this long after it was assigned, instead of waiting out a whole <see cref="CheckIntervalMinutes"/>
    /// interval.<br/>
    /// Default is 2000 ms. Set to 0 to check the moment the timer starts, accepting one request per configuration
    /// change. A new value applies the next time the timer starts, since applying it any sooner would mean restarting
    /// the timer, which is itself a scheduled check.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is negative.</exception>
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

    /// <summary>
    /// Sets the delay in milliseconds between the update check timer starting and the first check it runs.<br/>
    /// Default is 2000 ms. See <see cref="CheckStartDelayMs"/> for what restarts the delay and when a new value
    /// applies.
    /// </summary>
    /// <param name="delayMs">The delay in milliseconds.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireUpdateTracker SetCheckStartDelayMs(int delayMs)
    {
        CheckStartDelayMs = delayMs;
        return this;
    }

    private void StopUpdateCheckTimer()
    {
        updateCheckTimer?.Dispose();
        updateCheckTimer = null;
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

        StopUpdateCheckTimer();

        updateCheckTimer = new Timer(async _ => await CheckForUpdateAsync(),
            null,
            TimeSpan.FromMilliseconds(CheckStartDelayMs),
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

    /// <summary>
    /// Checks for an update immediately, rather than waiting for the next scheduled check.<br/>
    /// The returned task completes once the check has finished and its notifications have been delivered, so a caller
    /// can await it to re-enable the control that started it. It never faults: a check reports its own failures
    /// through the log, which is what the scheduled checks already rely on, so discarding this task is as safe as
    /// awaiting it.<br/>
    /// The check runs under exactly the rules a scheduled one does, and so does nothing when the module is disposed,
    /// when the module is inactive, when <see cref="RepoUrl"/> is not configured, when NoireLib is not initialized, or
    /// when <see cref="ShouldStopNotifyingAfterFirstNotification"/> has already closed on a shown update. Call
    /// <see cref="ResetUpdateNotification"/> first to check past that last one.<br/>
    /// The schedule of the automatic checks is left alone.
    /// </summary>
    /// <returns>A task that completes when the check has finished.</returns>
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
            // The module was disposed while the check was in flight. Expected, and there is nothing left to report to.
        }
        catch (ObjectDisposedException)
        {
            // Teardown landed between the disposed check above and this check reaching the token source or the HTTP
            // client, both of which it disposes. The same benign case as the cancellation above, and reported the same
            // way: the module is gone, which is not a failure of the check.
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, "Failed to check for updates.");
        }
    }

    /// <summary>
    /// Carries a detected update to every configured notification channel and closes the
    /// <see cref="ShouldStopNotifyingAfterFirstNotification"/> gate if at least one of them took it.<br/>
    /// Framework thread only: this reaches the notification manager and the chat log, and it hands
    /// <see cref="NewPluginVersionDetectedEvent"/> to event bus subscribers, which run inline on the calling thread.
    /// </summary>
    /// <param name="currentVersion">The currently installed plugin version.</param>
    /// <param name="remoteVersion">The newer version found in the repository.</param>
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
                XivChatType.Echo,
                ParseMessageTemplate(message, currentVersion.ToString(), remoteVersion.ToString()),
                ColorHelper.HexToVector3("#FCC203"));
        }

        if (DetectionReachesAChannel(EventBus != null, ShouldShowNotificationOnUpdate, ShouldPrintMessageInChatOnUpdate))
            HasShownUpdateNotification = true;
    }

    /// <summary>
    /// Whether a detected update reaches at least one notification channel, and therefore whether the
    /// <see cref="ShouldStopNotifyingAfterFirstNotification"/> gate has a delivery to close on.<br/>
    /// This is the whole rule behind that gate, kept in one place so that the decision to stop checking is made from
    /// what a detection actually carries rather than from the fact that a version was newer. See
    /// <see cref="ShouldStopNotifyingAfterFirstNotification"/> for why an attached event bus counts.
    /// </summary>
    /// <param name="hasEventBus">Whether an <see cref="EventBus"/> is attached to receive the detection.</param>
    /// <param name="showsNotification">The value of <see cref="ShouldShowNotificationOnUpdate"/>.</param>
    /// <param name="printsInChat">The value of <see cref="ShouldPrintMessageInChatOnUpdate"/>.</param>
    /// <returns>True when at least one channel carries the detection; otherwise, false.</returns>
    internal static bool DetectionReachesAChannel(bool hasEventBus, bool showsNotification, bool printsInChat)
        => hasEventBus || showsNotification || printsInChat;

    private string ParseMessageTemplate(string template, string currentVersion, string newVersion)
    {
        return template
            .Replace(UpdateTrackerTextTags.PluginInternalName, NoireService.PluginInterface.InternalName)
            .Replace(UpdateTrackerTextTags.CurrentVersion, currentVersion)
            .Replace(UpdateTrackerTextTags.NewVersion, newVersion);
    }

    /// <summary>
    /// Internal dispose method called when the module is disposed.<br/>
    /// Runs once. A second call returns having done nothing, since a module is reachable for teardown both from the
    /// consumer that owns it and from the library tearing its modules down.
    /// </summary>
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
