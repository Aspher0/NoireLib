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
    /// Whether the calling thread is inside the member copy that transfers a loaded configuration onto its auto-save
    /// wrapper, which is what tells the auto-save interceptor to let that copy's assignments pass without persisting
    /// each one.<br/>
    /// Scoped to a thread rather than shared by the process, because the only save this has to suppress is the one the
    /// copy itself raises. The copy assigns through the wrapper's intercepted setters, the interceptor runs inline
    /// inside each of those assignments, and the save it would perform is therefore always on the thread doing the
    /// copying. A save raised on any other thread is an unrelated consumer persisting a real change, and suppressing
    /// that would apply the change in memory and silently never write it.<br/>
    /// A thread scope also keeps copies of two different configuration types from ending each other's suppression:
    /// they serialize on separate locks, one per closed generic type, so they can overlap.
    /// </summary>
    [ThreadStatic]
    internal static bool IsInternalCopying;

    /// <summary>
    /// Backing state for <see cref="IsDegraded"/>.<br/>
    /// Declared protected rather than private because the member copy that transfers a configuration onto its
    /// auto-save wrapper reflects over the concrete configuration type, and that reflection does not see private
    /// fields declared on a base class. A private field here would leave the wrapper undegraded, and the wrapper
    /// is the instance most consumers actually hold.
    /// </summary>
    protected bool degradedLoad;

    /// <summary>
    /// Backing state for <see cref="DegradedBackupPath"/>. Protected for the same reason as <see cref="degradedLoad"/>.
    /// </summary>
    protected string? degradedBackupPath;

    /// <summary>
    /// Whether the full explanation for refusing to save has already been logged for the current degraded state.<br/>
    /// Deliberately private, unlike <see cref="degradedLoad"/>: it must not travel with a member copy onto another
    /// instance, because the copy produces a second instance whose own first refusal is the one a reader of the log
    /// would see, and that refusal has to carry the explanation rather than assume an earlier line covered it.
    /// </summary>
    private bool degradedSaveRefusalLogged;

    /// <summary>
    /// Settings to serialize data.<br/>
    /// <see cref="Newtonsoft.Json.TypeNameHandling"/> and <see cref="Newtonsoft.Json.PreserveReferencesHandling"/> are
    /// pinned rather than left to their defaults. Every <see cref="JsonConvert"/> entry point layers the process-global
    /// <see cref="JsonConvert.DefaultSettings"/> underneath the settings it is given and only overlays the properties
    /// this object actually sets, so a property left unmentioned here would take its value from whatever other code
    /// loaded into this process assigned to that global. Pinning them keeps the file format decided here alone, for
    /// derived classes that hand this object to <see cref="JsonConvert"/> as well as for the serializer below.
    /// </summary>
    protected static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,

        // Load-bearing: without it Newtonsoft populates into the collection a property initializer already created
        // instead of replacing it, which silently keeps the initializer's comparer and merges defaults with the file.
        ObjectCreationHandling = ObjectCreationHandling.Replace,

        // Type resolution driven by file content is what turns a configuration file into an instruction to construct
        // arbitrary types. It stays off.
        TypeNameHandling = TypeNameHandling.None,
        PreserveReferencesHandling = PreserveReferencesHandling.None,
    };

    /// <summary>
    /// Reads and writes the configuration file. It is built with
    /// <see cref="JsonSerializer.Create(JsonSerializerSettings)"/>, which resolves every setting from
    /// <see cref="JsonSettings"/> alone. The <see cref="JsonConvert"/> overloads and
    /// <see cref="JsonSerializer.CreateDefault(JsonSerializerSettings)"/> instead merge in
    /// <see cref="JsonConvert.DefaultSettings"/>, a process-global that any other code loaded into this process can
    /// assign, which would let unrelated code decide how the user's configuration is written and read back.
    /// </summary>
    private static readonly JsonSerializer ConfigSerializer = CreateConfigSerializer();

    private static JsonSerializer CreateConfigSerializer()
    {
        var serializer = JsonSerializer.Create(JsonSettings);

        // A configuration file holds exactly one JSON document, so anything after it means the file is corrupt.
        // JsonConvert.DeserializeObject turns this on for its callers implicitly; setting it explicitly keeps that
        // rejection in place now that the reads below go through a serializer instance instead.
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
    /// The version of the configuration schema, for potential migrations.<br/>
    /// Override this in a derived configuration and set the current schema version as the property initializer. The
    /// value an instance reports is always the schema it targets, never the schema of the file it was loaded from:
    /// <see cref="Load"/> compares the two, migrates the file's contents up when they differ, and leaves this reporting
    /// the target either way. It is serialized, so the number written to the file records the schema the file is at.
    /// </summary>
    public abstract int Version { get; set; }

    /// <summary>
    /// Gets the configuration file name (with or without extension).
    /// Override this method to provide a custom file name for your configuration.
    /// </summary>
    /// <returns>The configuration file name.</returns>
    public abstract string GetConfigFileName();

    /// <summary>
    /// Determines whether the configuration should be automatically loaded from disk when NoireLib initializes.
    /// </summary>
    [JsonIgnore]
    public virtual bool LoadFromDiskOnInitialization => true;

    /// <summary>
    /// Gets a value indicating whether this instance holds values that must not be written back to disk.<br/>
    /// This becomes true when <see cref="Load"/> finds a configuration file older than <see cref="Version"/> and the
    /// migration to the current schema fails. The file is still deserialized so the plugin can start, but members the
    /// migration was supposed to produce stay at their defaults, which makes the instance a partial view of the user's
    /// real settings.<br/>
    /// While this is true, <see cref="Save"/> refuses to write and returns false, so that ordinary saves performed
    /// during startup cannot replace a good file on disk with the partial values. <see cref="DegradedBackupPath"/>
    /// points at the copy of the file taken before the migration ran.<br/>
    /// The state is set by <see cref="Load"/> and reflects only the most recent load: a load that needs no migration,
    /// or whose migration succeeds, clears it. It is also cleared by <see cref="ClearDegradedState"/> and by a
    /// successful <see cref="ForceSave"/>.
    /// </summary>
    /// <seealso cref="ForceSave"/>
    /// <seealso cref="ClearDegradedState"/>
    [JsonIgnore]
    public bool IsDegraded => degradedLoad;

    /// <summary>
    /// Gets the full path to the copy of the configuration file taken before the failed migration that made this
    /// instance <see cref="IsDegraded"/>, or null when the instance is not degraded or no backup could be written.<br/>
    /// The file at this path is the user's configuration exactly as it was before the migration was attempted, and is
    /// what a recovery flow should restore from.
    /// </summary>
    [JsonIgnore]
    public string? DegradedBackupPath => degradedBackupPath;

    /// <summary>
    /// Gets a value indicating whether the current degraded state has already had its full explanation logged, which
    /// is what makes <see cref="Save"/> record the refusals following the first one at verbose level instead.<br/>
    /// It is reset every time the degraded state is decided anew, by <see cref="Load"/>, <see cref="ForceSave"/> and
    /// <see cref="ClearDegradedState"/>, so that each degraded state explains itself once in full.<br/>
    /// Read-only, which also keeps it out of the member copy that transfers a configuration onto its auto-save
    /// wrapper: the wrapper is a distinct instance whose own first refusal still owes the reader an explanation.
    /// </summary>
    internal bool HasLoggedDegradedSaveRefusal => degradedSaveRefusalLogged;

    /// <summary>
    /// Gets the schema version that this configuration's own type declares, read from a fresh instance of it rather
    /// than from <see cref="Version"/>, which anything holding the configuration is free to assign over.<br/>
    /// A configuration with members marked <see cref="AutoSaveAttribute"/> is handed to consumers as a generated
    /// subclass that intercepts their assignments, so the type such an instance reports is that subclass rather than
    /// the configuration whose declared version is wanted. The configuration type is therefore resolved from the
    /// instance rather than read from <see cref="object.GetType"/>, which also keeps this from building a second
    /// intercepting subclass every time it is asked.
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
    /// Gets the full path to the configuration file.<br/>
    /// Resolves <see cref="GetConfigFileName"/> against the plugin's configuration directory. Override this to place
    /// the file somewhere else; every file operation on the configuration (load, save, backup, delete and existence
    /// checks) goes through this method, so an override relocates all of them consistently.
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
    /// Saves the current configuration to a JSON file.<br/>
    /// Refuses to write and returns false while <see cref="IsDegraded"/> is true, because the instance then holds
    /// values that a failed migration left partially defaulted and writing them would destroy the user's file. Use
    /// <see cref="ForceSave"/> to write anyway, or <see cref="ClearDegradedState"/> once the values are repaired.
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
            // The number written to the file records the schema the values being written are at, which is the schema
            // this build defines whatever the property currently reports. Read from a fresh instance of this type
            // rather than from the property, so that a version assigned over it cannot mislabel the file and send a
            // later load down a migration path that does not match its contents.
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
    /// Reports a save that <see cref="Save"/> refused because the instance is <see cref="IsDegraded"/>.<br/>
    /// Members marked <see cref="AutoSaveAttribute"/> save on every assignment, so a degraded configuration refuses a
    /// save as often as anything assigns to one of them, which is often enough to bury the rest of the log. The first
    /// refusal of a degraded state therefore carries the whole explanation and the backup location, because the user
    /// has to learn that their configuration did not survive its migration and where the copy of it is. Every refusal
    /// after it is the same event repeating and is recorded at verbose level, which keeps it discoverable when the
    /// refusals themselves are what is being investigated without making the state cost an error per assignment.
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
    /// Saves the current configuration to a JSON file even when <see cref="IsDegraded"/> is true, overwriting the file
    /// on disk with the values this instance currently holds.<br/>
    /// This is the deliberate override of the protection described on <see cref="IsDegraded"/>, and it is destructive:
    /// after a failed migration the settings that the migration was supposed to produce are still at their defaults, so
    /// forcing the write replaces the user's stored values with those defaults. Prefer repairing the instance first and
    /// calling <see cref="ClearDegradedState"/>, or restoring <see cref="DegradedBackupPath"/>.<br/>
    /// A successful forced write clears the degraded state, since the file on disk then matches this instance and there
    /// is nothing left for the block to protect. A failed write leaves the degraded state in place.<br/>
    /// When the instance is not degraded this is exactly <see cref="Save"/>.
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
            // Only a write that actually landed retires the protection; otherwise the file on disk is still the one
            // worth protecting and the block has to stay. Restored from a finally because Save is virtual and resolves
            // the file path through another virtual member, so it can throw rather than report false. An exception
            // leaving the state cleared would retire the protection without anything having been written, and the next
            // ordinary save would then replace the user's file with the partially defaulted values.
            if (!success && wasDegraded)
            {
                degradedLoad = true;
                degradedBackupPath = previousBackupPath;
            }
        }
    }

    /// <summary>
    /// Clears the degraded state described on <see cref="IsDegraded"/> without writing anything to disk, so that
    /// <see cref="Save"/> is allowed again.<br/>
    /// Call this once the values a failed migration left at their defaults have been repaired in memory, whether by the
    /// plugin or by the user. It is an assertion that this instance is now safe to persist; it does not verify that, and
    /// the next ordinary <see cref="Save"/> will overwrite the file on disk.<br/>
    /// <see cref="DegradedBackupPath"/> is cleared with it, so read it first if the backup location is still needed.
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
    /// Loads the configuration from a JSON file and populates the current instance.
    /// Automatically executes migrations if the file version is older than the current version.<br/>
    /// When the file is older than <see cref="Version"/>, it is copied to a backup before the migration runs, and a
    /// migration that fails marks the instance <see cref="IsDegraded"/> instead of writing anything to disk.
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

                // Taken before the migration runs, while the file is still known to hold the user's settings at the
                // version they were written at. A failure to back up does not stop the load: nothing has been written
                // yet, and the degraded latch below is what actually keeps the file safe.
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

            // Version is a public read-write property, so the copy above brings the file's schema version across with
            // the settings and leaves this instance reporting it. What this instance holds is a view of the target
            // schema whatever the file said: values that needed no migration, values a migration has just produced, or,
            // when the migration failed, values the target schema defaults. The target is therefore restored here.
            // Leaving the file's version in place would make the next Load on this instance measure the file against
            // that stale value instead, so an unmigrated file would compare equal to its own version and the migration
            // it still needs would be skipped, silently, along with the degraded state that skip should have raised.
            // The migration decision above is unaffected: it is made from fileVersion, read out of the JSON, and from
            // targetVersion, read before this copy.
            Version = targetVersion;

            // Deserializing un-migrated JSON into the current type mostly succeeds, because unknown members are ignored
            // and absent ones keep their defaults. That silence is the danger: without this latch the instance looks
            // healthy and the next save replaces the user's file with the partial values. Assigned rather than only set,
            // so that a later load which needs no migration, or whose migration succeeds, clears a stale latch.
            var migrationFailed = fileVersion < targetVersion && !migrationSuccess;
            degradedLoad = migrationFailed;
            degradedBackupPath = migrationFailed ? backupPath : null;

            // A load decides the degraded state afresh, so the explanation is owed again rather than treated as already
            // given by a refusal from some earlier state of this instance.
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
        // Named for the version it was taken at rather than for the moment it was taken, which keeps exactly one backup
        // per schema version the file has been through. A timestamped name would add another copy on every start that
        // retries a failing migration. The ".bak" suffix keeps the backup from being picked up as a configuration file.
        var backupPath = $"{filePath}.v{fileVersion}.bak";

        // An existing backup was taken from this same file at this same version, on an earlier attempt, and is
        // therefore at least as trustworthy as anything that could be written now. Keeping it also means a forced or
        // otherwise degraded write that has since reached the file cannot be copied over the last good copy.
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
            // Parsed rather than deserialized on purpose. Every JsonConvert entry point layers the process-global
            // JsonConvert.DefaultSettings underneath the settings it is given, and that global is writable by any other
            // plugin sharing this process, so deserializing here would let a foreign TypeNameHandling apply to a file
            // this code has not validated. JObject.Parse reads the token stream and consults no settings at all.
            var versionToken = JObject.Parse(json)["Version"];

            if (versionToken != null && versionToken.Type != JTokenType.Null)
                return versionToken.Value<int>();
        }
        catch
        {
            // A file that cannot be parsed reports version 0, which routes it into the migration path rather than
            // letting it be treated as already current.
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
    /// Gets a value indicating whether this instance is at its defaults because there was no configuration file to load
    /// rather than because loading one did not work.<br/>
    /// True when the configuration resolves a file path and no file exists at it, which is the ordinary state of a
    /// plugin that has not saved its configuration yet. <see cref="Load"/> reports both cases the same way, by
    /// returning false, so this tells them apart for callers that keep the instance around: a default instance nothing
    /// was stored for is the real configuration and holding on to it is correct, while one that stands in for a file
    /// that exists and could not be read is a placeholder that should not outlive the attempt.<br/>
    /// The two are not distinguished by <see cref="Exists"/> alone, which also reports false when no path could be
    /// resolved at all, the state of a configuration reached before NoireLib is initialized.
    /// </summary>
    internal bool IsUnwrittenDefault => !string.IsNullOrEmpty(GetConfigFilePath()) && !Exists();
}
