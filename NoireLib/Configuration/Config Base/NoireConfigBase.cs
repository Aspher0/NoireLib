using Castle.DynamicProxy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NoireLib.Configuration.Migrations;
using NoireLib.Helpers;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace NoireLib.Configuration;

/// <summary>
/// Base class for NoireLib configuration classes that provides automatic JSON serialization and file management.
/// </summary>
[Serializable]
public abstract class NoireConfigBase : INoireConfig
{
    /// <summary>
    /// Suppresses auto-save while the member copy that transfers a loaded configuration onto its auto-save wrapper is
    /// running on the calling thread. Thread-static, not process-wide, so a save raised on another thread by an
    /// unrelated consumer still persists, and copies of two different configuration types can overlap.
    /// </summary>
    [ThreadStatic]
    internal static bool IsInternalCopying;

    /// <summary>
    /// Backing field for <see cref="IsDegraded"/>. Protected, not private, so the auto-save wrapper's reflected
    /// member copy can see it.
    /// </summary>
    protected bool degradedLoad;

    /// <summary>
    /// Backing field for <see cref="DegradedBackupPath"/>, protected for the same reason as <see cref="degradedLoad"/>.
    /// </summary>
    protected string? degradedBackupPath;

    /// <summary>
    /// Whether the degraded-state explanation has already been logged. Deliberately private, unlike
    /// <see cref="degradedLoad"/>: it must not carry across the member copy, so the copy's own first refusal still
    /// logs in full.
    /// </summary>
    private bool degradedSaveRefusalLogged;

    /// <summary>
    /// Serializer settings for configuration files. <see cref="Newtonsoft.Json.TypeNameHandling"/> and
    /// <see cref="Newtonsoft.Json.PreserveReferencesHandling"/> are pinned rather than inherited from the
    /// process-global <see cref="JsonConvert.DefaultSettings"/>, which other code in the process can reassign.
    /// </summary>
    protected static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,

        // Load-bearing: without this, Newtonsoft populates an existing collection instance instead of replacing it,
        // merging file values with the property initializer's defaults.
        ObjectCreationHandling = ObjectCreationHandling.Replace,

        // Stays off: type resolution driven by file content would let the file construct arbitrary types.
        TypeNameHandling = TypeNameHandling.None,
        PreserveReferencesHandling = PreserveReferencesHandling.None,
    };

    /// <summary>
    /// Serializer for reading and writing configuration files, built via
    /// <see cref="JsonSerializer.Create(JsonSerializerSettings)"/> so it resolves settings from
    /// <see cref="JsonSettings"/> alone rather than merging in the process-global
    /// <see cref="JsonConvert.DefaultSettings"/>.
    /// </summary>
    private static readonly JsonSerializer ConfigSerializer = CreateConfigSerializer();

    private static JsonSerializer CreateConfigSerializer()
    {
        var serializer = JsonSerializer.Create(JsonSettings);

        // A configuration file holds exactly one JSON document; content after it means the file is corrupt.
        serializer.CheckAdditionalContent = true;
        return serializer;
    }

    /// <summary>
    /// Serializes this instance to the JSON written to the configuration file.
    /// </summary>
    /// <returns>The indented JSON representation of this instance, serialized as its concrete type.</returns>
    private string SerializeConfigToJson()
    {
        var builder = new StringBuilder(256);

        using (var stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture))
        using (var jsonWriter = new JsonTextWriter(stringWriter))
        {
            ConfigSerializer.Serialize(jsonWriter, this, GetType());
        }

        return builder.ToString();
    }

    /// <summary>
    /// Deserializes the contents of a configuration file into a new instance of the given type.
    /// </summary>
    /// <param name="json">The JSON read from the configuration file.</param>
    /// <param name="type">The concrete configuration type to materialize.</param>
    /// <returns>The deserialized instance, or null when the JSON holds a bare null.</returns>
    /// <exception cref="JsonException">The JSON is malformed or carries content after the configuration object.</exception>
    private static object? DeserializeConfigFromJson(string json, Type type)
    {
        using var stringReader = new StringReader(json);
        using var jsonReader = new JsonTextReader(stringReader);

        return ConfigSerializer.Deserialize(jsonReader, type);
    }

    /// <summary>
    /// The configuration schema version. Override with the current schema version as the property initializer;
    /// always reports the target schema, not the file's, and <see cref="Load"/> migrates the file up to it when
    /// they differ.
    /// </summary>
    public abstract int Version { get; set; }

    /// <summary>
    /// The configuration file name (with or without extension). Override to provide a custom name.
    /// </summary>
    /// <returns>The configuration file name.</returns>
    public abstract string GetConfigFileName();

    /// <summary>
    /// Determines whether the configuration should be automatically loaded from disk when NoireLib initializes.
    /// </summary>
    [JsonIgnore]
    public virtual bool LoadFromDiskOnInitialization => true;

    /// <summary>
    /// Whether this instance holds values a failed migration left partially defaulted, set by <see cref="Load"/>
    /// when the file's version is older than <see cref="Version"/> and migration fails.<br/>
    /// While true, <see cref="Save"/> refuses to write and returns false; <see cref="DegradedBackupPath"/> points to
    /// the pre-migration backup. Cleared by <see cref="ClearDegradedState"/> or a successful <see cref="ForceSave"/>.
    /// </summary>
    /// <seealso cref="ForceSave"/>
    /// <seealso cref="ClearDegradedState"/>
    [JsonIgnore]
    public bool IsDegraded => degradedLoad;

    /// <summary>
    /// The path to the pre-migration backup that caused <see cref="IsDegraded"/>, or null when not degraded or no
    /// backup could be written. A recovery flow restores from this file.
    /// </summary>
    [JsonIgnore]
    public string? DegradedBackupPath => degradedBackupPath;

    /// <summary>
    /// Whether the current degraded state's full explanation has already been logged; later refusals log at verbose
    /// level instead. Reset whenever the degraded state is decided anew, by <see cref="Load"/>,
    /// <see cref="ForceSave"/> or <see cref="ClearDegradedState"/>.
    /// </summary>
    internal bool HasLoggedDegradedSaveRefusal => degradedSaveRefusalLogged;

    /// <summary>
    /// The schema version this configuration type declares, read from a fresh instance rather than
    /// <see cref="Version"/>, which callers can reassign. Resolved from the instance rather than
    /// <see cref="object.GetType"/>, since a type with <see cref="AutoSaveAttribute"/> members reports its
    /// generated subclass instead.
    /// </summary>
    /// <returns>The version a new instance of this configuration type reports, or the value <see cref="Version"/>
    /// currently holds when no fresh instance can be constructed.</returns>
    protected virtual int GetDefaultVersion()
    {
        try
        {
            var configType = ProxyUtil.GetUnproxiedType(this);

            if (Activator.CreateInstance(configType) is NoireConfigBase configInstance)
                return configInstance.Version;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<NoireConfigBase>(ex, "Failed to get default version, using current version.");
        }

        return Version;
    }

    /// <summary>
    /// The full path to the configuration file, resolved from <see cref="GetConfigFileName"/> against the plugin's
    /// configuration directory. Override to relocate the file; every file operation goes through this method.
    /// </summary>
    /// <returns>The full path to the configuration JSON file, or null if NoireLib is not initialized or the file name is invalid.</returns>
    protected virtual string? GetConfigFilePath()
    {
        var fileName = GetConfigFileName();
        if (string.IsNullOrEmpty(fileName))
        {
            NoireLogger.LogError<NoireConfigBase>($"Configuration file name is null or empty: {GetType().Name}");
            return null;
        }

        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            fileName += ".json";

        return FileHelper.GetPluginConfigFilePath(fileName);
    }

    /// <summary>
    /// Saves the current configuration to a JSON file. Refuses to write and returns false while
    /// <see cref="IsDegraded"/> is true; use <see cref="ForceSave"/> to write anyway or
    /// <see cref="ClearDegradedState"/> once the values are repaired.
    /// </summary>
    /// <returns>True if the save operation was successful; otherwise, false.</returns>
    /// <seealso cref="IsDegraded"/>
    public virtual bool Save()
    {
        if (degradedLoad)
        {
            LogDegradedSaveRefusal();
            return false;
        }

        var filePath = GetConfigFilePath();
        if (string.IsNullOrEmpty(filePath))
        {
            if (!NoireService.IsInitialized())
                NoireLogger.LogWarning<NoireConfigBase>("Cannot save configuration: NoireLib is not initialized.");

            return false;
        }

        try
        {
            // Read from a fresh instance rather than the property, so a version assigned over the property cannot
            // mislabel the file and send a later load down a migration path that does not match its contents.
            var defaultVersion = GetDefaultVersion();
            Version = defaultVersion;

            var currentJson = SerializeConfigToJson();

            if (FileHelper.FileExists(filePath))
            {
                var existingJson = FileHelper.ReadTextFromFile(filePath);
                if (existingJson != null && existingJson.Equals(currentJson, StringComparison.Ordinal))
                {
                    NoireLogger.LogVerbose<NoireConfigBase>($"Configuration unchanged, skipping save: {filePath}");
                    return true;
                }
            }

            var success = FileHelper.WriteJsonToFile(filePath, this, JsonSettings);
            if (success)
                NoireLogger.LogVerbose<NoireConfigBase>($"Configuration saved successfully to: {filePath}");

            return success;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<NoireConfigBase>(ex, $"Failed to save configuration to: {filePath}");
            return false;
        }
    }

    /// <summary>
    /// Reports a save that <see cref="Save"/> refused because the instance is <see cref="IsDegraded"/>. The first
    /// refusal per degraded state logs the full explanation and backup location at error level; later refusals log
    /// at verbose level.
    /// </summary>
    private void LogDegradedSaveRefusal()
    {
        if (degradedSaveRefusalLogged)
        {
            NoireLogger.LogVerbose<NoireConfigBase>(
                $"Refusing to save degraded configuration {GetType().Name} again; the first refusal was logged as an error.");

            return;
        }

        degradedSaveRefusalLogged = true;

        var backupNote = degradedBackupPath != null
            ? $" The file as it was before the migration is backed up at: {degradedBackupPath}."
            : string.Empty;

        // The versions involved are not repeated here: Load logs them at the point it fails to migrate, which is the
        // only way this state is entered.
        NoireLogger.LogError<NoireConfigBase>(
            $"Refusing to save configuration {GetType().Name}: it was loaded from a file that could not be migrated to " +
            $"the current schema, so this instance holds partially defaulted values and saving would overwrite the file " +
            $"on disk with them.{backupNote} Call {nameof(ForceSave)}() to write anyway, or {nameof(ClearDegradedState)}() " +
            $"once the values have been repaired. Further refusals by this instance are logged at verbose level.");
    }

    /// <summary>
    /// Saves even when <see cref="IsDegraded"/> is true, overwriting the file with the values this instance
    /// currently holds; destructive, since a failed migration leaves those values at their defaults. Prefer
    /// repairing the instance and calling <see cref="ClearDegradedState"/>, or restoring
    /// <see cref="DegradedBackupPath"/>.<br/>
    /// A successful write clears the degraded state; a failed write leaves it in place. Equivalent to
    /// <see cref="Save"/> when the instance is not degraded.
    /// </summary>
    /// <returns>True if the save operation was successful; otherwise, false.</returns>
    /// <seealso cref="IsDegraded"/>
    /// <seealso cref="ClearDegradedState"/>
    public virtual bool ForceSave()
    {
        var wasDegraded = degradedLoad;
        var previousBackupPath = degradedBackupPath;

        if (wasDegraded)
        {
            NoireLogger.LogWarning<NoireConfigBase>(
                $"Forcing a save of degraded configuration {GetType().Name}. The values on disk are being replaced by " +
                $"the partially defaulted values held in memory.");
        }

        // Clear before delegating rather than passing a flag through, so that a derived Save() override still runs and
        // still sees a consistent state.
        degradedLoad = false;
        degradedBackupPath = null;
        degradedSaveRefusalLogged = false;

        var success = false;

        try
        {
            success = Save();
            return success;
        }
        finally
        {
            // Only a write that actually landed retires the protection. Restored from a finally because Save is
            // virtual and can throw rather than report false; an exception leaving the state cleared would retire
            // the protection without anything having been written.
            if (!success && wasDegraded)
            {
                degradedLoad = true;
                degradedBackupPath = previousBackupPath;
            }
        }
    }

    /// <summary>
    /// Clears the degraded state described on <see cref="IsDegraded"/> without writing to disk, so
    /// <see cref="Save"/> is allowed again. Does not verify the repaired values are actually safe to persist; the
    /// next <see cref="Save"/> writes them as-is.<br/>
    /// <see cref="DegradedBackupPath"/> is cleared with it; read it first if still needed.
    /// </summary>
    /// <seealso cref="IsDegraded"/>
    /// <seealso cref="ForceSave"/>
    public virtual void ClearDegradedState()
    {
        degradedLoad = false;
        degradedBackupPath = null;
        degradedSaveRefusalLogged = false;
    }

    /// <summary>
    /// Loads the configuration from a JSON file and populates this instance, migrating automatically when the
    /// file's version is older than <see cref="Version"/>. The file is backed up before migration runs; a failed
    /// migration marks the instance <see cref="IsDegraded"/> instead of writing to disk.
    /// </summary>
    /// <returns>True if the load operation was successful; otherwise, false.</returns>
    /// <seealso cref="IsDegraded"/>
    public virtual bool Load()
    {
        var filePath = GetConfigFilePath();
        if (string.IsNullOrEmpty(filePath))
        {
            if (!NoireService.IsInitialized())
                NoireLogger.LogWarning<NoireConfigBase>("Cannot load configuration: NoireLib is not initialized.");

            return false;
        }

#if DEBUG
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
#endif

        try
        {
            if (!Exists())
            {
                NoireLogger.LogDebug<NoireConfigBase>($"Configuration file not found: {filePath}. Using default values.");
                return false;
            }

            var json = FileHelper.ReadTextFromFile(filePath);
            if (json == null)
            {
                NoireLogger.LogWarning<NoireConfigBase>($"Failed to read configuration from: {filePath}");
                return false;
            }

            var fileVersion = GetVersionFromJson(json);
            var targetVersion = Version;
            bool migrationSuccess = false;
            string? backupPath = null;

            if (fileVersion < targetVersion)
            {
                NoireLogger.LogInfo<NoireConfigBase>($"Configuration version mismatch: file={fileVersion}, target={targetVersion}. Attempting migration.");

                // A failed backup does not stop the load; the degraded latch below is what keeps the file safe.
                backupPath = CreateMigrationBackup(filePath, fileVersion);

                var migratedJson = MigrationExecutor.ExecuteMigrations(GetType(), json, fileVersion, targetVersion);

                if (migratedJson != null)
                {
                    migrationSuccess = true;
                    json = migratedJson;
                    NoireLogger.LogInfo<NoireConfigBase>($"Successfully migrated configuration from version {fileVersion} to {targetVersion}");
                }
                else
                {
                    var recoveryNote = backupPath != null
                        ? $"The file as it was before the migration is backed up at: {backupPath}."
                        : "No backup of the file could be written.";

                    NoireLogger.LogError<NoireConfigBase>(
                        $"Failed to migrate configuration {GetType().Name} from version {fileVersion} to {targetVersion}. " +
                        $"Loading the un-migrated values, which leaves anything the migration was meant to produce at its " +
                        $"default. Saving is blocked until the state is resolved. {recoveryNote}");
                }
            }

            var loadedConfig = DeserializeConfigFromJson(json, GetType());

            if (loadedConfig == null)
            {
                NoireLogger.LogWarning<NoireConfigBase>($"Failed to deserialize configuration from: {filePath}");
                return false;
            }

            CopyPropertiesFrom(loadedConfig);

            // Restored to the target: the copy above brought the file's version across, and leaving it would make
            // the next Load compare the file to its own stale version, silently skipping a migration it still needs.
            Version = targetVersion;

            // Deserializing un-migrated JSON mostly succeeds silently: unknown members are ignored and absent ones
            // default. Assigned rather than left alone, so a later successful load clears a stale latch.
            var migrationFailed = fileVersion < targetVersion && !migrationSuccess;
            degradedLoad = migrationFailed;
            degradedBackupPath = migrationFailed ? backupPath : null;

            // Reset here so a fresh degraded state logs its own explanation rather than reusing an earlier one.
            degradedSaveRefusalLogged = false;

            if (fileVersion < targetVersion && migrationSuccess)
            {
                NoireLogger.LogDebug<NoireConfigBase>("Saving migrated configuration to disk...");
                Save();
            }

            NoireConfigManager.AddConfigToCache(GetType(), this);

            NoireLogger.LogVerbose<NoireConfigBase>($"Configuration loaded successfully from: {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<NoireConfigBase>(ex, $"Failed to load configuration from: {filePath}");
            return false;
        }
        finally
        {
#if DEBUG
            stopwatch.Stop();
            NoireLogger.LogInfo(this, $"Loaded configuration \"{GetType().Name}\" in {stopwatch.ElapsedMilliseconds} ms");
#endif
        }
    }

    /// <summary>
    /// Copies the configuration file to a sibling backup before a migration is attempted.
    /// </summary>
    /// <param name="filePath">The full path to the configuration file to back up.</param>
    /// <param name="fileVersion">The schema version the file is currently at, which names the backup.</param>
    /// <returns>The full path to the backup, or null if no backup could be written.</returns>
    private static string? CreateMigrationBackup(string filePath, int fileVersion)
    {
        // Named for the version, not the moment, so retries do not add duplicate backups. The ".bak" suffix keeps
        // the backup from being picked up as a configuration file.
        var backupPath = $"{filePath}.v{fileVersion}.bak";

        // An existing backup from this version is kept rather than overwritten, so a later degraded write cannot
        // replace the last good copy.
        if (FileHelper.FileExists(backupPath))
        {
            NoireLogger.LogDebug<NoireConfigBase>($"A pre-migration backup already exists, keeping it: {backupPath}");
            return backupPath;
        }

        if (FileHelper.CopyFile(filePath, backupPath))
        {
            NoireLogger.LogInfo<NoireConfigBase>($"Backed up configuration to {backupPath} before migrating from version {fileVersion}.");
            return backupPath;
        }

        NoireLogger.LogWarning<NoireConfigBase>($"Could not back up configuration to {backupPath} before migrating from version {fileVersion}.");
        return null;
    }

    /// <summary>
    /// Extracts the version number from a JSON string.
    /// </summary>
    /// <param name="json">The JSON string.</param>
    /// <returns>The version number, or 0 if not found.</returns>
    private static int GetVersionFromJson(string json)
    {
        try
        {
            // Parsed, not deserialized: JsonConvert entry points merge in the process-global DefaultSettings, which
            // other code can reassign, and JObject.Parse consults no settings at all.
            var versionToken = JObject.Parse(json)["Version"];

            if (versionToken != null && versionToken.Type != JTokenType.Null)
                return versionToken.Value<int>();
        }
        catch
        {
            // Unparseable files report version 0, routing them into the migration path.
        }

        return 0;
    }

    /// <summary>
    /// Copies all properties from another instance to this instance.
    /// </summary>
    /// <param name="source">The source configuration to copy from.</param>
    protected virtual void CopyPropertiesFrom(object source)
    {
        if (source == null || source.GetType() != GetType())
            return;

        var properties = GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        foreach (var property in properties)
        {
            if (property.CanWrite && property.CanRead)
            {
                try
                {
                    var value = property.GetValue(source);
                    property.SetValue(this, value);
                }
                catch (Exception ex)
                {
                    NoireLogger.LogError<NoireConfigBase>(ex, $"Failed to copy property: {property.Name}");
                }
            }
        }
    }

    /// <summary>
    /// Deletes the configuration file.
    /// </summary>
    /// <returns>True if the delete operation was successful; otherwise, false.</returns>
    public virtual bool Delete()
    {
        var filePath = GetConfigFilePath();
        if (string.IsNullOrEmpty(filePath))
            return false;

        try
        {
            var success = FileHelper.DeleteFile(filePath);
            if (success)
            {
                NoireLogger.LogDebug<NoireConfigBase>($"Configuration file deleted: {filePath}");
            }
            return success;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<NoireConfigBase>(ex, $"Failed to delete configuration file: {filePath}");
            return false;
        }
    }

    /// <summary>
    /// Checks if the configuration file exists.
    /// </summary>
    /// <returns>True if the file exists; otherwise, false.</returns>
    public virtual bool Exists()
    {
        var filePath = GetConfigFilePath();
        return FileHelper.FileExists(filePath);
    }

    /// <summary>
    /// Whether this instance is at its defaults because no configuration file exists yet, rather than because
    /// loading one failed. <see cref="Load"/> returns false for both cases; this tells them apart, unlike
    /// <see cref="Exists"/> alone, which also reports false before NoireLib is initialized.
    /// </summary>
    internal bool IsUnwrittenDefault => !string.IsNullOrEmpty(GetConfigFilePath()) && !Exists();
}
