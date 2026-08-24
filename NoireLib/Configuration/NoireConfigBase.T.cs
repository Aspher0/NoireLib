namespace NoireLib.Configuration;

/// <summary>
/// Configuration base adding a lazily loaded, cached <see cref="Instance"/> singleton for the concrete type.
/// </summary>
/// <typeparam name="T">The concrete configuration type.</typeparam>
public abstract class NoireConfigBase<T> : NoireConfigBase where T : NoireConfigBase<T>, new()
{
    /// <summary>
    /// The singleton instance of this configuration.
    /// </summary>
    public static T Instance => NoireConfigManager.GetConfig<T>()!;

    /// <summary>
    /// Reloads the configuration from disk and updates the singleton instance.
    /// </summary>
    public static void Reload() => NoireConfigManager.ReloadConfig<T>();

    /// <summary>
    /// Clears the cached instance, so the next <see cref="Instance"/> access reloads from disk.
    /// </summary>
    public static void ClearCache() => NoireConfigManager.UnloadConfig<T>();
}
