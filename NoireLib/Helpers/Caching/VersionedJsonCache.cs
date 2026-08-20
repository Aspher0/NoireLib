using Newtonsoft.Json;
using System;
using System.IO;

namespace NoireLib.Helpers;

/// <summary>
/// Keeps derived data on disk under a stamp of the game build, the plugin version and a caller-supplied schema
/// number, and misses rather than returns a payload whose stamp no longer matches. Read and write failures are
/// logged and reported as a miss, never raised.
/// </summary>
/// <typeparam name="T">The cached payload.</typeparam>
public sealed class VersionedJsonCache<T>
{
    private const string LogPrefix = "[VersionedCache] ";

    private readonly string path;
    private readonly Func<string> pluginVersion;

    /// <summary>
    /// Creates a cache backed by a file, stamped against the current game build and a plugin version.
    /// </summary>
    /// <param name="filePath">The cache file path.</param>
    /// <param name="pluginVersionProvider">The version to stamp, or null for the calling assembly's.</param>
    public VersionedJsonCache(string filePath, Func<string>? pluginVersionProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        path = filePath;
        pluginVersion = pluginVersionProvider ?? DefaultPluginVersion;
    }

    /// <summary> Gets the cache file path. </summary>
    public string Path => path;

    /// <summary> Gets a value indicating whether a cache file exists, whatever its stamp says. </summary>
    public bool Exists => File.Exists(path);

    /// <summary>
    /// Reads the cached payload, missing when there is none, it cannot be read, or its stamp no longer matches.
    /// </summary>
    /// <param name="schemaVersion">The caller's version number for the payload shape and meaning.</param>
    /// <returns>The payload, or default.</returns>
    public T? Read(int schemaVersion)
    {
        if (!File.Exists(path))
            return default;

        try
        {
            var file = FileHelper.ReadJsonFromFile<CacheFile>(path);

            if (file == null || !IsCurrent(file, schemaVersion))
                return default;

            return file.Payload;
        }
        catch (Exception ex)
        {
            NoireLogger.LogWarning($"Could not read the cache at '{path}'; rebuilding. {ex.Message}", LogPrefix);
            return default;
        }
    }

    /// <summary>
    /// Writes the payload atomically, stamped with the current game build, plugin version and schema version.
    /// </summary>
    /// <param name="payload">The payload to cache.</param>
    /// <param name="schemaVersion">The caller's version number for the payload shape and meaning.</param>
    /// <returns>True when the cache was written.</returns>
    public bool Write(T payload, int schemaVersion)
    {
        var file = new CacheFile
        {
            GameVersion = GameVersionHelper.CurrentGameVersion("unknown"),
            PluginVersion = pluginVersion(),
            SchemaVersion = schemaVersion,
            Payload = payload,
        };

        return FileHelper.WriteJsonToFile(path, file, atomic: true);
    }

    /// <summary> Deletes the cache file. </summary>
    /// <returns>True when a file was present and is now gone.</returns>
    public bool Invalidate() => File.Exists(path) && FileHelper.DeleteFile(path);

    /// <summary>
    /// Reports whether a stamp read off a cache file matches the current one on all three components.
    /// </summary>
    /// <param name="cachedGameVersion">The stamped game build.</param>
    /// <param name="cachedPluginVersion">The stamped plugin version.</param>
    /// <param name="cachedSchemaVersion">The stamped schema version.</param>
    /// <param name="gameVersion">The current game build.</param>
    /// <param name="pluginVersion">The current plugin version.</param>
    /// <param name="schemaVersion">The current schema version.</param>
    /// <returns>True when the cache may be used.</returns>
    public static bool IsStampCurrent(
        string? cachedGameVersion, string? cachedPluginVersion, int cachedSchemaVersion,
        string gameVersion, string pluginVersion, int schemaVersion)
        => cachedSchemaVersion == schemaVersion
        && string.Equals(cachedPluginVersion, pluginVersion, StringComparison.Ordinal)
        && string.Equals(cachedGameVersion, gameVersion, StringComparison.Ordinal);

    private bool IsCurrent(CacheFile file, int schemaVersion)
        => IsStampCurrent(
            file.GameVersion, file.PluginVersion, file.SchemaVersion,
            GameVersionHelper.CurrentGameVersion("unknown"), pluginVersion(), schemaVersion);

    private static string DefaultPluginVersion()
        => NoireService.PluginInstance?.GetType().Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary> The on-disk stamp and payload. </summary>
    private sealed class CacheFile
    {
        [JsonProperty("gameVersion")]
        public string GameVersion { get; set; } = string.Empty;

        [JsonProperty("pluginVersion")]
        public string PluginVersion { get; set; } = string.Empty;

        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; } = -1;

        [JsonProperty("payload")]
        public T? Payload { get; set; }
    }
}
