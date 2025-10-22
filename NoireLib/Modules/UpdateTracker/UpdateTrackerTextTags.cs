namespace NoireLib.UpdateTracker;

/// <summary>
/// Provides text tags for use in update notifications.
/// </summary>
public static class UpdateTrackerTextTags
{
    /// <summary>
    /// Will be replaced with the current version of the plugin.
    /// </summary>
    public const string CurrentVersion = "{{CURRENT_VERSION}}";

    /// <summary>
    /// Will be replaced with the new version available of the plugin.
    /// </summary>
    public const string NewVersion = "{{NEW_VERSION}}";

    /// <summary>
    /// Will be replaced with the internal name of the plugin.
    /// </summary>
    public const string PluginInternalName = "{{PLUGIN_NAME}}";
}
