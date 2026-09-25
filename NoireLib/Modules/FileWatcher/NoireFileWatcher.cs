using Dalamud.Plugin.Services;
using NoireLib.Core.Modules;
using NoireLib.EventBus;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.FileWatcher;

/// <summary>
/// Watches directories and files, with several registrations, callbacks, duplicate suppression and EventBus events.
/// Callbacks and events run on the framework thread, one at a time, and never after disposal.
/// </summary>
public class NoireFileWatcher : NoireModuleBase<NoireFileWatcher>
{
    #region Private Properties and Fields

    private readonly Dictionary<string, WatchRegistration> watchRegistrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FileSystemWatcher, string> watcherToWatchId = new();
    private readonly Dictionary<string, string> keyToWatchId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> recentNotificationCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object watchLock = new();

    private readonly ConcurrentQueue<Action> deliveryQueue = new();
    private int queuedDeliveryCount;
    private readonly object deliveryPumpLock = new();
    private int deliveryPumpAttached;
    private int deliveryOverflowReported;
    private int deliveriesInFlight;
    private int disposed;

    // The deliveries this thread is inside, innermost last: a callback may dispose its own module mid-delivery.
    [ThreadStatic]
    private static List<NoireFileWatcher>? DeliveriesOnThisThread;

    private long totalRegistrations;
    private long totalRemoved;
    private long totalNotificationsObserved;
    private long totalNotificationsDispatched;
    private long totalErrors;
    private long totalDuplicateNotificationsSuppressed;
    private long totalCallbackExceptionsCaught;
    private long totalDeliveriesDropped;

    private long duplicateCacheSweepCounter;

    #endregion

    #region Public Properties and Constructors

    /// <summary>The EventBus watcher events are published to, or null for CLR events and callbacks only.</summary>
    public NoireEventBus? EventBus { get; set; } = null;

    /// <summary>The default constructor needed for internal purposes.</summary>
    public NoireFileWatcher() : base() { }

    /// <summary>Creates a new instance of the <see cref="NoireFileWatcher"/> module.</summary>
    /// <param name="moduleId">The optional module identifier.</param>
    /// <param name="active">Whether the module should be active upon creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="eventBus">Optional EventBus used to publish module events.</param>
    /// <param name="autoEnableNewWatches">Whether newly created watches start automatically while active.</param>
    /// <param name="suppressDuplicateNotifications">Whether duplicate notifications are suppressed.</param>
    /// <param name="duplicateNotificationWindowMs">Duplicate suppression window in milliseconds.</param>
    public NoireFileWatcher(
        string? moduleId = null,
        bool active = true,
        bool enableLogging = true,
        NoireEventBus? eventBus = null,
        bool autoEnableNewWatches = true,
        bool suppressDuplicateNotifications = true,
        int duplicateNotificationWindowMs = 100)
        : base(moduleId, active, enableLogging, eventBus, autoEnableNewWatches, suppressDuplicateNotifications, duplicateNotificationWindowMs) { }

    internal NoireFileWatcher(ModuleId? moduleId, bool active = true, bool enableLogging = true)
        : base(moduleId, active, enableLogging) { }

    #endregion

    #region Module Lifecycle Methods

    /// <summary>Initializes the module with optional initialization parameters.</summary>
    protected override void InitializeModule(params object?[] args)
    {
        if (args.Length > 0 && args[0] is NoireEventBus eventBus)
            EventBus = eventBus;

        if (args.Length > 1 && args[1] is bool autoEnable)
            autoEnableNewWatches = autoEnable;

        if (args.Length > 2 && args[2] is bool suppressDuplicates)
            suppressDuplicateNotifications = suppressDuplicates;

        if (args.Length > 3 && args[3] is int duplicateWindowMs)
            DuplicateNotificationWindow = TimeSpan.FromMilliseconds(duplicateWindowMs);

        if (EnableLogging)
            NoireLogger.LogInfo(this, "FileWatcher module initialized.");
    }

    /// <summary>
    /// Called when the module is activated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> false to true.
    /// </summary>
    protected override void OnActivated()
    {
        lock (watchLock)
        {
            foreach (var registration in watchRegistrations.Values)
            {
                if (registration.IsEnabled)
                    registration.Watcher.EnableRaisingEvents = true;
            }
        }

        if (EnableLogging)
            NoireLogger.LogInfo(this, "FileWatcher module activated.");
    }

    /// <summary>
    /// Called when the module is deactivated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> true to false.
    /// </summary>
    protected override void OnDeactivated()
    {
        lock (watchLock)
        {
            foreach (var registration in watchRegistrations.Values)
                registration.Watcher.EnableRaisingEvents = false;
        }

        if (EnableLogging)
            NoireLogger.LogInfo(this, "FileWatcher module deactivated.");
    }

    #endregion

    #region Module Configuration Management

    private bool autoEnableNewWatches = true;
    /// <summary>Whether newly added watches should be automatically started while the module is active.</summary>
    public bool AutoEnableNewWatches
    {
        get => autoEnableNewWatches;
        set => autoEnableNewWatches = value;
    }

    /// <summary>Sets whether newly created watches should auto-start while active.</summary>
    public NoireFileWatcher SetAutoEnableNewWatches(bool autoEnable)
    {
        AutoEnableNewWatches = autoEnable;
        return this;
    }

    private bool suppressDuplicateNotifications = true;
    /// <summary>
    /// Whether duplicate notifications should be suppressed within <see cref="DuplicateNotificationWindow"/>.
    /// </summary>
    public bool SuppressDuplicateNotifications
    {
        get => suppressDuplicateNotifications;
        set => suppressDuplicateNotifications = value;
    }

    private TimeSpan duplicateNotificationWindow = TimeSpan.FromMilliseconds(100);
    /// <summary>Time window used for duplicate notification suppression.</summary>
    public TimeSpan DuplicateNotificationWindow
    {
        get => duplicateNotificationWindow;
        set => duplicateNotificationWindow = value <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : value;
    }

    /// <summary>Sets duplicate suppression behavior.</summary>
    public NoireFileWatcher SetDuplicateSuppression(bool enabled, TimeSpan? window = null)
    {
        SuppressDuplicateNotifications = enabled;
        if (window.HasValue)
            DuplicateNotificationWindow = window.Value;
        return this;
    }

    #endregion

    #region Public Events

    /// <summary>
    /// Raised for every notification dispatched by the module, on the framework thread with no overlap.
    /// </summary>
    public event Action<FileWatchNotification>? NotificationReceived;

    /// <summary>Raised for changed notifications, on the framework thread with no overlap.</summary>
    public event Action<FileWatchNotification>? Changed;

    /// <summary>Raised for created notifications, on the framework thread with no overlap.</summary>
    public event Action<FileWatchNotification>? Created;

    /// <summary>Raised for deleted notifications, on the framework thread with no overlap.</summary>
    public event Action<FileWatchNotification>? Deleted;

    /// <summary>Raised for renamed notifications, on the framework thread with no overlap.</summary>
    public event Action<FileWatchNotification>? Renamed;

    /// <summary>Raised for watcher-level errors, on the framework thread with no overlap.</summary>
    public event Action<FileWatchError>? Error;

    #endregion

    #region Public API Methods

    /// <summary>Registers a directory watch, delivered as <see cref="Watch"/> describes.</summary>
    /// <param name="directoryPath">The directory to watch.</param>
    /// <param name="callback">Called for every matching notification.</param>
    /// <param name="asyncCallback">Started for every matching notification.</param>
    /// <param name="owner">The owner the callbacks are removed with.</param>
    /// <param name="key">A key; any watch already holding it is replaced.</param>
    /// <param name="patterns">Glob patterns the files must match. All files when empty.</param>
    /// <param name="includeSubdirectories">Whether subdirectories are watched too.</param>
    /// <returns>The watch ID.</returns>
    public string WatchDirectory(
        string directoryPath,
        Action<FileWatchNotification>? callback = null,
        Func<FileWatchNotification, Task>? asyncCallback = null,
        object? owner = null,
        string? key = null,
        IReadOnlyCollection<string>? patterns = null,
        bool includeSubdirectories = true)
    {
        var options = new FileWatchRegistrationOptions
        {
            Path = directoryPath,
            TargetType = FileWatchTargetType.Directory,
            IncludeSubdirectories = includeSubdirectories,
            Key = key,
            Patterns = patterns?.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? ["*"],
        };

        return Watch(options, callback, asyncCallback, owner);
    }

    /// <summary>Registers a single-file watch, delivered as <see cref="Watch"/> describes.</summary>
    /// <param name="filePath">The file to watch.</param>
    /// <param name="callback">Called for every notification on the file.</param>
    /// <param name="asyncCallback">Started for every notification on the file.</param>
    /// <param name="owner">The owner the callbacks are removed with.</param>
    /// <param name="key">A key; any watch already holding it is replaced.</param>
    /// <returns>The watch ID.</returns>
    public string WatchFile(
        string filePath,
        Action<FileWatchNotification>? callback = null,
        Func<FileWatchNotification, Task>? asyncCallback = null,
        object? owner = null,
        string? key = null)
    {
        return Watch(new FileWatchRegistrationOptions
        {
            Path = filePath,
            TargetType = FileWatchTargetType.File,
            IncludeSubdirectories = false,
            Key = key,
            Patterns = [Path.GetFileName(filePath)],
        }, callback, asyncCallback, owner);
    }

    /// <summary>
    /// Registers a watch. Callbacks run on the framework thread one at a time, never once retired, and on the observing
    /// thread when NoireLib is not initialized. On a disposed module the returned ID has no watch behind it.
    /// </summary>
    /// <param name="options">The watch to register.</param>
    /// <param name="callback">Called for every matching notification.</param>
    /// <param name="asyncCallback">Started for every matching notification.</param>
    /// <param name="owner">The owner the callbacks are removed with.</param>
    /// <returns>The watch ID.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">When the options carry an empty path.</exception>
    public string Watch(
        FileWatchRegistrationOptions options,
        Action<FileWatchNotification>? callback = null,
        Func<FileWatchNotification, Task>? asyncCallback = null,
        object? owner = null)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(options.Path))
            throw new ArgumentException("Path cannot be empty.", nameof(options));

        var normalizedPath = NormalizePath(options.Path);
        var targetType = ResolveTargetType(normalizedPath, options.TargetType);
        ValidatePathExists(normalizedPath, targetType, options.AllowNonExistingPath);

        var watcher = CreateWatcher(normalizedPath, targetType, options);
        var watchId = Guid.NewGuid().ToString("N");
        var rootPath = targetType == FileWatchTargetType.File
            ? (Path.GetDirectoryName(normalizedPath) ?? normalizedPath)
            : normalizedPath;

        var registration = new WatchRegistration(
            watchId,
            normalizedPath,
            rootPath,
            targetType,
            options,
            watcher,
            options.StartEnabled);

        if (callback != null)
            registration.Callbacks.Add(new CallbackRegistration(new FileWatchCallbackToken(Guid.NewGuid()), callback, owner, isAsync: false));

        if (asyncCallback != null)
            registration.Callbacks.Add(new CallbackRegistration(new FileWatchCallbackToken(Guid.NewGuid()), asyncCallback, owner, isAsync: true));

        FileWatchRegistrationInfo registeredInfo;
        WatchRegistration? displacedRegistration = null;

        lock (watchLock)
        {
            // The watch already holding the key is retired in the lock that inserts the new one: two racing calls cannot both win.
            if (!string.IsNullOrWhiteSpace(options.Key)
                && keyToWatchId.TryGetValue(options.Key!, out var displacedWatchId)
                && watchRegistrations.TryGetValue(displacedWatchId, out displacedRegistration))
            {
                UnindexRegistration(displacedRegistration);
            }

            watchRegistrations[watchId] = registration;
            watcherToWatchId[watcher] = watchId;

            if (!string.IsNullOrWhiteSpace(options.Key))
                keyToWatchId[options.Key!] = watchId;

            // Under the lock: another thread may be adding callbacks.
            registeredInfo = ToInfo(registration);
        }

        Interlocked.Increment(ref totalRegistrations);

        // Disposed outside the lock: FileSystemWatcher.Dispose can block. Its removal is announced before the new watch.
        if (displacedRegistration != null)
        {
            displacedRegistration.Watcher.EnableRaisingEvents = false;
            displacedRegistration.Watcher.Dispose();

            Interlocked.Increment(ref totalRemoved);

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Replaced the watch on key '{options.Key}' at path '{displacedRegistration.WatchedPath}'.");

            var replacedEvent = new FileWatchRemovedEvent(
                displacedRegistration.WatchId,
                displacedRegistration.WatchedPath,
                displacedRegistration.Options.Key);

            PostDelivery(() => PublishEvent(replacedEvent));
        }

        if (EnableLogging)
            NoireLogger.LogInfo(this, $"Registered watch '{watchId}' on path '{normalizedPath}'.");

        var registeredEvent = new FileWatchRegisteredEvent(registeredInfo);
        PostDelivery(() => PublishEvent(registeredEvent));

        // Raising starts once the registration event is queued: a subscriber hears of a watch before its first notification.
        var abandoned = false;

        lock (watchLock)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                // A registration landing after the disposal sweep would leak its FileSystemWatcher.
                UnindexRegistration(registration);
                abandoned = true;
            }
            else if (IsActive && AutoEnableNewWatches && registration.IsEnabled && watchRegistrations.ContainsKey(watchId))
            {
                watcher.EnableRaisingEvents = true;
            }
        }

        if (abandoned)
            watcher.Dispose();

        return watchId;
    }

    /// <summary>Adds a callback to a watch, invoked as <see cref="Watch"/> describes.</summary>
    /// <param name="watchId">The watch to attach to.</param>
    /// <param name="callback">Called for every matching notification.</param>
    /// <param name="owner">The owner the callback is removed with.</param>
    /// <returns>The token <see cref="RemoveCallback"/> takes.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="callback"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">When no watch has this ID.</exception>
    public FileWatchCallbackToken AddCallback(string watchId, Action<FileWatchNotification> callback, object? owner = null)
    {
        if (callback == null)
            throw new ArgumentNullException(nameof(callback));

        lock (watchLock)
        {
            if (!watchRegistrations.TryGetValue(watchId, out var registration))
                throw new KeyNotFoundException($"Watch '{watchId}' does not exist.");

            var token = new FileWatchCallbackToken(Guid.NewGuid());
            registration.Callbacks.Add(new CallbackRegistration(token, callback, owner, isAsync: false));
            return token;
        }
    }

    /// <summary>Adds an async callback to a watch. A faulted task is caught and logged.</summary>
    /// <param name="watchId">The watch to attach to.</param>
    /// <param name="callback">Started for every matching notification.</param>
    /// <param name="owner">The owner the callback is removed with.</param>
    /// <returns>The token <see cref="RemoveCallback"/> takes.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="callback"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">When no watch has this ID.</exception>
    public FileWatchCallbackToken AddAsyncCallback(string watchId, Func<FileWatchNotification, Task> callback, object? owner = null)
    {
        if (callback == null)
            throw new ArgumentNullException(nameof(callback));

        lock (watchLock)
        {
            if (!watchRegistrations.TryGetValue(watchId, out var registration))
                throw new KeyNotFoundException($"Watch '{watchId}' does not exist.");

            var token = new FileWatchCallbackToken(Guid.NewGuid());
            registration.Callbacks.Add(new CallbackRegistration(token, callback, owner, isAsync: true));
            return token;
        }
    }

    /// <summary>Removes a callback from all watches by token.</summary>
    public bool RemoveCallback(FileWatchCallbackToken token)
    {
        lock (watchLock)
        {
            foreach (var registration in watchRegistrations.Values)
            {
                var removed = registration.Callbacks.RemoveAll(c => c.Token.Equals(token));
                if (removed > 0)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Removes all callbacks owned by a specific owner.</summary>
    public int RemoveCallbacksByOwner(object owner)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));

        var removedCount = 0;

        lock (watchLock)
        {
            foreach (var registration in watchRegistrations.Values)
                removedCount += registration.Callbacks.RemoveAll(c => ReferenceEquals(c.Owner, owner));
        }

        return removedCount;
    }

    /// <summary>Enables or disables a watch.</summary>
    /// <param name="watchId">The watch to toggle.</param>
    /// <param name="enabled">Whether the watch raises events.</param>
    /// <returns>Whether the watch existed.</returns>
    public bool SetWatchEnabled(string watchId, bool enabled)
    {
        lock (watchLock)
        {
            if (!watchRegistrations.TryGetValue(watchId, out var registration))
                return false;

            registration.IsEnabled = enabled;
            registration.Watcher.EnableRaisingEvents = IsActive && enabled;
        }

        var stateChangedEvent = new FileWatchStateChangedEvent(watchId, enabled);
        PostDelivery(() => PublishEvent(stateChangedEvent));
        return true;
    }

    /// <summary>Enables every watch, publishing a <see cref="FileWatchStateChangedEvent"/> for each one that changed.</summary>
    /// <returns>This module.</returns>
    public NoireFileWatcher EnableAllWatches() => SetAllWatchesEnabled(true);

    /// <summary>Disables every watch, publishing a <see cref="FileWatchStateChangedEvent"/> for each one that changed.</summary>
    /// <returns>This module.</returns>
    public NoireFileWatcher DisableAllWatches() => SetAllWatchesEnabled(false);

    private NoireFileWatcher SetAllWatchesEnabled(bool enabled)
    {
        List<string> changedWatchIds = [];

        lock (watchLock)
        {
            foreach (var registration in watchRegistrations.Values)
            {
                if (registration.IsEnabled != enabled)
                    changedWatchIds.Add(registration.WatchId);

                registration.IsEnabled = enabled;
                registration.Watcher.EnableRaisingEvents = IsActive && enabled;
            }
        }

        // After the lock: an inline delivery runs subscribers on this thread, and they may call back in.
        foreach (var watchId in changedWatchIds)
        {
            var stateChangedEvent = new FileWatchStateChangedEvent(watchId, enabled);
            PostDelivery(() => PublishEvent(stateChangedEvent));
        }

        return this;
    }

    /// <summary>Removes a watch. Its undelivered notifications are discarded.</summary>
    /// <param name="watchId">The watch to remove.</param>
    /// <returns>Whether the watch existed.</returns>
    public bool RemoveWatch(string watchId)
    {
        WatchRegistration? registration = null;

        lock (watchLock)
        {
            if (!watchRegistrations.TryGetValue(watchId, out registration))
                return false;

            UnindexRegistration(registration);
        }

        // Outside the lock: FileSystemWatcher.Dispose can block, and the delivery path takes the lock every frame.
        registration.Watcher.EnableRaisingEvents = false;
        registration.Watcher.Dispose();

        Interlocked.Increment(ref totalRemoved);

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Removed watch '{watchId}' on path '{registration.WatchedPath}'.");

        var removedEvent = new FileWatchRemovedEvent(watchId, registration.WatchedPath, registration.Options.Key);
        PostDelivery(() => PublishEvent(removedEvent));
        return true;
    }

    /// <summary>Removes a watch by key, as <see cref="RemoveWatch"/> does.</summary>
    /// <param name="key">The key of the watch to remove.</param>
    /// <returns>Whether a watch held the key.</returns>
    public bool RemoveWatchByKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        string? watchId;
        lock (watchLock)
        {
            if (!keyToWatchId.TryGetValue(key, out watchId))
                return false;
        }

        return RemoveWatch(watchId);
    }

    /// <summary>Removes every watch: one <see cref="FileWatchRemovedEvent"/> each, then one <see cref="FileWatchesClearedEvent"/>.</summary>
    /// <returns>This module.</returns>
    public NoireFileWatcher ClearAllWatches()
    {
        List<string> watchIds;
        lock (watchLock)
            watchIds = watchRegistrations.Keys.ToList();

        foreach (var watchId in watchIds)
            RemoveWatch(watchId);

        var clearedEvent = new FileWatchesClearedEvent(watchIds.Count);
        PostDelivery(() => PublishEvent(clearedEvent));
        return this;
    }

    /// <summary>Gets an immutable snapshot of all registrations.</summary>
    public IReadOnlyList<FileWatchRegistrationInfo> GetWatches()
    {
        lock (watchLock)
            return watchRegistrations.Values.Select(ToInfo).ToList();
    }

    /// <summary>Gets one registration by watch ID.</summary>
    public FileWatchRegistrationInfo? GetWatch(string watchId)
    {
        lock (watchLock)
        {
            if (!watchRegistrations.TryGetValue(watchId, out var registration))
                return null;

            return ToInfo(registration);
        }
    }

    /// <summary>Gets one registration by key.</summary>
    public FileWatchRegistrationInfo? GetWatchByKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        lock (watchLock)
        {
            if (!keyToWatchId.TryGetValue(key, out var watchId))
                return null;

            if (!watchRegistrations.TryGetValue(watchId, out var registration))
                return null;

            return ToInfo(registration);
        }
    }

    /// <summary>Gets aggregated statistics about this module instance.</summary>
    public FileWatcherStatistics GetStatistics()
    {
        lock (watchLock)
        {
            return new FileWatcherStatistics(
                RegisteredWatches: watchRegistrations.Count,
                EnabledWatches: watchRegistrations.Values.Count(w => w.IsEnabled),
                TotalRegistrations: totalRegistrations,
                TotalRemoved: totalRemoved,
                TotalNotificationsObserved: totalNotificationsObserved,
                TotalNotificationsDispatched: totalNotificationsDispatched,
                TotalErrors: totalErrors,
                TotalDuplicateNotificationsSuppressed: totalDuplicateNotificationsSuppressed,
                TotalCallbackExceptionsCaught: totalCallbackExceptionsCaught)
            {
                TotalDeliveriesDropped = totalDeliveriesDropped,
            };
        }
    }

    #endregion

    #region Framework Thread Delivery

    // Beyond this many pending deliveries the oldest is dropped: an event storm outpaces frames.
    internal const int DeliveryQueueCapacity = 4096;

    // Test seam: queues deliveries without NoireLib. Only DrainDeliveryQueue runs them.
    internal bool ForceQueuedDelivery { get; set; } = false;

    private bool InlineDelivery => !NoireService.IsInitialized() && !ForceQueuedDelivery;

    internal void PostDelivery(Action delivery)
    {
        if (Volatile.Read(ref disposed) != 0)
            return;

        if (InlineDelivery)
        {
            RunDelivery(delivery);
            return;
        }

        if (Interlocked.Increment(ref queuedDeliveryCount) > DeliveryQueueCapacity)
        {
            // The newest notification describes the path as it is now.
            if (deliveryQueue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref queuedDeliveryCount);
                Interlocked.Increment(ref totalDeliveriesDropped);
            }

            // Once per overflow episode: warnings log regardless of EnableLogging.
            if (Interlocked.Exchange(ref deliveryOverflowReported, 1) == 0)
                NoireLogger.LogWarning(this, $"File watcher delivery queue reached its capacity of {DeliveryQueueCapacity}; the oldest pending deliveries are being dropped.");
        }

        deliveryQueue.Enqueue(delivery);

        if (Volatile.Read(ref deliveryPumpAttached) == 0)
            AttachDeliveryPump();
    }

    // Attached by the first post into an empty queue, detached by the drain that empties it.
    internal bool IsDeliveryPumpAttached => Volatile.Read(ref deliveryPumpAttached) != 0;

    private void AttachDeliveryPump()
    {
        lock (deliveryPumpLock)
        {
            if (deliveryPumpAttached != 0 || Volatile.Read(ref disposed) != 0)
                return;

            Interlocked.Exchange(ref deliveryPumpAttached, 1);

            if (NoireService.IsInitialized())
                NoireService.Framework.Update += OnFrameworkUpdate;
        }
    }

    // A poster increments the count before reading the flag, and this clears the flag before reading the count.
    private void DetachDeliveryPump(bool onlyWhenIdle)
    {
        lock (deliveryPumpLock)
        {
            if (deliveryPumpAttached == 0)
                return;

            Interlocked.Exchange(ref deliveryPumpAttached, 0);

            if (onlyWhenIdle && Volatile.Read(ref queuedDeliveryCount) != 0)
            {
                Interlocked.Exchange(ref deliveryPumpAttached, 1);
                return;
            }

            if (NoireService.IsInitialized())
                NoireService.Framework.Update -= OnFrameworkUpdate;
        }
    }

    private void OnFrameworkUpdate(IFramework framework) => DrainDeliveryQueue();

    // Serializes the watchers' thread-pool events: callbacks never run in parallel.
    internal void DrainDeliveryQueue()
    {
        // Only what was queued at entry: a callback causing more activity cannot starve the frame.
        var toDrain = Volatile.Read(ref queuedDeliveryCount);

        while (toDrain-- > 0 && Volatile.Read(ref disposed) == 0 && deliveryQueue.TryDequeue(out var delivery))
        {
            Interlocked.Decrement(ref queuedDeliveryCount);
            RunDelivery(delivery);
        }

        // Re-arms the overflow report for the next storm.
        if (Volatile.Read(ref queuedDeliveryCount) == 0)
        {
            Volatile.Write(ref deliveryOverflowReported, 0);
            DetachDeliveryPump(onlyWhenIdle: true);
        }
    }

    // A failing handler never stops the drain.
    private void RunDelivery(Action delivery)
    {
        var stack = DeliveriesOnThisThread ??= [];
        stack.Add(this);

        // Incremented before the latch is read, while disposal sets the latch before reading this: a racing delivery is
        // either turned away or awaited.
        Interlocked.Increment(ref deliveriesInFlight);

        try
        {
            if (Volatile.Read(ref disposed) != 0)
                return;

            delivery();
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref totalCallbackExceptionsCaught);
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "File watcher delivery failed.");
        }
        finally
        {
            Interlocked.Decrement(ref deliveriesInFlight);
            stack.RemoveAt(stack.Count - 1);
        }
    }

    // Skips the deliveries this thread is inside: a callback may dispose its own module.
    private void WaitForDeliveriesToDrain()
    {
        var selfInFlight = 0;

        if (DeliveriesOnThisThread != null)
        {
            foreach (var watcher in DeliveriesOnThisThread)
            {
                if (ReferenceEquals(watcher, this))
                    selfInFlight++;
            }
        }

        var spinner = new SpinWait();
        while (Volatile.Read(ref deliveriesInFlight) > selfInFlight)
            spinner.SpinOnce();
    }

    #endregion

    #region Private Helper Methods

    // Caller holds watchLock. Once unindexed, a registration is unreachable and its watcher is disposed outside the lock.
    private void UnindexRegistration(WatchRegistration registration)
    {
        watchRegistrations.Remove(registration.WatchId);
        watcherToWatchId.Remove(registration.Watcher);

        var key = registration.Options.Key;
        if (string.IsNullOrWhiteSpace(key))
            return;

        // Only while the key still names this registration: another watch may have taken the key over.
        if (keyToWatchId.TryGetValue(key, out var indexedWatchId)
            && string.Equals(indexedWatchId, registration.WatchId, StringComparison.Ordinal))
        {
            keyToWatchId.Remove(key);
        }
    }

    private FileSystemWatcher CreateWatcher(string normalizedPath, FileWatchTargetType targetType, FileWatchRegistrationOptions options)
    {
        var watcherPath = targetType == FileWatchTargetType.File
            ? (Path.GetDirectoryName(normalizedPath) ?? normalizedPath)
            : normalizedPath;

        var filter = targetType == FileWatchTargetType.File
            ? Path.GetFileName(normalizedPath)
            : "*";

        var watcher = new FileSystemWatcher(watcherPath, filter)
        {
            IncludeSubdirectories = targetType == FileWatchTargetType.Directory && options.IncludeSubdirectories,
            NotifyFilter = options.NotifyFilter,
            InternalBufferSize = options.InternalBufferSize,
            EnableRaisingEvents = false,
        };

        watcher.Changed += OnWatcherChanged;
        watcher.Created += OnWatcherCreated;
        watcher.Deleted += OnWatcherDeleted;
        watcher.Renamed += OnWatcherRenamed;
        watcher.Error += OnWatcherError;

        return watcher;
    }

    private void OnWatcherChanged(object sender, FileSystemEventArgs args)
        => HandleFileSystemEvent(sender, args, FileWatchEventType.Changed);

    private void OnWatcherCreated(object sender, FileSystemEventArgs args)
        => HandleFileSystemEvent(sender, args, FileWatchEventType.Created);

    private void OnWatcherDeleted(object sender, FileSystemEventArgs args)
        => HandleFileSystemEvent(sender, args, FileWatchEventType.Deleted);

    private void OnWatcherRenamed(object sender, RenamedEventArgs args)
    {
        var watcher = sender as FileSystemWatcher;
        if (watcher == null || !TryGetRegistration(watcher, out var registration))
            return;

        if (!IsEventEnabled(registration.Options, FileWatchEventType.Renamed))
            return;

        if (!ShouldProcessPath(registration, args.FullPath))
            return;

        Interlocked.Increment(ref totalNotificationsObserved);

        var notification = new FileWatchNotification(
            registration.WatchId,
            registration.RootPath,
            registration.TargetType,
            args.FullPath,
            args.Name,
            FileWatchEventType.Renamed,
            DateTimeOffset.UtcNow,
            args.ChangeType,
            args.OldFullPath,
            args.OldName,
            registration.Options.Key);

        DispatchNotification(registration, notification);
    }

    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        var watcher = sender as FileSystemWatcher;
        if (watcher == null || !TryGetRegistration(watcher, out var registration))
            return;

        if (!registration.Options.NotifyOnError)
            return;

        var exception = args.GetException() ?? new IOException("Unknown filesystem watcher error.");
        var error = new FileWatchError(
            registration.WatchId,
            registration.WatchedPath,
            exception,
            DateTimeOffset.UtcNow,
            registration.Options.Key);

        Interlocked.Increment(ref totalErrors);

        if (EnableLogging)
            NoireLogger.LogError(this, exception, $"Watcher error on '{registration.WatchedPath}'.");

        PostDelivery(() => DeliverError(error));
    }

    private void DeliverError(FileWatchError error)
    {
        if (Volatile.Read(ref disposed) != 0)
            return;

        try
        {
            Error?.Invoke(error);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref totalCallbackExceptionsCaught);
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "Error callback threw an exception.");
        }

        PublishEvent(new FileWatchErrorEvent(error));
    }

    private void HandleFileSystemEvent(object sender, FileSystemEventArgs args, FileWatchEventType eventType)
    {
        var watcher = sender as FileSystemWatcher;
        if (watcher == null || !TryGetRegistration(watcher, out var registration))
            return;

        if (!IsEventEnabled(registration.Options, eventType))
            return;

        if (!ShouldProcessPath(registration, args.FullPath))
            return;

        Interlocked.Increment(ref totalNotificationsObserved);

        var notification = new FileWatchNotification(
            registration.WatchId,
            registration.RootPath,
            registration.TargetType,
            args.FullPath,
            args.Name,
            eventType,
            DateTimeOffset.UtcNow,
            args.ChangeType,
            WatchKey: registration.Options.Key);

        DispatchNotification(registration, notification);
    }

    private void DispatchNotification(WatchRegistration registration, FileWatchNotification notification)
    {
        // Before the queue: the burst of Changed events one write produces takes no capacity.
        if (SuppressDuplicateNotifications && IsSuppressedDuplicate(notification))
        {
            Interlocked.Increment(ref totalDuplicateNotificationsSuppressed);
            return;
        }

        // Not coalesced at drain time: counts would follow the frame rate and drop events with suppression off.
        PostDelivery(() => DeliverNotification(registration, notification));
    }

    private void DeliverNotification(WatchRegistration registration, FileWatchNotification notification)
    {
        List<CallbackRegistration> callbacks;

        lock (watchLock)
        {
            // Read at delivery time: a callback or watch retired while queued is not invoked.
            if (Volatile.Read(ref disposed) != 0 || !watchRegistrations.ContainsKey(registration.WatchId))
                return;

            callbacks = registration.Callbacks.ToList();
        }

        foreach (var callback in callbacks)
        {
            try
            {
                if (callback.IsAsync)
                {
                    var asyncCallback = (Func<FileWatchNotification, Task>)callback.Callback;
                    _ = asyncCallback(notification).ContinueWith(t =>
                    {
                        if (!t.IsFaulted)
                            return;

                        Interlocked.Increment(ref totalCallbackExceptionsCaught);
                        if (EnableLogging)
                            NoireLogger.LogError(this, t.Exception!.InnerException ?? t.Exception, "Async file watcher callback failed.");
                    }, TaskScheduler.Default);
                }
                else
                {
                    var syncCallback = (Action<FileWatchNotification>)callback.Callback;
                    syncCallback(notification);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref totalCallbackExceptionsCaught);
                if (EnableLogging)
                    NoireLogger.LogError(this, ex, "File watcher callback failed.");
            }
        }

        TriggerModuleEvents(notification);

        Interlocked.Increment(ref totalNotificationsDispatched);
        PublishEvent(new FileWatchNotificationEvent(notification));
    }

    private void TriggerModuleEvents(FileWatchNotification notification)
    {
        try
        {
            NotificationReceived?.Invoke(notification);

            switch (notification.EventType)
            {
                case FileWatchEventType.Changed:
                    Changed?.Invoke(notification);
                    break;
                case FileWatchEventType.Created:
                    Created?.Invoke(notification);
                    break;
                case FileWatchEventType.Deleted:
                    Deleted?.Invoke(notification);
                    break;
                case FileWatchEventType.Renamed:
                    Renamed?.Invoke(notification);
                    break;
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref totalCallbackExceptionsCaught);
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "File watcher CLR event callback failed.");
        }
    }

    // Collapses the burst of events one file write produces.
    internal bool IsSuppressedDuplicate(FileWatchNotification notification)
    {
        var now = notification.OccurredAtUtc;
        var key = $"{notification.WatchId}|{notification.EventType}|{notification.FullPath}|{notification.OldFullPath}";

        lock (watchLock)
        {
            if (recentNotificationCache.TryGetValue(key, out var previousTimestamp)
                && (now - previousTimestamp) <= DuplicateNotificationWindow)
            {
                return true;
            }

            recentNotificationCache[key] = now;

            var shouldSweep = Interlocked.Increment(ref duplicateCacheSweepCounter) % 128 == 0;
            if (shouldSweep)
            {
                var threshold = now - DuplicateNotificationWindow - DuplicateNotificationWindow;
                var staleKeys = recentNotificationCache
                    .Where(pair => pair.Value < threshold)
                    .Select(pair => pair.Key)
                    .ToList();

                foreach (var staleKey in staleKeys)
                    recentNotificationCache.Remove(staleKey);
            }
        }

        return false;
    }

    private bool TryGetRegistration(FileSystemWatcher watcher, out WatchRegistration registration)
    {
        lock (watchLock)
        {
            if (watcherToWatchId.TryGetValue(watcher, out var watchId)
                && watchRegistrations.TryGetValue(watchId, out registration!))
            {
                return true;
            }
        }

        registration = null!;
        return false;
    }

    // Errors arrive through their own event, checked against NotifyOnError.
    private static bool IsEventEnabled(FileWatchRegistrationOptions options, FileWatchEventType eventType)
    {
        return eventType switch
        {
            FileWatchEventType.Changed => options.NotifyOnChanged,
            FileWatchEventType.Created => options.NotifyOnCreated,
            FileWatchEventType.Deleted => options.NotifyOnDeleted,
            FileWatchEventType.Renamed => options.NotifyOnRenamed,
            _ => true,
        };
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path.Trim());

    private static FileWatchTargetType ResolveTargetType(string path, FileWatchTargetType requestedTargetType)
    {
        if (requestedTargetType != FileWatchTargetType.Auto)
            return requestedTargetType;

        if (Directory.Exists(path))
            return FileWatchTargetType.Directory;

        if (File.Exists(path))
            return FileWatchTargetType.File;

        var hasExtension = !string.IsNullOrWhiteSpace(Path.GetExtension(path));
        return hasExtension ? FileWatchTargetType.File : FileWatchTargetType.Directory;
    }

    private static void ValidatePathExists(string path, FileWatchTargetType targetType, bool allowNonExistingPath)
    {
        if (allowNonExistingPath)
            return;

        if (targetType == FileWatchTargetType.Directory && !Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory '{path}' does not exist.");

        if (targetType == FileWatchTargetType.File)
        {
            var directoryPath = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                throw new DirectoryNotFoundException($"Directory '{directoryPath}' does not exist.");
        }
    }

    private static bool ShouldProcessPath(WatchRegistration registration, string fullPath)
    {
        if (registration.TargetType == FileWatchTargetType.File)
            return string.Equals(registration.WatchedPath, fullPath, StringComparison.OrdinalIgnoreCase);

        var patterns = registration.Options.Patterns;
        if (patterns == null || patterns.Count == 0)
            return true;

        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            if (IsPatternMatch(pattern, fileName))
                return true;
        }

        return false;
    }

    private static bool IsPatternMatch(string pattern, string input)
    {
        var escaped = Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");

        return Regex.IsMatch(input, $"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static FileWatchRegistrationInfo ToInfo(WatchRegistration registration)
    {
        return new FileWatchRegistrationInfo(
            WatchId: registration.WatchId,
            Path: registration.WatchedPath,
            TargetType: registration.TargetType,
            Patterns: registration.Options.Patterns.ToList(),
            IncludeSubdirectories: registration.Options.IncludeSubdirectories,
            NotifyFilter: registration.Options.NotifyFilter,
            IsEnabled: registration.IsEnabled,
            Key: registration.Options.Key,
            CallbackCount: registration.Callbacks.Count(c => !c.IsAsync),
            AsyncCallbackCount: registration.Callbacks.Count(c => c.IsAsync));
    }

    // Always reached through PostDelivery: Publish runs synchronous handlers inline, on the caller's thread.
    private void PublishEvent<TEvent>(TEvent eventData)
    {
        EventBus?.Publish(eventData);
    }

    #endregion

    /// <summary>Stops every delivery, blocking until the ones already running finish.</summary>
    protected override void DisposeInternal()
    {
        // Delivery closes first: a disposed watcher's already raised events may still be in flight.
        Interlocked.Exchange(ref disposed, 1);
        DetachDeliveryPump(onlyWhenIdle: false);

        deliveryQueue.Clear();
        Volatile.Write(ref queuedDeliveryCount, 0);

        // Makes disposal a hard boundary for deliveries already past the latch.
        WaitForDeliveriesToDrain();

        // Publishes nothing: the latch is set and the plugin may be unloading.
        ClearAllWatches();

        lock (watchLock)
            recentNotificationCache.Clear();

        NotificationReceived = null;
        Changed = null;
        Created = null;
        Deleted = null;
        Renamed = null;
        Error = null;

        if (EnableLogging)
        {
            var stats = GetStatistics();
            NoireLogger.LogInfo(this, $"FileWatcher disposed. Watches: {stats.RegisteredWatches}, Notifications: {stats.TotalNotificationsDispatched}, Dropped: {stats.TotalDeliveriesDropped}, Errors: {stats.TotalErrors}");
        }
    }

    #region Private Classes

    private sealed class CallbackRegistration
    {
        public FileWatchCallbackToken Token { get; }
        public Delegate Callback { get; }
        public object? Owner { get; }
        public bool IsAsync { get; }

        public CallbackRegistration(FileWatchCallbackToken token, Delegate callback, object? owner, bool isAsync)
        {
            Token = token;
            Callback = callback;
            Owner = owner;
            IsAsync = isAsync;
        }
    }

    private sealed class WatchRegistration
    {
        public string WatchId { get; }
        public string WatchedPath { get; }
        public string RootPath { get; }
        public FileWatchTargetType TargetType { get; }
        public FileWatchRegistrationOptions Options { get; }
        public FileSystemWatcher Watcher { get; }
        public List<CallbackRegistration> Callbacks { get; } = [];
        public bool IsEnabled { get; set; }

        public WatchRegistration(
            string watchId,
            string watchedPath,
            string rootPath,
            FileWatchTargetType targetType,
            FileWatchRegistrationOptions options,
            FileSystemWatcher watcher,
            bool isEnabled)
        {
            WatchId = watchId;
            WatchedPath = watchedPath;
            RootPath = rootPath;
            TargetType = targetType;
            Options = options;
            Watcher = watcher;
            IsEnabled = isEnabled;
        }
    }

    #endregion
}
