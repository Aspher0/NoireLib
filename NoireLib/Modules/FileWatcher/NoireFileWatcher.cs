using NoireLib.Core.Modules;
using NoireLib.EventBus;
using System;
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
/// and EventBus integration.
/// </summary>
public class NoireFileWatcher : NoireModuleBase<NoireFileWatcher>
{
    #region Private Properties and Fields

    private readonly Dictionary<string, WatchRegistration> watchRegistrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FileSystemWatcher, string> watcherToWatchId = new();
    private readonly Dictionary<string, string> keyToWatchId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> recentNotificationCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object watchLock = new();

    private long totalRegistrations;
    private long totalRemoved;
    private long totalNotificationsObserved;
    private long totalNotificationsDispatched;
    private long totalErrors;
    private long totalDuplicateNotificationsSuppressed;
    private long totalCallbackExceptionsCaught;

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
    /// Event raised for every notification dispatched by the module.
    /// </summary>
    public event Action<FileWatchNotification>? NotificationReceived;

    /// <summary>
    /// Event raised for changed notifications.
    /// </summary>
    public event Action<FileWatchNotification>? Changed;

    /// <summary>
    /// Event raised for created notifications.
    /// </summary>
    public event Action<FileWatchNotification>? Created;

    /// <summary>
    /// Event raised for deleted notifications.
    /// </summary>
    public event Action<FileWatchNotification>? Deleted;

    /// <summary>
    /// Event raised for renamed notifications.
    /// </summary>
    public event Action<FileWatchNotification>? Renamed;

    /// <summary>
    /// Event raised for watcher-level errors.
    /// </summary>
    public event Action<FileWatchError>? Error;

    #endregion

    #region Public API Methods

    /// <summary>
    /// Registers a new directory watch.
    /// </summary>
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
    /// Registers a new single-file watch.
    /// </summary>
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
    /// Registers a new filesystem watch using advanced options.
    /// </summary>
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

        if (!string.IsNullOrWhiteSpace(options.Key))
            RemoveWatchByKey(options.Key!);

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

        lock (watchLock)
        {
            watchRegistrations[watchId] = registration;
            watcherToWatchId[watcher] = watchId;

            if (!string.IsNullOrWhiteSpace(options.Key))
                keyToWatchId[options.Key!] = watchId;

            if (IsActive && AutoEnableNewWatches && registration.IsEnabled)
                watcher.EnableRaisingEvents = true;
        }

        Interlocked.Increment(ref totalRegistrations);

        if (EnableLogging)
            NoireLogger.LogInfo(this, $"Registered watch '{watchId}' on path '{normalizedPath}'.");

        PublishEvent(new FileWatchRegisteredEvent(ToInfo(registration)));
        return watchId;
    }

    /// <summary>
    /// Adds a callback to an existing watch registration.
    /// </summary>
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
    /// Adds an async callback to an existing watch registration.
    /// </summary>
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

        var totalRemoved = 0;

        lock (watchLock)
        {
            foreach (var registration in watchRegistrations.Values)
                totalRemoved += registration.Callbacks.RemoveAll(c => ReferenceEquals(c.Owner, owner));
        }

        return totalRemoved;
    }

    /// <summary>
    /// Enables or disables a watch.
    /// </summary>
    public bool SetWatchEnabled(string watchId, bool enabled)
    {
        lock (watchLock)
        {
            if (!watchRegistrations.TryGetValue(watchId, out var registration))
                return false;

            registration.IsEnabled = enabled;
            registration.Watcher.EnableRaisingEvents = IsActive && enabled;
        }

        PublishEvent(new FileWatchStateChangedEvent(watchId, enabled));
        return true;
    }

    /// <summary>
    /// Enables all registered watches.
    /// </summary>
    public NoireFileWatcher EnableAllWatches()
    {
        lock (watchLock)
        {
            foreach (var registration in watchRegistrations.Values)
            {
                registration.IsEnabled = true;
                registration.Watcher.EnableRaisingEvents = IsActive;
            }
        }

        return this;
    }

    /// <summary>
    /// Disables all registered watches.
    /// </summary>
    public NoireFileWatcher DisableAllWatches()
    {
        lock (watchLock)
        {
            foreach (var registration in watchRegistrations.Values)
            {
                registration.IsEnabled = false;
                registration.Watcher.EnableRaisingEvents = false;
            }
        }

        return this;
    }

    /// <summary>
    /// Removes a watch registration.
    /// </summary>
    public bool RemoveWatch(string watchId)
    {
        WatchRegistration? registration = null;

        lock (watchLock)
        {
            if (!watchRegistrations.TryGetValue(watchId, out registration))
                return false;

            watchRegistrations.Remove(watchId);
            watcherToWatchId.Remove(registration.Watcher);

            if (!string.IsNullOrWhiteSpace(registration.Options.Key))
                keyToWatchId.Remove(registration.Options.Key!);
        }

        registration.Watcher.EnableRaisingEvents = false;
        registration.Watcher.Dispose();

        Interlocked.Increment(ref totalRemoved);

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Removed watch '{watchId}' on path '{registration.WatchedPath}'.");

        PublishEvent(new FileWatchRemovedEvent(watchId, registration.WatchedPath, registration.Options.Key));
        return true;
    }

    /// <summary>
    /// Removes a watch registration by key.
    /// </summary>
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
    /// Removes all watch registrations.
    /// </summary>
    public NoireFileWatcher ClearAllWatches()
    {
        List<string> watchIds;
        lock (watchLock)
            watchIds = watchRegistrations.Keys.ToList();

        foreach (var watchId in watchIds)
            RemoveWatch(watchId);

        PublishEvent(new FileWatchesClearedEvent(watchIds.Count));
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
                TotalCallbackExceptionsCaught: totalCallbackExceptionsCaught);
        }
    }

    #endregion

    #region Private Helper Methods

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

        if (!registration.Options.NotifyOnRenamed)
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

        if (EnableLogging)
            NoireLogger.LogError(this, exception, $"Watcher error on '{registration.WatchedPath}'.");
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
        if (SuppressDuplicateNotifications && IsSuppressedDuplicate(notification))
        {
            Interlocked.Increment(ref totalDuplicateNotificationsSuppressed);
            return;
        }

        List<CallbackRegistration> callbacks;

        lock (watchLock)
            callbacks = registration.Callbacks.ToList();

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

    private bool IsSuppressedDuplicate(FileWatchNotification notification)
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

    private static bool IsEventEnabled(FileWatchRegistrationOptions options, FileWatchEventType eventType)
    {
        return eventType switch
        {
            FileWatchEventType.Changed => options.NotifyOnChanged,
            FileWatchEventType.Created => options.NotifyOnCreated,
            FileWatchEventType.Deleted => options.NotifyOnDeleted,
            FileWatchEventType.Renamed => options.NotifyOnRenamed,
            FileWatchEventType.Error => options.NotifyOnError,
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

    private void PublishEvent<TEvent>(TEvent eventData)
    {
        EventBus?.Publish(eventData);
    }

    #endregion

    /// <summary>
    /// Internal dispose method called when the module is disposed.
    /// </summary>
    protected override void DisposeInternal()
    {
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
            NoireLogger.LogInfo(this, $"FileWatcher disposed. Watches: {stats.RegisteredWatches}, Notifications: {stats.TotalNotificationsDispatched}, Errors: {stats.TotalErrors}");
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
