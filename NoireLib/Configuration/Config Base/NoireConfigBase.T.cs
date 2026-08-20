using NoireLib.Helpers.ObjectExtensions;

namespace NoireLib.Configuration;

/// <summary>
/// Configuration base adding a lazily loaded, cached <see cref="Instance"/> singleton for the concrete type.
/// </summary>
/// <typeparam name="T">The concrete configuration type.</typeparam>
public abstract class NoireConfigBase<T> : NoireConfigBase where T : NoireConfigBase<T>, new()
{
    private static T? CachedInstance;
    private static readonly object InstanceLock = new();

    /// <summary>The singleton instance of this configuration, loaded from disk on first access.</summary>
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

    /// <summary>Reloads the configuration from disk and updates the singleton instance.</summary>
    public static void Reload()
    {
        lock (InstanceLock)
        {
            // Evicted first, since GetConfig returns a cached instance without touching the file.
            NoireConfigManager.UnloadConfig<T>();
            CachedInstance = LoadProxiedInstance();
        }
    }

    /// <summary>
    /// Loads the configuration from disk and transfers it onto the auto-save wrapper that consumers hold.
    /// </summary>
    /// <returns>The wrapper carrying the loaded values, or null when the configuration could not be loaded.</returns>
    private static T? LoadProxiedInstance()
    {
        var rawInstance = NoireConfigManager.GetConfig<T>();
        var proxy = NoireConfigAutoSaveProxy.Create(rawInstance);

        // Create returns the raw instance itself when there are no [AutoSave] members or the proxy failed to build.
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
                // The copy runs derived setters and can throw; left set, the suppression would disable auto-save for
                // the rest of the session. Restored rather than cleared, so an enclosing copy keeps its suppression.
                IsInternalCopying = wasCopying;
            }

            // Loading cached the raw instance while consumers hold the proxy, so SaveAllCached would otherwise write
            // the load-time snapshot.
            NoireConfigManager.ReplaceCachedInstance(typeof(T), proxy);

            // The proxy is a generated subclass with a serializer contract of its own that the load did not build.
            // Warmed while this thread still has it to itself.
            WarmSerializerFor(proxy);
        }

        return proxy;
    }

    /// <summary>Clears the cached instance, so the next <see cref="Instance"/> access reloads from disk.</summary>
    public static void ClearCache()
    {
        lock (InstanceLock)
        {
            // The manager entry goes with it, or a warm manager cache would satisfy the next access with no read.
            NoireConfigManager.UnloadConfig<T>();
            CachedInstance = null;
        }
    }
}
