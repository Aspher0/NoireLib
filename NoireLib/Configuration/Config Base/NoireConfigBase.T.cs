using NoireLib.Helpers.ObjectExtensions;

namespace NoireLib.Configuration;

/// <summary>
/// Generic base class for NoireLib configurations that provides automatic singleton instance management.
/// Inherit from this class to get automatic Instance property that handles loading and caching.
/// </summary>
/// <typeparam name="T">The concrete configuration type</typeparam>
public abstract class NoireConfigBase<T> : NoireConfigBase where T : NoireConfigBase<T>, new()
{
    private static T? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets the singleton instance of this configuration.
    /// The configuration is automatically loaded from disk on first access.
    /// </summary>
    public static T Instance
    {
        get
        {
            if (_instance == null)
                lock (_lock)
                    if (_instance == null)
                    {
                        var rawInstance = NoireConfigManager.GetConfig<T>();
                        var proxy = NoireConfigAutoSaveProxy.Create(rawInstance);

                        IsInternalCopying = true;
                        rawInstance?.CopyMembersTo(proxy);
                        IsInternalCopying = false;

                        _instance = proxy;
                    }

            return _instance!;
        }
    }

    /// <summary>
    /// Reloads the configuration from disk and updates the singleton instance.
    /// </summary>
    public static void Reload()
    {
        lock (_lock)
        {
            var rawInstance = NoireConfigManager.GetConfig<T>();
            var proxy = NoireConfigAutoSaveProxy.Create(rawInstance);

            IsInternalCopying = true;
            rawInstance?.CopyMembersTo(proxy);
            IsInternalCopying = false;

            _instance = proxy;
        }
    }

    /// <summary>
    /// Clears the cached instance. The next access to Instance will reload from disk.
    /// </summary>
    public static void ClearCache()
    {
        lock (_lock)
            _instance = null;
    }
}
