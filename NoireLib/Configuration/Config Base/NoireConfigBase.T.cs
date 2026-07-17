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
    /// Gets the singleton instance of this configuration.
    /// The configuration is automatically loaded from disk on first access.
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

        // Create returns the raw instance itself when the configuration has no [AutoSave] members or when building the
        // proxy failed, and a distinct proxy otherwise. Only the distinct-proxy case needs the copy and the cache swap:
        // when the proxy is the raw instance there is one object and nothing has diverged.
        if (rawInstance != null && !ReferenceEquals(proxy, rawInstance))
        {
            // The copy assigns through the wrapper's intercepted setters, so without this every member marked [AutoSave]
            // would write the file it was just read from, once per member.
            var wasCopying = IsInternalCopying;
            IsInternalCopying = true;

            try
            {
                rawInstance.CopyMembersTo(proxy);
            }
            finally
            {
                // The copy reflects over the configuration's members and runs whatever a derived class does in a
                // property setter, so it can throw. Left set, the suppression would outlive the copy and disable
                // auto-save for every configuration on this thread for the rest of the session: settings would apply in
                // memory, never reach disk, and report no error. Restored to its previous value rather than cleared, so
                // that a copy running further up this call stack keeps the suppression it is relying on.
                IsInternalCopying = wasCopying;
            }

            // Loading cached the raw instance in the manager, but consumers hold the proxy returned here. Swap the
            // manager's entry for the proxy so a manager-level SaveAllCached writes the values consumers have been
            // changing rather than the raw load-time snapshot.
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
