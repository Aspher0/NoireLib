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
/// A module providing advanced filesystem watching for directories and files.<br/>
/// Supports multiple concurrent watch registrations, callback subscriptions, duplicate suppression,
/// and EventBus integration.<br/>
/// Every callback, CLR event and EventBus publication this module makes is invoked on the framework thread, so
/// handlers may touch game state directly. This covers the registration lifecycle events as much as the
/// notification ones, so an EventBus subscriber's thread never depends on which thread some other consumer
/// happened to call <see cref="Watch"/> or <see cref="RemoveWatch"/> from.<br/>
/// Handlers never overlap: the underlying watchers raise events concurrently on thread pool threads, but
/// deliveries are queued and drained one at a time. Queueing is also what makes the lifecycle events
/// asynchronous: they reach subscribers after the call that caused them has already returned.<br/>
/// When NoireLib is not initialized, deliveries run inline on the calling or observing thread instead, which is
/// what makes the module usable without a running game. Once the module is disposed, nothing is delivered again.
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
    private int deliveryPumpAttached;
    private int deliveryOverflowReported;
    private int deliveriesInFlight;
    private int disposed;

    /// <summary>
    /// The watcher instances whose deliveries the current thread is currently inside, innermost last.<br/>
    /// A consumer callback may dispose the very module that invoked it, and that delivery is still in flight while
    /// <see cref="DisposeInternal"/> runs, so a disposing thread has to recognize its own deliveries rather than
    /// wait for them to finish. Deliveries nest (a callback can post further work that runs inline), and several
    /// watcher instances can be delivering on one thread, hence a stack rather than a flag.
    /// </summary>
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

    /// <summary>
    /// The associated EventBus instance for publishing watcher events.<br/>
    /// If <see langword="null"/>, events are only exposed through CLR events and callbacks.
    /// </summary>
    public NoireEventBus? EventBus { get; set; } = null;

    /// <summary>
    /// The default constructor needed for internal purposes.
    /// </summary>
    public NoireFileWatcher() : base() { }

    /// <summary>
    /// Creates a new instance of the <see cref="NoireFileWatcher"/> module.
    /// </summary>
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

    /// <summary>
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>.<br/>
    /// Only used for internal module management.
    /// </summary>
    internal NoireFileWatcher(ModuleId? moduleId, bool active = true, bool enableLogging = true)
        : base(moduleId, active, enableLogging) { }

    #endregion

    #region Module Lifecycle Methods

    /// <summary>
    /// Initializes the module with optional initialization parameters.
    /// </summary>
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
    /// <summary>
    /// Whether newly added watches should be automatically started while the module is active.
    /// </summary>
    public bool AutoEnableNewWatches
    {
        get => autoEnableNewWatches;
        set => autoEnableNewWatches = value;
    }

    /// <summary>
    /// Sets whether newly created watches should auto-start while active.
    /// </summary>
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
    /// <summary>
    /// Time window used for duplicate notification suppression.
    /// </summary>
    public TimeSpan DuplicateNotificationWindow
    {
        get => duplicateNotificationWindow;
        set => duplicateNotificationWindow = value <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : value;
    }

    /// <summary>
    /// Sets duplicate suppression behavior.
    /// </summary>
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
    /// Event raised for every notification dispatched by the module.<br/>
    /// Handlers are invoked on the framework thread and never overlap each other, so they may touch game state
    /// directly. Falls back to the observing thread when NoireLib is not initialized.
    /// </summary>
    public event Action<FileWatchNotification>? NotificationReceived;

    /// <summary>
    /// Event raised for changed notifications.<br/>
    /// Handlers are invoked on the framework thread and never overlap each other, so they may touch game state
    /// directly. Falls back to the observing thread when NoireLib is not initialized.
    /// </summary>
    public event Action<FileWatchNotification>? Changed;

    /// <summary>
    /// Event raised for created notifications.<br/>
    /// Handlers are invoked on the framework thread and never overlap each other, so they may touch game state
    /// directly. Falls back to the observing thread when NoireLib is not initialized.
    /// </summary>
    public event Action<FileWatchNotification>? Created;

    /// <summary>
    /// Event raised for deleted notifications.<br/>
    /// Handlers are invoked on the framework thread and never overlap each other, so they may touch game state
    /// directly. Falls back to the observing thread when NoireLib is not initialized.
    /// </summary>
    public event Action<FileWatchNotification>? Deleted;

    /// <summary>
    /// Event raised for renamed notifications.<br/>
    /// Handlers are invoked on the framework thread and never overlap each other, so they may touch game state
    /// directly. Falls back to the observing thread when NoireLib is not initialized.
    /// </summary>
    public event Action<FileWatchNotification>? Renamed;

    /// <summary>
    /// Event raised for watcher-level errors.<br/>
    /// Handlers are invoked on the framework thread and never overlap each other, so they may touch game state
    /// directly. Falls back to the observing thread when NoireLib is not initialized.
    /// </summary>
    public event Action<FileWatchError>? Error;

    #endregion

    #region Public API Methods

    /// <summary>
    /// Registers a new directory watch.<br/>
    /// <paramref name="callback"/> is invoked on the framework thread, and <paramref name="asyncCallback"/> is started
    /// on it, so both may touch game state. See <see cref="Watch"/> for the full delivery contract.
    /// </summary>
    /// <param name="directoryPath">The directory to watch.</param>
    /// <param name="callback">Optional synchronous callback invoked for every matching notification.</param>
    /// <param name="asyncCallback">Optional asynchronous callback started for every matching notification.</param>
    /// <param name="owner">Optional owner associated with the callbacks, for bulk removal.</param>
    /// <param name="key">Optional user key. An existing watch with the same key is replaced.</param>
    /// <param name="patterns">Optional glob patterns filtering the watched files. Defaults to all files.</param>
    /// <param name="includeSubdirectories">Whether subdirectories are watched recursively.</param>
    /// <returns>The ID of the new watch registration.</returns>
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

    /// <summary>
    /// Registers a new single-file watch.<br/>
    /// <paramref name="callback"/> is invoked on the framework thread, and <paramref name="asyncCallback"/> is started
    /// on it, so both may touch game state. See <see cref="Watch"/> for the full delivery contract.
    /// </summary>
    /// <param name="filePath">The file to watch.</param>
    /// <param name="callback">Optional synchronous callback invoked for every notification on the file.</param>
    /// <param name="asyncCallback">Optional asynchronous callback started for every notification on the file.</param>
    /// <param name="owner">Optional owner associated with the callbacks, for bulk removal.</param>
    /// <param name="key">Optional user key. An existing watch with the same key is replaced.</param>
    /// <returns>The ID of the new watch registration.</returns>
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
    /// Registers a new filesystem watch using advanced options.<br/>
    /// <paramref name="callback"/> is invoked on the framework thread, so it may touch game state directly.
    /// <paramref name="asyncCallback"/> is started on the framework thread and runs fire-and-forget, so the work before
    /// its first await is equally safe; what runs after an await is governed by that callback's own awaits.<br/>
    /// Callbacks never overlap: the underlying watchers raise events concurrently on thread pool threads, but
    /// deliveries are queued and drained one at a time. Delivery falls back to the observing thread when NoireLib is
    /// not initialized. A callback retired before its notification is delivered is not invoked.<br/>
    /// The <see cref="FileWatchRegisteredEvent"/> this publishes is queued like everything else, so it reaches
    /// EventBus subscribers on the framework thread after this method has returned. The watch only starts raising
    /// events once that registration event is queued, so a subscriber is never told about a notification for a watch
    /// it has not been told about yet.<br/>
    /// Registering on a disposed module is silently abandoned: an ID is still returned, but no watch exists under it
    /// and <see cref="GetWatch"/> reports none.
    /// </summary>
    /// <param name="options">The registration options describing the watch.</param>
    /// <param name="callback">Optional synchronous callback invoked for every matching notification.</param>
    /// <param name="asyncCallback">Optional asynchronous callback started for every matching notification.</param>
    /// <param name="owner">Optional owner associated with the callbacks, for bulk removal.</param>
    /// <returns>The ID of the new watch registration.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the options carry an empty path.</exception>
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
            // A key identifies at most one watch, so registering a key displaces whichever watch already holds it. The
            // displaced watch is resolved and retired inside the same lock acquisition that inserts the new one, so two
            // concurrent Watch calls for the same key cannot both complete and leave one of them reachable by id but
            // not by key. Retiring it before the insert lets the new key mapping take its place cleanly.
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

            // Snapshotted under the lock because it reads the callback list, which another thread may be adding to.
            registeredInfo = ToInfo(registration);
        }

        Interlocked.Increment(ref totalRegistrations);

        // The displaced watch's watcher is disposed and its removal announced outside the lock for the same reason
        // RemoveWatch does it there: FileSystemWatcher.Dispose can block, and watchLock is taken on the framework
        // thread by the delivery path. Unindexing it above already made it unreachable, so this thread is the last to
        // touch it. Its removal is queued before the registration event below, so a subscriber tracking watches sees
        // the old one leave before the new one arrives.
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

        // Raising is turned on only once the registration event is queued, so a subscriber that tracks watches by ID
        // is always told that a watch exists before it can receive that watch's first notification. The state is
        // re-read rather than reused because a subscriber handling the registration event may already have disabled
        // or removed this watch by the time delivery returns.
        var abandoned = false;

        lock (watchLock)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                // Disposal removes every watch it can see and then stops looking, so a registration that lands after
                // that sweep would sit in the module forever holding a live FileSystemWatcher that nothing disposes.
                // Taking it back out here is what keeps a registration racing disposal from outliving the module.
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

    /// <summary>
    /// Adds a callback to an existing watch registration.<br/>
    /// The callback is invoked on the framework thread and never overlaps another callback, so it may touch game state
    /// directly. Falls back to the observing thread when NoireLib is not initialized.
    /// </summary>
    /// <param name="watchId">The ID of the watch registration to attach to.</param>
    /// <param name="callback">The callback invoked for every matching notification.</param>
    /// <param name="owner">Optional owner associated with the callback, for bulk removal.</param>
    /// <returns>A token identifying the callback, for use with <see cref="RemoveCallback"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no watch with the given ID exists.</exception>
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

    /// <summary>
    /// Adds an async callback to an existing watch registration.<br/>
    /// The callback is started on the framework thread and runs fire-and-forget, so the work before its first await may
    /// touch game state directly; what runs after an await is governed by the callback's own awaits. A faulted task is
    /// caught, counted and logged. Falls back to the observing thread when NoireLib is not initialized.
    /// </summary>
    /// <param name="watchId">The ID of the watch registration to attach to.</param>
    /// <param name="callback">The callback started for every matching notification.</param>
    /// <param name="owner">Optional owner associated with the callback, for bulk removal.</param>
    /// <returns>A token identifying the callback, for use with <see cref="RemoveCallback"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no watch with the given ID exists.</exception>
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

    /// <summary>
    /// Removes a callback from all watches by token.
    /// </summary>
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

    /// <summary>
    /// Removes all callbacks owned by a specific owner.
    /// </summary>
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

    /// <summary>
    /// Enables or disables a watch.<br/>
    /// The <see cref="FileWatchStateChangedEvent"/> this publishes reaches EventBus subscribers on the framework
    /// thread, after this method has returned.
    /// </summary>
    /// <param name="watchId">The ID of the watch registration to toggle.</param>
    /// <param name="enabled">Whether the watch should raise events while the module is active.</param>
    /// <returns>True when a watch with the given ID existed, false otherwise.</returns>
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

    /// <summary>
    /// Enables all registered watches.<br/>
    /// Publishes one <see cref="FileWatchStateChangedEvent"/> for each watch that was not already enabled, exactly as
    /// <see cref="SetWatchEnabled"/> does. Watches already enabled are left alone and report nothing.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireFileWatcher EnableAllWatches() => SetAllWatchesEnabled(true);

    /// <summary>
    /// Disables all registered watches.<br/>
    /// Publishes one <see cref="FileWatchStateChangedEvent"/> for each watch that was not already disabled, exactly as
    /// <see cref="SetWatchEnabled"/> does. Watches already disabled are left alone and report nothing.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireFileWatcher DisableAllWatches() => SetAllWatchesEnabled(false);

    /// <summary>
    /// Applies one enabled state to every registered watch, reporting the watches whose state actually changed.<br/>
    /// A bulk change reports itself as the same per-watch <see cref="FileWatchStateChangedEvent"/> that
    /// <see cref="SetWatchEnabled"/> publishes, rather than through an event of its own, so that a subscriber tracking
    /// watch state stays correct without having to know which API the caller reached for. Only watches that actually
    /// changed are reported, so a bulk call over watches already in the requested state is silent.
    /// </summary>
    /// <param name="enabled">The state to apply to every registered watch.</param>
    /// <returns>The module instance for chaining.</returns>
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

        // Posted after the lock is released, because an inline delivery runs an EventBus subscriber on this very
        // thread, and a subscriber calling back into the module must not find watchLock already held.
        foreach (var watchId in changedWatchIds)
        {
            var stateChangedEvent = new FileWatchStateChangedEvent(watchId, enabled);
            PostDelivery(() => PublishEvent(stateChangedEvent));
        }

        return this;
    }

    /// <summary>
    /// Removes a watch registration.<br/>
    /// The watch stops raising events immediately, and notifications it captured but that have not been delivered yet
    /// are discarded. The <see cref="FileWatchRemovedEvent"/> this publishes reaches EventBus subscribers on the
    /// framework thread, after this method has returned.
    /// </summary>
    /// <param name="watchId">The ID of the watch registration to remove.</param>
    /// <returns>True when a watch with the given ID existed, false otherwise.</returns>
    public bool RemoveWatch(string watchId)
    {
        WatchRegistration? registration = null;

        lock (watchLock)
        {
            if (!watchRegistrations.TryGetValue(watchId, out registration))
                return false;

            UnindexRegistration(registration);
        }

        // Disposed outside the lock because FileSystemWatcher.Dispose can block briefly while it tears down its
        // native handle, and watchLock is taken on the framework thread by the delivery path, so holding it across
        // that call would stall a frame. Unindexing above is what makes this safe: no other site can still reach a
        // registration that has left watchRegistrations, so this thread is the last one to touch the watcher.
        registration.Watcher.EnableRaisingEvents = false;
        registration.Watcher.Dispose();

        Interlocked.Increment(ref totalRemoved);

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Removed watch '{watchId}' on path '{registration.WatchedPath}'.");

        var removedEvent = new FileWatchRemovedEvent(watchId, registration.WatchedPath, registration.Options.Key);
        PostDelivery(() => PublishEvent(removedEvent));
        return true;
    }

    /// <summary>
    /// Removes a watch registration by key.<br/>
    /// Behaves exactly like <see cref="RemoveWatch"/> once the key is resolved.
    /// </summary>
    /// <param name="key">The user key of the watch registration to remove.</param>
    /// <returns>True when a watch with the given key existed, false otherwise.</returns>
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

    /// <summary>
    /// Removes all watch registrations.<br/>
    /// Publishes one <see cref="FileWatchRemovedEvent"/> per watch followed by a single
    /// <see cref="FileWatchesClearedEvent"/>. All of them reach EventBus subscribers on the framework thread, in that
    /// order, after this method has returned.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
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

    /// <summary>
    /// Gets an immutable snapshot of all registrations.
    /// </summary>
    public IReadOnlyList<FileWatchRegistrationInfo> GetWatches()
    {
        lock (watchLock)
            return watchRegistrations.Values.Select(ToInfo).ToList();
    }

    /// <summary>
    /// Gets one registration by watch ID.
    /// </summary>
    public FileWatchRegistrationInfo? GetWatch(string watchId)
    {
        lock (watchLock)
        {
            if (!watchRegistrations.TryGetValue(watchId, out var registration))
                return null;

            return ToInfo(registration);
        }
    }

    /// <summary>
    /// Gets one registration by key.
    /// </summary>
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

    /// <summary>
    /// Gets aggregated statistics about this module instance.
    /// </summary>
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

    /// <summary>
    /// Maximum number of deliveries allowed to wait for the framework thread at once.<br/>
    /// A filesystem event storm can produce notifications far faster than the game renders frames, so the queue
    /// is bounded: beyond this many pending deliveries the oldest is dropped rather than letting memory grow.
    /// </summary>
    internal const int DeliveryQueueCapacity = 4096;

    /// <summary>
    /// Forces deliveries through the framework thread queue even when NoireLib is not initialized, leaving
    /// <see cref="DrainDeliveryQueue"/> as the only way to run them.<br/>
    /// This is the seam for exercising queueing, ordering and the drop policy without a running game.
    /// </summary>
    internal bool ForceQueuedDelivery { get; set; } = false;

    /// <summary>
    /// Whether deliveries run inline on the thread that observed the filesystem event.<br/>
    /// Without an initialized NoireLib there is no framework thread to marshal onto, so inline is the only option.
    /// </summary>
    private bool InlineDelivery => !NoireService.IsInitialized() && !ForceQueuedDelivery;

    /// <summary>
    /// Queues a delivery to run on the framework thread, or runs it inline when there is no framework thread
    /// to marshal onto. Deliveries posted after disposal are discarded.
    /// </summary>
    /// <param name="delivery">The delivery to run.</param>
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
            // Drop the oldest delivery to make room. For filesystem events the newest notification describes the
            // current state of a path, which is what a consumer re-reading that path needs.
            if (deliveryQueue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref queuedDeliveryCount);
                Interlocked.Increment(ref totalDeliveriesDropped);
            }

            // Report once per overflow episode rather than once per drop. A bulk filesystem operation overflows on
            // every event for as long as it runs, and warnings log regardless of EnableLogging, so a per-drop warning
            // would turn a storm into a second storm of log writes.
            if (Interlocked.Exchange(ref deliveryOverflowReported, 1) == 0)
                NoireLogger.LogWarning(this, $"File watcher delivery queue reached its capacity of {DeliveryQueueCapacity}; the oldest pending deliveries are being dropped.");
        }

        deliveryQueue.Enqueue(delivery);

        if (NoireService.IsInitialized() && Interlocked.CompareExchange(ref deliveryPumpAttached, 1, 0) == 0)
            NoireService.Framework.Update += OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework) => DrainDeliveryQueue();

    /// <summary>
    /// Runs the deliveries queued as of entry, on the calling thread.<br/>
    /// Draining is what serializes deliveries: the underlying watchers raise events concurrently on several
    /// thread pool threads, so without this step consumer callbacks could run in parallel with each other.
    /// </summary>
    internal void DrainDeliveryQueue()
    {
        // Only drain what was queued at entry, so a callback that triggers further filesystem activity cannot
        // starve the frame it is running on.
        var toDrain = Volatile.Read(ref queuedDeliveryCount);

        while (toDrain-- > 0 && Volatile.Read(ref disposed) == 0 && deliveryQueue.TryDequeue(out var delivery))
        {
            Interlocked.Decrement(ref queuedDeliveryCount);
            RunDelivery(delivery);
        }

        // Arm the overflow report again once the backlog is fully gone, so a later storm is reported afresh.
        if (Volatile.Read(ref queuedDeliveryCount) == 0)
            Volatile.Write(ref deliveryOverflowReported, 0);
    }

    /// <summary>
    /// Runs one delivery, containing any exception so that a single failing handler cannot stop the drain.<br/>
    /// A delivery that starts after disposal is discarded, and one that has already started keeps
    /// <see cref="DisposeInternal"/> waiting until it finishes.
    /// </summary>
    /// <param name="delivery">The delivery to run.</param>
    private void RunDelivery(Action delivery)
    {
        var stack = DeliveriesOnThisThread ??= [];
        stack.Add(this);

        // The in-flight registration below is published before the disposal latch is read, and DisposeInternal sets
        // that latch before it reads the in-flight count. Both operations are full fences, so at least one of the two
        // threads observes the other: a delivery racing disposal either turns itself away here or holds Dispose up
        // until it has finished. Without that pairing a delivery could pass the latch check and then invoke a
        // consumer callback after Dispose had already returned and torn the module down around it.
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

    /// <summary>
    /// Blocks until every delivery that had already passed the disposal latch has finished.<br/>
    /// Deliveries the calling thread is itself inside are excluded: a consumer callback is allowed to dispose the
    /// module that invoked it, and waiting for that delivery would be waiting for the caller's own stack frame.<br/>
    /// No lock is held while waiting, so a callback that is still running remains free to call back into the module.
    /// </summary>
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

    /// <summary>
    /// Removes one registration from every index that holds it. The caller must hold <see cref="watchLock"/>.<br/>
    /// Dropping a registration from <see cref="watchRegistrations"/> is what retires it, and that is the invariant the
    /// whole watcher lifecycle rests on: every site that touches a registration's <see cref="FileSystemWatcher"/>
    /// looks the registration up here and touches it within the same lock acquisition, never across two. A
    /// registration that is no longer indexed is therefore unreachable, which is what lets a caller dispose its
    /// watcher outside the lock without another thread finding a disposed instance. Splitting any of those lookups
    /// from its touch would reintroduce that window.<br/>
    /// <see cref="keyToWatchId"/> is the one index this retires conditionally, because a key can resolve to a watch
    /// other than this one. See the remark on that removal.
    /// </summary>
    /// <param name="registration">The registration to remove from the indexes.</param>
    private void UnindexRegistration(WatchRegistration registration)
    {
        watchRegistrations.Remove(registration.WatchId);
        watcherToWatchId.Remove(registration.Watcher);

        var key = registration.Options.Key;
        if (string.IsNullOrWhiteSpace(key))
            return;

        // Retired only while the key still resolves to this registration. Registering a key resolves and retires the
        // watch already holding it inside the same lock acquisition that inserts the new one, so the index always
        // resolves to exactly one live watch. This check keeps that guarantee even if a registration is retired out of
        // order: removing the mapping unconditionally could retire a key that has since been taken over by a different,
        // still-registered watch, which would then keep raising events while the key reported that it does not exist.
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

    /// <summary>
    /// Invokes the error event and publishes the matching EventBus event.<br/>
    /// Runs on the framework thread unless NoireLib is not initialized.
    /// </summary>
    /// <param name="error">The error to deliver.</param>
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
        // Duplicate suppression stays on the observing thread, ahead of the queue. A file write commonly arrives as a
        // burst of Changed events for one path, and collapsing the burst here keeps it from consuming queue capacity.
        if (SuppressDuplicateNotifications && IsSuppressedDuplicate(notification))
        {
            Interlocked.Increment(ref totalDuplicateNotificationsSuppressed);
            return;
        }

        // Notifications that survive suppression are delivered individually. Coalescing again at drain time would tie
        // how many notifications a consumer sees to the frame rate, and would silently discard events for consumers
        // that turned suppression off precisely because they want every one.
        PostDelivery(() => DeliverNotification(registration, notification));
    }

    /// <summary>
    /// Invokes the callbacks and events of one notification.<br/>
    /// Runs on the framework thread unless NoireLib is not initialized.
    /// </summary>
    /// <param name="registration">The watch registration that captured the notification.</param>
    /// <param name="notification">The notification to deliver.</param>
    private void DeliverNotification(WatchRegistration registration, FileWatchNotification notification)
    {
        List<CallbackRegistration> callbacks;

        lock (watchLock)
        {
            // The callback set is read at delivery time, not at observation time, so a callback or watch the consumer
            // retired while this notification was queued does not get invoked afterwards.
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

    /// <summary>
    /// Whether a notification repeats one already seen for the same watch, event type and paths within
    /// <see cref="DuplicateNotificationWindow"/>. Collapses the burst of events a single file write produces.
    /// </summary>
    /// <param name="notification">The notification to test.</param>
    /// <returns>True when the notification should be discarded as a duplicate.</returns>
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

    /// <summary>
    /// Whether a watch is configured to observe a given kind of filesystem notification.<br/>
    /// Only the four notification kinds are mapped. A watcher-level error is not a notification and never reaches
    /// this method: it arrives through a separate event carrying an exception rather than a path, and
    /// <see cref="FileWatchRegistrationOptions.NotifyOnError"/> is checked on that path instead.
    /// </summary>
    /// <param name="options">The options of the watch that captured the event.</param>
    /// <param name="eventType">The kind of notification to test.</param>
    /// <returns>True when the notification should be processed.</returns>
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

    /// <summary>
    /// Publishes one event to the associated EventBus, if there is one.<br/>
    /// Every call site reaches this through <see cref="PostDelivery"/> and never directly, because
    /// <see cref="NoireEventBus.Publish{TEvent}(TEvent)"/> invokes synchronous handlers inline: publishing straight from a public
    /// method would run an EventBus subscriber on whichever thread that method's caller happened to use, and a
    /// subscriber cannot see, let alone control, which thread an unrelated consumer registers watches from.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="eventData">The event to publish.</param>
    private void PublishEvent<TEvent>(TEvent eventData)
    {
        EventBus?.Publish(eventData);
    }

    #endregion

    /// <summary>
    /// Internal dispose method called when the module is disposed.<br/>
    /// Once this returns, no callback, CLR event handler or EventBus subscriber of this module can be invoked again.
    /// It blocks for as long as a delivery that had already started takes to finish, so a handler that blocks
    /// indefinitely blocks disposal with it.
    /// </summary>
    protected override void DisposeInternal()
    {
        // Close the delivery path before tearing the watchers down. Disposing a watcher does not retract the events it
        // already raised, so notifications can still be in flight on thread pool threads at this point; blocking new
        // deliveries and detaching the drain first is what stops them from reaching a consumer callback afterwards.
        Interlocked.Exchange(ref disposed, 1);

        if (Interlocked.Exchange(ref deliveryPumpAttached, 0) == 1)
            NoireService.Framework.Update -= OnFrameworkUpdate;

        deliveryQueue.Clear();
        Volatile.Write(ref queuedDeliveryCount, 0);

        // The latch turns away deliveries that have not started, but one that had already passed it is on its way to a
        // consumer callback right now. Waiting for those is what makes disposal a hard boundary rather than a very
        // likely one, and it matters most without an initialized NoireLib, where deliveries run on whatever thread
        // observed the filesystem event instead of on the single framework thread that also runs this teardown.
        WaitForDeliveriesToDrain();

        // Nothing below is published: the latch is set, so the watch removals this performs deliver no lifecycle
        // events. A module being torn down has no business calling into subscribers of a plugin that is unloading.
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
