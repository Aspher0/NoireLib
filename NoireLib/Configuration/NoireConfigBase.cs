using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NoireLib.Configuration.Migrations;
using NoireLib.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Configuration;

/// <summary>
/// Base class for NoireLib configuration classes that provides automatic JSON serialization and file management.
/// </summary>
[Serializable]
public abstract class NoireConfigBase : INoireConfig
{
    /// <summary>
    /// Backing field for <see cref="IsDegraded"/>
    /// </summary>
    protected bool degradedLoad;

    /// <summary>
    /// Backing field for <see cref="DegradedBackupPath"/>
    /// </summary>
    protected string? degradedBackupPath;

    private bool degradedSaveRefusalLogged;

    // Guards the queued payload. Never held while touching a file.
    private readonly object stateGate = new();

    // Guards taking the queued payload and writing it. Two writes of one configuration never overlap.
    private readonly object writeGate = new();

    // The JSON a queued save will write, serialized on the thread that asked for it.
    private string? pendingJson;

    private string? pendingPath;

    private long pendingDueAt;

    // When the oldest unwritten change arrived. Caps how long a run of changes defers the write.
    private long pendingSince;

    // The background loop draining the queued payload, or null when none is running.
    private Task? flushTask;

    // The file text as last read or written by this instance, or null when unknown. What the unchanged-save skip
    // compares against in memory.
    private string? lastPersistedJson;

    // The fingerprint of the state last handed to persistence. Meaningless while hasBaselineFingerprint is false.
    private long baselineFingerprint;

    private bool hasBaselineFingerprint;

    // Whether an access armed this configuration for a check, 0 or 1, written interlocked.
    private int accessArmed;

    private bool autoSaveEnrolled;

    // The compiled fingerprint for the concrete type, or null when not enrolled.
    private Func<object, long>? fingerprint;

    private bool setupComplete;

    // The configurations holding a payload that is not on disk yet.
    private static readonly ConcurrentDictionary<NoireConfigBase, byte> PendingWriters = new(ReferenceComparer.Instance);

    /// <summary>
    /// How long <see cref="RequestSave"/> waits for further changes before it writes.
    /// </summary>
    public static TimeSpan SaveDebounceInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The longest a change may stay unwritten while further changes keep arriving.
    /// </summary>
    public static TimeSpan MaxSaveDelay { get; set; } = TimeSpan.FromSeconds(2);

    private const int FlushPollMilliseconds = 250;

    // Compares configurations by identity. A derived type may define its own equality.
    private sealed class ReferenceComparer : IEqualityComparer<NoireConfigBase>
    {
        public static readonly ReferenceComparer Instance = new();

        public bool Equals(NoireConfigBase? x, NoireConfigBase? y) => ReferenceEquals(x, y);

        public int GetHashCode(NoireConfigBase obj) => RuntimeHelpers.GetHashCode(obj);
    }

    /// <summary>
    /// Serializer settings for configuration files, pinned here rather than taken from the process-global
    /// <see cref="JsonConvert.DefaultSettings"/>.
    /// </summary>
    protected static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        ObjectCreationHandling = ObjectCreationHandling.Replace,

        TypeNameHandling = TypeNameHandling.None,
        PreserveReferencesHandling = PreserveReferencesHandling.None,
    };

    private static readonly JsonSerializer ConfigSerializer = CreateConfigSerializer();

    private static JsonSerializer CreateConfigSerializer()
    {
        var serializer = JsonSerializer.Create(JsonSettings);

        serializer.CheckAdditionalContent = true;
        return serializer;
    }

    // Serializes this instance to the JSON written to the configuration file.
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

    // Deserializes the contents of a configuration file into a new instance of the given type.
    private static object? DeserializeConfigFromJson(string json, Type type)
    {
        using var stringReader = new StringReader(json);
        using var jsonReader = new JsonTextReader(stringReader);

        return ConfigSerializer.Deserialize(jsonReader, type);
    }

    /// <summary>
    /// The schema version this build targets. Never the file version, which <see cref="Load"/> migrates up to it.
    /// </summary>
    public abstract int Version { get; set; }

    /// <summary>
    /// The configuration file name (with or without extension). Override to provide a custom name.
    /// </summary>
    /// <returns>The configuration file name.</returns>
    public abstract string GetConfigFileName();

    /// <summary>
    /// Whether this configuration loads in the background when NoireLib initializes. Leave it true unless you want
    /// the load to happen on first access instead.
    /// </summary>
    [JsonIgnore]
    public virtual bool LoadFromDiskOnInitialization => true;

    /// <summary>
    /// Whether a failed migration left this instance partially defaulted. <see cref="Save"/> refuses to write until
    /// <see cref="ClearDegradedState"/> or a successful <see cref="ForceSave"/> clears it.
    /// </summary>
    /// <seealso cref="ForceSave"/>
    /// <seealso cref="ClearDegradedState"/>
    [JsonIgnore]
    public bool IsDegraded => degradedLoad;

    /// <summary>
    /// The pre-migration backup behind <see cref="IsDegraded"/>, or null when not degraded or no backup was written.
    /// </summary>
    [JsonIgnore]
    public string? DegradedBackupPath => degradedBackupPath;

    // Whether the current degraded state already logged its save refusal.
    [JsonIgnore]
    internal bool HasLoggedDegradedSaveRefusal => degradedSaveRefusalLogged;

    // The version a fresh instance of each type reports. Cached: first construction of a type costs hundreds of
    // milliseconds.
    private static readonly ConcurrentDictionary<Type, int> DefaultVersions = new();

    // Whether each type carries utoSaveAttribute, computed once per type.
    private static readonly ConcurrentDictionary<Type, bool> EnrollmentByType = new();

    /// <summary>
    /// The schema version this type declares, read from a fresh instance rather than from <see cref="Version"/>.
    /// </summary>
    /// <returns>The version a new instance reports, or <see cref="Version"/> when none can be constructed.</returns>
    protected virtual int GetDefaultVersion()
    {
        var configType = GetType();

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
    /// The full path to the configuration file, from <see cref="GetConfigFileName"/> against the plugin's
    /// configuration directory. Override to relocate the file. Every file operation goes through it.
    /// </summary>
    /// <returns>The full path, or null when NoireLib is not initialized or the file name is invalid.</returns>
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
    /// Saves to the JSON file, blocking until the write lands. Refused while <see cref="IsDegraded"/> is true.
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
            bool success;

            lock (writeGate)
            {
                // Dropping the queued payload stops it landing afterwards with older values.
                DiscardPendingPayload();
                success = WriteSerializedConfig(filePath, currentJson);
            }

            if (success)
                RefreshFingerprintBaseline();

            return success;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<NoireConfigBase>(ex, $"Failed to save configuration to: {filePath}");
            return false;
        }
    }

#if DEBUG
    // Serialize duration in milliseconds above which a save is logged as slow.
    private const double SlowSerializeMs = 20;
#endif

    /// <summary>
    /// Captures the configuration on the calling thread and writes it on a background thread shortly after. A change
    /// made after this returns belongs to the next write.
    /// </summary>
    /// <seealso cref="FlushPendingSave"/>
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

        try
        {
            currentJson = SerializeForSave();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<NoireConfigBase>(ex, $"Failed to serialize configuration for: {filePath}");
            return;
        }

        // Refreshed here so the autosave check does not capture the same state a second time on the next tick.
        RefreshFingerprintBaseline();

        QueueSerializedPayload(currentJson, filePath);
    }

    // Queues an already-serialized payload for the debounced background write shared with RequestSave.
    internal void QueueSerializedPayload(string json, string filePath)
    {
        var now = Environment.TickCount64;

        lock (stateGate)
        {
            if (pendingJson == null)
                pendingSince = now;

            pendingJson = json;
            pendingPath = filePath;

            var due = now + (long)SaveDebounceInterval.TotalMilliseconds;
            var deadline = pendingSince + (long)MaxSaveDelay.TotalMilliseconds;
            pendingDueAt = due < deadline ? due : deadline;

            PendingWriters[this] = 0;

            flushTask ??= Task.Run(RunFlushLoop);
        }
    }

    /// <summary>
    /// Writes anything <see cref="RequestSave"/> queued for this configuration, and waits for a running write.
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

    internal static bool FlushAllPendingSaves()
    {
        var allSuccess = true;

        // A payload can be queued while a pass is already walking. The pass count is bounded.
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

    // Stamps the instance with the schema this build declares and serializes it on the calling thread.
    private string SerializeForSave()
    {
        // From a fresh instance, not the property: a version assigned over it would mislabel the file.
        Version = GetDefaultVersion();

#if DEBUG
        var jsonStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var json = SerializeConfigToJson();
        var jsonMs = System.Diagnostics.Stopwatch.GetElapsedTime(jsonStartedAt).TotalMilliseconds;

        if (jsonMs > SlowSerializeMs)
            NoireLogger.LogWarning<NoireConfigBase>($"Serializing {GetType().Name} took {jsonMs:F0}ms.");

        return json;
#else
        return SerializeConfigToJson();
#endif
    }

    // Writes serialized JSON to disk, skipping the write when the file already holds it. Callers hold writeGate, which
    // also guards lastPersistedJson.
    private bool WriteSerializedConfig(string filePath, string json)
    {
        // The only disk read on this path, and only for a file this session has never touched.
        if (lastPersistedJson == null && FileHelper.FileExists(filePath))
            lastPersistedJson = FileHelper.ReadTextFromFile(filePath);

        if (json.Equals(lastPersistedJson, StringComparison.Ordinal) && FileHelper.FileExists(filePath))
        {
            NoireLogger.LogVerbose<NoireConfigBase>($"Configuration unchanged, skipping save: {filePath}");
            return true;
        }

        // Written atomically, so a crash mid-write leaves the previous file intact.
        var success = FileHelper.ReplaceFileAtomically(filePath, Encoding.UTF8.GetBytes(json));

        if (success)
        {
            lastPersistedJson = json;
            NoireLogger.LogVerbose<NoireConfigBase>($"Configuration saved successfully to: {filePath}");
        }

        return success;
    }

    // Waits out the debounce window and writes the queued payload, then exits once nothing is left queued.
    private async Task RunFlushLoop()
    {
        while (true)
        {
            long remaining;

            lock (stateGate)
            {
                if (pendingJson == null)
                {
                    // Cleared under the lock a request takes, so a request either joins this loop or starts one.
                    flushTask = null;
                    PendingWriters.TryRemove(this, out _);
                    return;
                }

                remaining = pendingDueAt - Environment.TickCount64;
            }

            if (remaining > 0)
            {
                // Sliced rather than waited out in one go, so a flush ends this loop promptly.
                await Task.Delay((int)Math.Min(remaining, FlushPollMilliseconds)).ConfigureAwait(false);
                continue;
            }

            WritePendingPayload();
        }
    }

    // Takes the queued payload and writes it, blocking while another write of this configuration is running.
    private bool WritePendingPayload()
    {
        // Taken inside the lock, not before it: otherwise a slow writer overwrites newer values with older ones.
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

    private void DiscardPendingPayload()
    {
        lock (stateGate)
        {
            pendingJson = null;
            pendingPath = null;
        }
    }

    // Logs a save refused for sDegraded: the first refusal of a degraded state at error level, the ones after it at
    // verbose.
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
    /// Saves even while <see cref="IsDegraded"/>, clearing the degraded state only if the write lands.
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

        // Cleared before delegating, so a derived Save() override sees a consistent state.
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
            // From a finally: the virtual Save can throw rather than report false, and nothing was written.
            if (!success && wasDegraded)
            {
                degradedLoad = true;
                degradedBackupPath = previousBackupPath;
            }
        }
    }

    /// <summary>
    /// Clears <see cref="IsDegraded"/> and <see cref="DegradedBackupPath"/> without writing or checking the values,
    /// allowing <see cref="Save"/> again.
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
    /// Loads the file into this instance, backing it up and migrating it when its version is older than
    /// <see cref="Version"/>, and latching <see cref="IsDegraded"/> when that migration fails.
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
            // Paid here, off the first changed setting, and normally on the background load thread.
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

            var rawDiskText = json;
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

            // The copy above brought the file's version across; leaving it would skip the next needed migration.
            Version = targetVersion;

            // Un-migrated JSON deserializes silently, so only the latch marks it. Assigned unconditionally, so a
            // later successful load clears a stale one.
            var migrationFailed = fileVersion < targetVersion && !migrationSuccess;
            degradedLoad = migrationFailed;
            degradedBackupPath = migrationFailed ? backupPath : null;

            // Reset so a fresh degraded state logs its own explanation rather than reusing an earlier one.
            degradedSaveRefusalLogged = false;

            var migrated = fileVersion < targetVersion && migrationSuccess;

            if (migrated)
            {
                NoireLogger.LogDebug<NoireConfigBase>("Saving migrated configuration to disk...");
                Save();
            }

            // The migrated branch's Save already recorded what it wrote; only an unmigrated load knows the disk text.
            CompleteLoadSetup(migrated ? null : rawDiskText);

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

    // Prepares the instance once: enrollment, compiled fingerprint, serializer warmup and change baselines.
    internal void CompleteLoadSetup(string? diskText)
    {
        if (setupComplete)
        {
            if (diskText != null)
            {
                lock (writeGate)
                    lastPersistedJson = diskText;
            }

            return;
        }

        autoSaveEnrolled = EnrollmentByType.GetOrAdd(GetType(), ComputeEnrollment);

        if (autoSaveEnrolled)
        {
            try
            {
                fingerprint = ConfigFingerprint.ForType(GetType());
            }
            catch (Exception ex)
            {
                NoireLogger.LogError<NoireConfigBase>(ex,
                    $"Could not build the change fingerprint for {GetType().Name}; automatic deep saving is disabled for it.");
            }
        }

        try
        {
            // Builds the Newtonsoft contract for this type here rather than on the first changed setting.
            SerializeConfigToJson();
        }
        catch (Exception ex)
        {
            NoireLogger.LogVerbose<NoireConfigBase>($"Could not warm the serializer for {GetType().Name}: {ex.Message}");
        }

        if (diskText != null)
        {
            lock (writeGate)
                lastPersistedJson = diskText;
        }

        RefreshFingerprintBaseline();
        setupComplete = true;
    }

    // Whether a type carries utoSaveAttribute on the class or on any public instance member.
    private static bool ComputeEnrollment(Type type)
    {
        if (type.GetCustomAttribute<AutoSaveAttribute>(inherit: true) != null)
            return true;

        return type
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Any(member => member.GetCustomAttribute<AutoSaveAttribute>(inherit: true) != null);
    }

    // Arms one autosave check on the next framework tick. Called by every access path.
    internal void MarkAccessed()
    {
        if (!autoSaveEnrolled)
            return;

        if (Interlocked.CompareExchange(ref accessArmed, 1, 0) == 0)
            NoireConfigWatch.Arm(this);
    }

    // Clears the armed flag before a check runs. An access during the check arms the next one.
    internal void ResetArm() => Volatile.Write(ref accessArmed, 0);

    // Compares the content fingerprint to the baseline and queues a write when they differ.
    internal bool RunAutoSaveCheck()
    {
        if (degradedLoad || fingerprint == null)
            return false;

        long current;

        try
        {
            current = fingerprint(this);
        }
        catch
        {
            // A mutator on another thread changed a collection mid-walk; the next tick reads a settled state.
            return true;
        }

        if (hasBaselineFingerprint && current == baselineFingerprint)
            return false;

        var filePath = GetConfigFilePath();
        if (string.IsNullOrEmpty(filePath))
            return false;

        string json;

        try
        {
            json = SerializeForSave();
        }
        catch (Exception ex)
        {
            NoireLogger.LogDebug<NoireConfigBase>($"Could not capture a change of {GetType().Name} this tick: {ex.Message}");
            return true;
        }

        baselineFingerprint = current;
        hasBaselineFingerprint = true;
        QueueSerializedPayload(json, filePath);
        return true;
    }

    private void RefreshFingerprintBaseline()
    {
        if (fingerprint == null)
            return;

        try
        {
            baselineFingerprint = fingerprint(this);
            hasBaselineFingerprint = true;
        }
        catch
        {
            // Unreadable mid-mutation state: the next check treats everything as changed and self-heals.
            hasBaselineFingerprint = false;
        }
    }

    // Copies the configuration file to a sibling backup before a migration is attempted.
    private static string? CreateMigrationBackup(string filePath, int fileVersion)
    {
        // Named for the version rather than the moment, so retrying a failing migration keeps one backup per schema.
        var backupPath = $"{filePath}.v{fileVersion}.bak";

        // Kept rather than overwritten: a later degraded write must not replace the last good copy.
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

    private static int GetVersionFromJson(string json)
    {
        try
        {
            // Parsed, not deserialized: JsonConvert merges the process-global DefaultSettings, JObject.Parse does not.
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

        var properties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

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
                lock (writeGate)
                    lastPersistedJson = null;

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

    // Whether this instance is at its defaults because no file exists yet, rather than because a load failed. oad
    // returns false for both.
    internal bool IsUnwrittenDefault => !string.IsNullOrEmpty(GetConfigFilePath()) && !Exists();
}
