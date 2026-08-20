using Castle.DynamicProxy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NoireLib.Configuration.Migrations;
using NoireLib.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace NoireLib.Configuration;

/// <summary>
/// Base class for NoireLib configuration classes that provides automatic JSON serialization and file management.
/// </summary>
[Serializable]
public abstract class NoireConfigBase : INoireConfig
{
    /// <summary>
    /// Suppresses auto-save on the calling thread only, while the member copy that transfers a loaded configuration
    /// onto its auto-save wrapper runs.
    /// </summary>
    [ThreadStatic]
    internal static bool IsInternalCopying;

    /// <summary>
    /// Backing field for <see cref="IsDegraded"/>, protected so the auto-save wrapper's reflected member copy can
    /// see it.
    /// </summary>
    protected bool degradedLoad;

    /// <summary>
    /// Backing field for <see cref="DegradedBackupPath"/>, protected for the same reason as <see cref="degradedLoad"/>.
    /// </summary>
    protected string? degradedBackupPath;

    /// <summary>
    /// Whether the degraded-state explanation has already been logged, kept private so it does not carry across the
    /// member copy.
    /// </summary>
    private bool degradedSaveRefusalLogged;

    /// <summary>
    /// Guards the queued payload below, and is never held while a file is being touched.
    /// </summary>
    private readonly object stateGate = new();

    /// <summary>
    /// Guards taking the queued payload and writing it, so the newest payload is always the last one on disk and
    /// two writes of the same configuration never overlap.
    /// </summary>
    private readonly object writeGate = new();

    /// <summary>The JSON a queued save will write, already serialized on the thread that asked for it.</summary>
    private string? pendingJson;

    /// <summary>The path the queued payload belongs to.</summary>
    private string? pendingPath;

    /// <summary>When the queued payload becomes due, as <see cref="Environment.TickCount64"/>.</summary>
    private long pendingDueAt;

    /// <summary>When the oldest unwritten change arrived, which caps how long a run of changes can defer the write.</summary>
    private long pendingSince;

    /// <summary>The background loop draining the queued payload, or null when none is running.</summary>
    private Task? flushTask;

    /// <summary>
    /// The configurations holding a payload that is not on disk yet.
    /// </summary>
    private static readonly ConcurrentDictionary<NoireConfigBase, byte> PendingWriters = new(ReferenceComparer.Instance);

    /// <summary>
    /// The configuration types whose serializer has already been warmed, keyed by the exact type that is serialized.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, byte> WarmedSerializerTypes = new();

    /// <summary>
    /// How long <see cref="RequestSave"/> waits for further changes before it writes.
    /// </summary>
    public static TimeSpan SaveDebounceInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The longest a change may stay unwritten while further changes keep arriving.
    /// </summary>
    public static TimeSpan MaxSaveDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>The longest the background writer sleeps in one go while waiting for a window to close.</summary>
    private const int FlushPollMilliseconds = 250;

    /// <summary>Compares configurations by identity, since a derived type may define its own equality.</summary>
    private sealed class ReferenceComparer : IEqualityComparer<NoireConfigBase>
    {
        public static readonly ReferenceComparer Instance = new();

        public bool Equals(NoireConfigBase? x, NoireConfigBase? y) => ReferenceEquals(x, y);

        public int GetHashCode(NoireConfigBase obj) => RuntimeHelpers.GetHashCode(obj);
    }

    /// <summary>
    /// Serializer settings for configuration files, pinned rather than inherited from the process-global
    /// <see cref="JsonConvert.DefaultSettings"/> that other code can reassign.
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
    /// Serializer for reading and writing configuration files, resolving settings from <see cref="JsonSettings"/>
    /// alone rather than merging in the process-global <see cref="JsonConvert.DefaultSettings"/>.
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
    /// The schema version this build targets, overridden as a property initializer and never reporting the file's
    /// version, which <see cref="Load"/> migrates up to it.
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
    /// Whether a failed migration left this instance partially defaulted, which makes <see cref="Save"/> refuse to
    /// write until <see cref="ClearDegradedState"/> or a successful <see cref="ForceSave"/> clears it.
    /// </summary>
    /// <seealso cref="ForceSave"/>
    /// <seealso cref="ClearDegradedState"/>
    [JsonIgnore]
    public bool IsDegraded => degradedLoad;

    /// <summary>
    /// The path to the pre-migration backup that caused <see cref="IsDegraded"/>, or null when not degraded or no
    /// backup could be written.
    /// </summary>
    [JsonIgnore]
    public string? DegradedBackupPath => degradedBackupPath;

    /// <summary>
    /// Whether the current degraded state's full explanation has already been logged, reset whenever
    /// <see cref="Load"/>, <see cref="ForceSave"/> or <see cref="ClearDegradedState"/> decides that state anew.
    /// </summary>
    internal bool HasLoggedDegradedSaveRefusal => degradedSaveRefusalLogged;

    /// <summary>
    /// The schema version this configuration type declares, read from a fresh instance of the unproxied type, since
    /// a type with <see cref="AutoSaveAttribute"/> members reports its generated subclass from
    /// <see cref="object.GetType"/>.
    /// </summary>
    /// <returns>The version a new instance reports, or the current <see cref="Version"/> when no fresh instance can
    /// be constructed.</returns>
    protected virtual int GetDefaultVersion()
    {
        var configType = ProxyUtil.GetUnproxiedType(this);

        if (DefaultVersions.TryGetValue(configType, out var cached))
            return cached;

        try
        {
            if (Activator.CreateInstance(configType) is NoireConfigBase configInstance)
            {
                DefaultVersions[configType] = configInstance.Version;
                return configInstance.Version;
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<NoireConfigBase>(ex, "Failed to get default version, using current version.");
        }

        return Version;
    }

    /// <summary>
    /// The version a fresh instance of each configuration type reports, cached because constructing that instance
    /// costs hundreds of milliseconds the first time a type is built inside a plugin's load context.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, int> DefaultVersions = new();

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
    /// Saves the current configuration to its JSON file, blocking until the write has landed, and refusing while
    /// <see cref="IsDegraded"/> is true.
    /// </summary>
    /// <returns>True if the save operation was successful; otherwise, false.</returns>
    /// <seealso cref="IsDegraded"/>
    /// <seealso cref="RequestSave"/>
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
            var currentJson = SerializeForSave();

            lock (writeGate)
            {
                // Dropping the queued payload stops it landing afterwards with older values.
                DiscardPendingPayload();
                return WriteSerializedConfig(filePath, currentJson);
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<NoireConfigBase>(ex, $"Failed to save configuration to: {filePath}");
            return false;
        }
    }

#if DEBUG
    /// <summary>
    /// Captures the configuration on the calling thread and writes it shortly afterwards on a background thread, so
    /// a change made after this returns belongs to the next write.
    /// </summary>
    /// <seealso cref="FlushPendingSave"/>
    /// <summary>Serialize duration in milliseconds above which a save is logged as slow.</summary>
    private const double SlowSerializeMs = 20;
#endif

    public virtual void RequestSave()
    {
        if (degradedLoad)
        {
            LogDegradedSaveRefusal();
            return;
        }

        var filePath = GetConfigFilePath();
        if (string.IsNullOrEmpty(filePath))
        {
            if (!NoireService.IsInitialized())
                NoireLogger.LogWarning<NoireConfigBase>("Cannot save configuration: NoireLib is not initialized.");

            return;
        }

        string currentJson;

#if DEBUG
        var serializeStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
#endif

        try
        {
            currentJson = SerializeForSave();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<NoireConfigBase>(ex, $"Failed to serialize configuration for: {filePath}");
            return;
        }

#if DEBUG
        var serializeMs = System.Diagnostics.Stopwatch.GetElapsedTime(serializeStartedAt).TotalMilliseconds;
        if (serializeMs > SlowSerializeMs)
            NoireLogger.LogWarning<NoireConfigBase>($"Serializing {GetType().Name} took {serializeMs:F0}ms.");
#endif

        var now = Environment.TickCount64;

        lock (stateGate)
        {
            if (pendingJson == null)
                pendingSince = now;

            pendingJson = currentJson;
            pendingPath = filePath;

            var due = now + (long)SaveDebounceInterval.TotalMilliseconds;
            var deadline = pendingSince + (long)MaxSaveDelay.TotalMilliseconds;
            pendingDueAt = due < deadline ? due : deadline;

            PendingWriters[this] = 0;

            flushTask ??= Task.Run(RunFlushLoop);
        }
    }

    /// <summary>
    /// Writes anything <see cref="RequestSave"/> has queued for this configuration and waits for a write already
    /// running to finish.
    /// </summary>
    /// <returns>True when nothing was pending or the pending payload was written; otherwise, false.</returns>
    public bool FlushPendingSave() => WritePendingPayload();

    /// <summary>Whether this configuration is holding changes that are not on disk yet.</summary>
    [JsonIgnore]
    public bool HasPendingSave
    {
        get
        {
            lock (stateGate)
                return pendingJson != null;
        }
    }

    /// <summary>
    /// Writes every configuration holding queued changes.
    /// </summary>
    /// <returns>True when every pending payload reached disk; otherwise, false.</returns>
    public static bool FlushAllPendingSaves()
    {
        var allSuccess = true;

        // A configuration can queue a payload while the pass that would have caught it is already walking; the pass
        // count is bounded, so continuous setting changes cannot pin this loop.
        for (var pass = 0; pass < 4 && !PendingWriters.IsEmpty; pass++)
        {
            foreach (var config in PendingWriters.Keys)
            {
                try
                {
                    if (!config.FlushPendingSave())
                        allSuccess = false;
                }
                catch (Exception ex)
                {
                    allSuccess = false;

                    NoireLogger.LogError<NoireConfigBase>(ex,
                        $"Failed to flush the pending save of {config.GetType().Name}. The remaining pending saves are " +
                        $"still being flushed.");
                }
            }
        }

        return allSuccess;
    }

    /// <summary>
    /// Stamps the instance with the schema this build declares and serializes it on the calling thread.
    /// </summary>
    /// <returns>The JSON a save writes.</returns>
    private string SerializeForSave()
    {
#if DEBUG
        var versionStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
#endif

        // Read from a fresh instance rather than the property: a version assigned over the property would mislabel
        // the file and send a later load down a migration path that does not match its contents.
        Version = GetDefaultVersion();

#if DEBUG
        var versionMs = System.Diagnostics.Stopwatch.GetElapsedTime(versionStartedAt).TotalMilliseconds;
        var jsonStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var json = SerializeConfigToJson();
        var jsonMs = System.Diagnostics.Stopwatch.GetElapsedTime(jsonStartedAt).TotalMilliseconds;

        if (versionMs + jsonMs > SlowSerializeMs)
            NoireLogger.LogWarning<NoireConfigBase>(
                $"{GetType().Name} save split: default version {versionMs:F0}ms, json {jsonMs:F0}ms.");

        return json;
#else
        return SerializeConfigToJson();
#endif
    }

    /// <summary>
    /// Writes serialized configuration JSON to disk, skipping the write when the file already holds those bytes.
    /// </summary>
    /// <param name="filePath">The configuration file to write.</param>
    /// <param name="json">The JSON to write.</param>
    /// <returns>True if the file holds the given JSON on return; otherwise, false.</returns>
    private static bool WriteSerializedConfig(string filePath, string json)
    {
        if (FileHelper.FileExists(filePath))
        {
            var existingJson = FileHelper.ReadTextFromFile(filePath);
            if (existingJson != null && existingJson.Equals(json, StringComparison.Ordinal))
            {
                NoireLogger.LogVerbose<NoireConfigBase>($"Configuration unchanged, skipping save: {filePath}");
                return true;
            }
        }

        // Written atomically, so a crash mid-write leaves the previous file intact.
        var success = FileHelper.ReplaceFileAtomically(filePath, Encoding.UTF8.GetBytes(json));
        if (success)
            NoireLogger.LogVerbose<NoireConfigBase>($"Configuration saved successfully to: {filePath}");

        return success;
    }

    /// <summary>
    /// Waits out the debounce window and writes the queued payload, then exits once nothing is left queued.
    /// </summary>
    private async Task RunFlushLoop()
    {
        while (true)
        {
            long remaining;

            lock (stateGate)
            {
                if (pendingJson == null)
                {
                    // Cleared under the same lock a request takes, so a request arriving now either sees a live loop
                    // and leaves it to run, or sees none and starts a fresh one.
                    flushTask = null;
                    PendingWriters.TryRemove(this, out _);
                    return;
                }

                remaining = pendingDueAt - Environment.TickCount64;
            }

            if (remaining > 0)
            {
                // Sliced rather than waited out in one go, so a flush clearing the payload ends this loop promptly
                // however long the configured window is.
                await Task.Delay((int)Math.Min(remaining, FlushPollMilliseconds)).ConfigureAwait(false);
                continue;
            }

            WritePendingPayload();
        }
    }

    /// <summary>
    /// Takes the queued payload and writes it, blocking while another write of this configuration is running.
    /// </summary>
    /// <returns>True when nothing was queued or the queued payload was written; otherwise, false.</returns>
    private bool WritePendingPayload()
    {
        // The payload is taken inside this lock rather than before it: otherwise a slower writer could overwrite the
        // file with values older than the ones a faster one already put there.
        lock (writeGate)
        {
            string? json;
            string? path;

            lock (stateGate)
            {
                json = pendingJson;
                path = pendingPath;
                pendingJson = null;
                pendingPath = null;
            }

            if (json == null || path == null)
                return true;

            try
            {
                return WriteSerializedConfig(path, json);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError<NoireConfigBase>(ex, $"Failed to save configuration to: {path}");
                return false;
            }
        }
    }

    /// <summary>Drops the queued payload without writing it.</summary>
    private void DiscardPendingPayload()
    {
        lock (stateGate)
        {
            pendingJson = null;
            pendingPath = null;
        }
    }

    /// <summary>
    /// Serializes the instance once and discards the result, building the Newtonsoft contract and accessors for this
    /// type here rather than on the first changed setting.
    /// </summary>
    private void WarmSerializer()
    {
        var type = GetType();

        if (!WarmedSerializerTypes.TryAdd(type, 0))
            return;

        try
        {
            SerializeConfigToJson();
        }
        catch (Exception ex)
        {
            NoireLogger.LogVerbose<NoireConfigBase>($"Could not warm the serializer for {type.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Warms the serializer of a configuration the caller still holds exclusively.
    /// </summary>
    /// <param name="config">The configuration to warm.</param>
    internal static void WarmSerializerFor(NoireConfigBase config) => config.WarmSerializer();

    /// <summary>
    /// Reports a save refused because the instance is <see cref="IsDegraded"/>, logging the first refusal per
    /// degraded state at error level and later ones at verbose level.
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

        NoireLogger.LogError<NoireConfigBase>(
            $"Refusing to save configuration {GetType().Name}: it was loaded from a file that could not be migrated to " +
            $"the current schema, so this instance holds partially defaulted values and saving would overwrite the file " +
            $"on disk with them.{backupNote} Call {nameof(ForceSave)}() to write anyway, or {nameof(ClearDegradedState)}() " +
            $"once the values have been repaired. Further refusals by this instance are logged at verbose level.");
    }

    /// <summary>
    /// Saves even when <see cref="IsDegraded"/> is true, overwriting the file with values a failed migration may
    /// have left at their defaults, and clearing the degraded state only if the write lands.
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

        // Cleared before delegating rather than passing a flag through, so a derived Save() override still runs and
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
            // Restored from a finally because the virtual Save can throw rather than report false, which would
            // otherwise retire the degraded protection with nothing written.
            if (!success && wasDegraded)
            {
                degradedLoad = true;
                degradedBackupPath = previousBackupPath;
            }
        }
    }

    /// <summary>
    /// Clears <see cref="IsDegraded"/> and <see cref="DegradedBackupPath"/> without writing to disk or verifying the
    /// values, allowing <see cref="Save"/> again.
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
    /// Loads the configuration file into this instance, backing the file up and migrating it when its version is
    /// older than <see cref="Version"/>, and marking the instance <see cref="IsDegraded"/> if that migration fails.
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
            // Both costs are paid here rather than on the first changed setting, while nothing else holds this
            // instance; the default version is as expensive as the serializer, since it builds the type through
            // the proxy machinery.
            WarmSerializer();
            _ = GetDefaultVersion();

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

                // A failed backup does not stop the load; the degraded latch below keeps the file safe.
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

            // The copy above brought the file's version across; leaving it would make the next Load compare the file
            // to its own stale version and silently skip a migration it still needs.
            Version = targetVersion;

            // Deserializing un-migrated JSON mostly succeeds silently, so the latch is what marks it; assigned
            // unconditionally so a later successful load clears a stale one.
            var migrationFailed = fileVersion < targetVersion && !migrationSuccess;
            degradedLoad = migrationFailed;
            degradedBackupPath = migrationFailed ? backupPath : null;

            // Reset so a fresh degraded state logs its own explanation rather than reusing an earlier one.
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
        // Named for the version, not the moment, so retries do not add duplicate backups; the ".bak" suffix keeps
        // the backup from being picked up as a configuration file.
        var backupPath = $"{filePath}.v{fileVersion}.bak";

        // An existing backup from this version is kept, so a later degraded write cannot replace the last good copy.
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
    /// Extracts the schema version from configuration JSON.
    /// </summary>
    /// <param name="json">The configuration JSON.</param>
    /// <returns>The version number, or 0 when absent or unparseable.</returns>
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
    /// Whether this instance is at its defaults because no configuration file exists yet rather than because a load
    /// failed, which <see cref="Load"/> and <see cref="Exists"/> alone cannot distinguish.
    /// </summary>
    internal bool IsUnwrittenDefault => !string.IsNullOrEmpty(GetConfigFilePath()) && !Exists();
}
