using NoireLib.Helpers.ObjectExtensions;

namespace NoireLib.Configuration;

/// <summary>
/// Generic base class for NoireLib configurations that provides automatic singleton instance management.
/// Inherit from this class to get automatic Instance property that handles loading and caching.
/// </summary>
/// <typeparam name="T">The concrete configuration type</typeparam>
public abstract class NoireConfigBase<T> : NoireConfigBase where T : NoireConfigBase<T>, new()
{
    private static T? CachedInstance;
    private static readonly object InstanceLock = new();

    /// <summary>
    /// The singleton instance of this configuration, loaded from disk on first access.
    /// </summary>
    public static T Instance
    {
        get
        {
            if (CachedInstance == null)
                lock (InstanceLock)
                    if (CachedInstance == null)
                        CachedInstance = LoadProxiedInstance();

            return CachedInstance!;
        }
    }

    /// <summary>
    /// Reloads the configuration from disk and updates the singleton instance.
    /// </summary>
    public static void Reload()
    {
        lock (InstanceLock)
            CachedInstance = LoadProxiedInstance();
    }

    /// <summary>
    /// Loads the configuration from disk and transfers it onto the auto-save wrapper that consumers hold.
    /// </summary>
    /// <returns>The wrapper carrying the loaded values, or null when the configuration could not be loaded.</returns>
    private static T? LoadProxiedInstance()
    {
        var rawInstance = NoireConfigManager.GetConfig<T>();
        var proxy = NoireConfigAutoSaveProxy.Create(rawInstance);

        // Create returns the raw instance itself when there are no [AutoSave] members or the proxy failed to build;
        // only a distinct proxy needs the copy and cache swap below.
        if (rawInstance != null && !ReferenceEquals(proxy, rawInstance))
        {
            // The copy assigns through the wrapper's intercepted setters; without this, every [AutoSave] member
            // would write the file it was just read from, once per member.
            var wasCopying = IsInternalCopying;
            IsInternalCopying = true;

            try
            {
                rawInstance.CopyMembersTo(proxy);
            }
            finally
            {
                // The copy can throw, since it runs whatever a derived setter does. Left set, the suppression would
                // outlive the copy and silently disable auto-save for the rest of the session. Restored rather than
                // cleared, so a copy further up the call stack keeps the suppression it relies on.
                IsInternalCopying = wasCopying;
            }

            // Loading cached the raw instance, but consumers hold the proxy. Swap the manager's entry for the proxy
            // so SaveAllCached writes the values consumers have been changing, not the load-time snapshot.
            NoireConfigManager.ReplaceCachedInstance(typeof(T), proxy);
        }

        return proxy;
    }

    /// <summary>
    /// Clears the cached instance. The next access to Instance will reload from disk.
    /// </summary>
    public static void ClearCache()
    {
        lock (InstanceLock)
            CachedInstance = null;
    }
}
