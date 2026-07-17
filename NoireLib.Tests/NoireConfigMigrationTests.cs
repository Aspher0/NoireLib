using FluentAssertions;
using Newtonsoft.Json.Linq;
using NoireLib.Configuration;
using NoireLib.Configuration.Migrations;
using NoireLib.Helpers.ObjectExtensions;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the configuration migration path, locking the invariant that a configuration file is never
/// destroyed by a migration that does not work.<br/>
/// Deserializing un-migrated JSON into the current type mostly succeeds, because Newtonsoft ignores unknown members and
/// leaves absent ones at their defaults, so a failed migration produces a partially defaulted instance and no exception.
/// The protections under test are that such an instance refuses to persist (<see cref="NoireConfigBase.IsDegraded"/>)
/// and that the file is copied to a backup before any migration is attempted.<br/>
/// The same invariant reaches past a single load, so this also covers what a configuration reports about its own schema
/// version afterwards, how loudly a refused save repeats itself, and which instances
/// <see cref="NoireConfigManager.GetConfig{T}"/> is allowed to cache. Each of those decides whether a later save is
/// aimed at the user's real settings or at defaults standing in for them.<br/>
/// The opposite failure is covered alongside it, a save that should happen and does not: the suppression that keeps the
/// member copy onto the auto-save wrapper from writing back what it has just read is scoped to the copying thread and
/// to the copy, so that neither a copy that throws nor a copy running at that moment can cost an unrelated setting its
/// write.<br/>
/// These tests drive the real <see cref="NoireConfigBase.Load"/> and <see cref="NoireConfigBase.Save"/> against real
/// files in a temporary directory. Each configuration under test overrides
/// <c>GetConfigFilePath()</c> to point at that directory, which is what keeps them independent of a running game.
/// </summary>
public class NoireConfigMigrationTests : IDisposable
{
    #region Fixtures

    /// <summary>
    /// A version 1 file holding a value the user set. Written verbatim so that any write by the library, which formats
    /// indented and adds the members absent here, changes the bytes on disk.
    /// </summary>
    private const string V1FileContent = "{\"Version\":1,\"Value\":\"user-set-value\"}";

    private readonly string tempDirectory;

    public NoireConfigMigrationTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "NoireLibConfigMigrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        // Migrations are registered in a process-wide table keyed by config type, so a test must not inherit whatever a
        // previous one registered.
        NoireConfigManager.ClearMigrations();
        ResetManagerState();
    }

    public void Dispose()
    {
        NoireConfigManager.ClearMigrations();
        ResetManagerState();

        try
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, true);
        }
        catch (IOException)
        {
            // A leftover temporary directory must not fail a test run.
        }
    }

    /// <summary>
    /// A configuration at schema version 2. <c>AddedInV2</c> stands for a member that only a migration can populate
    /// from a version 1 file, which is what makes a failed migration observable.
    /// </summary>
    private class TestConfigBase : NoireConfigBase
    {
        internal string? filePathOverride;

        public override int Version { get; set; } = 2;

        public override string GetConfigFileName() => "test-config.json";

        protected override string? GetConfigFilePath() => filePathOverride;

        public string Value { get; set; } = "value-default";

        public string AddedInV2 { get; set; } = "added-in-v2-default";
    }

    private sealed class ThrowingConfig : TestConfigBase;

    private sealed class EmptyResultConfig : TestConfigBase;

    private sealed class WorkingConfig : TestConfigBase;

    private sealed class NoMigrationsConfig : TestConfigBase;

    private sealed class ConcurrencyConfig : TestConfigBase;

    /// <summary>
    /// A configuration whose <see cref="NoireConfigBase.Save"/> can be made to throw rather than report false, which
    /// <see cref="NoireConfigBase.ForceSave"/> has to treat as a write that did not land.
    /// </summary>
    private sealed class SaveThrowsConfig : TestConfigBase
    {
        internal bool throwOnSave;

        public override bool Save()
        {
            // Save is virtual and resolves its path through another virtual member, so a derived configuration
            // throwing out of it is a state the base has to hold up under.
            if (throwOnSave)
                throw new InvalidOperationException("Simulated defect in a derived Save override.");

            return base.Save();
        }
    }

    /// <summary>
    /// A configuration reached the way plugins reach one, through <see cref="NoireConfigManager.GetConfig{T}"/>, which
    /// constructs the instance itself. The path it resolves is therefore held statically rather than per instance,
    /// since there is nowhere to hand an instance-level override in.
    /// </summary>
    private class ManagerProbeConfigBase : NoireConfigBase
    {
        internal static string? PathOverride;

        public override int Version { get; set; } = 1;

        public override string GetConfigFileName() => "manager-probe.json";

        protected override string? GetConfigFilePath() => PathOverride;

        public string Value { get; set; } = "value-default";
    }

    private sealed class CacheProbeConfig : ManagerProbeConfigBase;

    /// <summary>
    /// Sits at schema version 2 so that the version 1 fixture file drives it through a migration, and through the
    /// degraded state when that migration fails.
    /// </summary>
    private sealed class DegradedProbeConfig : ManagerProbeConfigBase
    {
        public override int Version { get; set; } = 2;

        public string AddedInV2 { get; set; } = "added-in-v2-default";
    }

    /// <summary>
    /// A configuration reached the way plugins reach one that auto-saves, through <see cref="NoireConfigBase{T}.Instance"/>,
    /// which wraps the loaded instance in the Castle proxy that turns an assignment into a save.<br/>
    /// Public, and nested in a public class, because the proxy is a generated subclass in another assembly: a type that
    /// assembly cannot see is not proxied at all, and the wrapper quietly falls back to the unproxied instance, which
    /// would leave the tests below passing for the wrong reason.<br/>
    /// <see cref="Save"/> is counted rather than performed, so what a test observes is whether auto-save fired rather
    /// than whether a file landed. The statics are per closed generic type, so each concrete configuration below gets
    /// its own, which is also how the singleton and the manager cache key what they hold.
    /// </summary>
    /// <typeparam name="T">The concrete configuration type.</typeparam>
    public abstract class AutoSaveProbeConfig<T> : NoireConfigBase<T> where T : AutoSaveProbeConfig<T>, new()
    {
        /// <summary>
        /// How many times auto-save has reached <see cref="Save"/> on this configuration type.
        /// </summary>
        public static int SaveCount;

        /// <summary>
        /// The path this configuration type resolves, or null to resolve none.
        /// </summary>
        public static string? PathOverride;

        public override int Version { get; set; } = 1;

        public override string GetConfigFileName() => $"{typeof(T).Name}.json";

        protected override string? GetConfigFilePath() => PathOverride;

        /// <summary>
        /// A member whose assignment must persist, which is what auto-save is for.
        /// </summary>
        [AutoSave]
        public virtual string Tripwire { get; set; } = "tripwire-default";

        /// <inheritdoc/>
        public override bool Save()
        {
            Interlocked.Increment(ref SaveCount);
            return true;
        }

        /// <summary>
        /// Returns the state this type keeps statically to what a test is entitled to assume it is.
        /// </summary>
        public static void ResetProbeState()
        {
            ClearCache();
            NoireConfigManager.UnloadConfig<T>();
            SaveCount = 0;
            PathOverride = null;
        }
    }

    public class SaveCountingConfig : AutoSaveProbeConfig<SaveCountingConfig>;

    /// <summary>
    /// A configuration reached the way plugins reach one that auto-saves, through <see cref="NoireConfigBase{T}.Instance"/>,
    /// and which performs a real save rather than counting one, so that what the version hardening in
    /// <see cref="NoireConfigBase.Save"/> writes is observable in a file.<br/>
    /// Public, and nested in a public class, for the same reason <see cref="AutoSaveProbeConfig{T}"/> is: the auto-save
    /// wrapper is a generated subclass built in another assembly, and a type that assembly cannot see is never wrapped
    /// at all, which would leave the tests below passing against a plain instance and proving nothing.
    /// </summary>
    public class VersionProbeConfig : NoireConfigBase<VersionProbeConfig>
    {
        /// <summary>
        /// The path this configuration resolves, or null to resolve none.
        /// </summary>
        public static string? PathOverride;

        /// <summary>
        /// The schema this class declares, and therefore the number its saves have to write whatever
        /// <see cref="NoireConfigBase.Version"/> has been assigned.
        /// </summary>
        public const int DeclaredVersion = 4;

        public override int Version { get; set; } = DeclaredVersion;

        public override string GetConfigFileName() => "version-probe.json";

        protected override string? GetConfigFilePath() => PathOverride;

        /// <summary>
        /// Present so that the configuration is wrapped at all: the wrapper is only built for a type that has a member
        /// marked <see cref="AutoSaveAttribute"/>.
        /// </summary>
        [AutoSave]
        public virtual string Tripwire { get; set; } = "tripwire-default";

        /// <summary>
        /// Exposes the protected default-version lookup so that a test can observe what it resolves rather than only
        /// what a save happens to write.<br/>
        /// Not virtual, so the wrapper does not intercept it and the call runs against the wrapper itself, which is the
        /// instance whose reported type is not the configuration type.
        /// </summary>
        /// <returns>The version the lookup resolves.</returns>
        public int ProbeDefaultVersion() => GetDefaultVersion();

        /// <summary>
        /// Returns the state this type keeps statically to what a test is entitled to assume it is.
        /// </summary>
        public static void ResetProbeState()
        {
            ClearCache();
            NoireConfigManager.UnloadConfig<VersionProbeConfig>();
            PathOverride = null;
        }
    }

    /// <summary>
    /// A configuration whose member copy onto the auto-save wrapper can be made to fail, which is the case the
    /// suppression the copy turns on has to survive.
    /// </summary>
    public class ThrowOnCopyConfig : AutoSaveProbeConfig<ThrowOnCopyConfig>
    {
        /// <summary>
        /// Whether <see cref="Exploding"/> refuses assignment, which is what makes the copy throw.
        /// </summary>
        public static bool ThrowOnSet;

        private string exploding = "exploding-default";

        /// <summary>
        /// Stands in for anything a derived configuration can do in a property setter that the member copy runs but
        /// does not control, a type mismatch or a validating setter among them.
        /// </summary>
        [AutoSave]
        public virtual string Exploding
        {
            get => exploding;
            set
            {
                if (ThrowOnSet)
                    throw new InvalidOperationException("Simulated defect in a configuration property setter.");

                exploding = value;
            }
        }
    }

    private sealed class CountingMigration : IConfigMigration
    {
        public CountingMigration(int fromVersion)
        {
            FromVersion = fromVersion;
        }

        public int FromVersion { get; }

        public int ToVersion => FromVersion + 1;

        public string Migrate(JObject jsonObject) => jsonObject.ToString();
    }

    /// <summary>
    /// Returns the process-wide state the manager keeps to what a test is entitled to assume it is, since the cache is
    /// keyed by type and shared by every test that reaches the same configuration type.
    /// </summary>
    private static void ResetManagerState()
    {
        NoireConfigManager.UnloadConfig<CacheProbeConfig>();
        NoireConfigManager.UnloadConfig<DegradedProbeConfig>();
        ManagerProbeConfigBase.PathOverride = null;

        // The auto-save probes are reached through the singleton, which caches the wrapper statically per type on top
        // of the manager's own cache, so both have to be dropped for a test to get a configuration it can predict.
        SaveCountingConfig.ResetProbeState();
        ThrowOnCopyConfig.ResetProbeState();
        ThrowOnCopyConfig.ThrowOnSet = false;
        VersionProbeConfig.ResetProbeState();
    }

    private sealed class ThrowingMigration : IConfigMigration
    {
        public int FromVersion => 1;

        public int ToVersion => 2;

        public string Migrate(JObject jsonObject) => throw new InvalidOperationException("Simulated defect in a migration.");
    }

    private sealed class EmptyResultMigration : IConfigMigration
    {
        public int FromVersion => 1;

        public int ToVersion => 2;

        public string Migrate(JObject jsonObject) => string.Empty;
    }

    private sealed class WorkingMigration : IConfigMigration
    {
        public int FromVersion => 1;

        public int ToVersion => 2;

        public string Migrate(JObject jsonObject)
        {
            jsonObject["AddedInV2"] = "produced-by-migration";
            jsonObject["Version"] = 2;
            return jsonObject.ToString();
        }
    }

    private string WriteConfigFile(string fileName, string content)
    {
        var path = Path.Combine(tempDirectory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private T NewConfigAt<T>(string path) where T : TestConfigBase, new()
        => new() { filePathOverride = path };

    private static string BackupPathFor(string configPath, int fileVersion) => $"{configPath}.v{fileVersion}.bak";

    #endregion

    #region The file survives a failed migration

    [Fact]
    public void Load_MigrationThrows_LeavesFileByteIdentical()
    {
        // The core regression: a migration that throws must not cost the user their configuration.
        var path = WriteConfigFile("throwing.json", V1FileContent);
        var before = File.ReadAllBytes(path);

        NoireConfigManager.RegisterMigration<ThrowingConfig>(new ThrowingMigration());

        var config = NewConfigAt<ThrowingConfig>(path);
        config.Load();

        File.ReadAllBytes(path).Should().Equal(before, "a migration that throws must never write to the configuration file");
    }

    [Fact]
    public void Load_MigrationThrows_MarksInstanceDegraded()
    {
        var path = WriteConfigFile("throwing.json", V1FileContent);
        NoireConfigManager.RegisterMigration<ThrowingConfig>(new ThrowingMigration());

        var config = NewConfigAt<ThrowingConfig>(path);
        config.Load();

        config.IsDegraded.Should().BeTrue("the instance was populated from un-migrated JSON and is only a partial view of the user's settings");

        // The load itself does not fail loudly: the un-migrated file deserializes fine, which is exactly why the latch
        // has to exist rather than relying on an exception reaching the caller.
        config.Value.Should().Be("user-set-value", "members present in the old file still load");
        config.AddedInV2.Should().Be("added-in-v2-default", "the member the migration was meant to produce stays at its default, which is the silent data loss being guarded");
    }

    [Fact]
    public void Save_WhileDegraded_RefusesAndLeavesFileByteIdentical()
    {
        // Modules save during startup for their own reasons. Before the latch, that ordinary save is what turned a
        // failed migration into permanent loss by writing the partially defaulted instance over the good file.
        var path = WriteConfigFile("throwing.json", V1FileContent);
        var before = File.ReadAllBytes(path);

        NoireConfigManager.RegisterMigration<ThrowingConfig>(new ThrowingMigration());

        var config = NewConfigAt<ThrowingConfig>(path);
        config.Load();
        config.IsDegraded.Should().BeTrue();

        config.Value = "written-by-a-module-during-startup";
        config.Save().Should().BeFalse("a degraded instance must refuse to persist");

        File.ReadAllBytes(path).Should().Equal(before, "the refused save must not have touched the file");
    }

    [Fact]
    public void Load_MigrationReturnsEmptyJson_MarksInstanceDegraded()
    {
        // A migration that returns nothing fails the same way one that throws does.
        var path = WriteConfigFile("empty.json", V1FileContent);
        var before = File.ReadAllBytes(path);

        NoireConfigManager.RegisterMigration<EmptyResultConfig>(new EmptyResultMigration());

        var config = NewConfigAt<EmptyResultConfig>(path);
        config.Load();

        config.IsDegraded.Should().BeTrue();
        File.ReadAllBytes(path).Should().Equal(before);
    }

    [Fact]
    public void Load_NoMigrationPathRegistered_MarksInstanceDegraded()
    {
        // An old file with no migration registered for it reaches the same place: values the current schema expects are
        // simply absent, so the instance must not be allowed to persist over the file.
        var path = WriteConfigFile("nopath.json", V1FileContent);
        var before = File.ReadAllBytes(path);

        var config = NewConfigAt<NoMigrationsConfig>(path);
        config.Load();

        config.IsDegraded.Should().BeTrue();
        config.Save().Should().BeFalse();
        File.ReadAllBytes(path).Should().Equal(before);
    }

    [Fact]
    public void DegradedState_SurvivesMemberCopyOntoAnotherInstance()
    {
        // A configuration reached through NoireConfigBase<T>.Instance is copied member by member onto an auto-save
        // wrapper, and that copy reflects over the concrete type, which cannot see private fields declared on the base
        // class. If the latch stops surviving this copy, the instance consumers actually hold comes up undegraded and
        // saves freely, which is the whole defect again.
        var path = WriteConfigFile("throwing.json", V1FileContent);
        NoireConfigManager.RegisterMigration<ThrowingConfig>(new ThrowingMigration());

        var loaded = NewConfigAt<ThrowingConfig>(path);
        loaded.Load();
        loaded.IsDegraded.Should().BeTrue();

        var copy = new ThrowingConfig();
        loaded.CopyMembersTo(copy);

        copy.IsDegraded.Should().BeTrue("the latch must travel with the values it protects");
        copy.DegradedBackupPath.Should().Be(loaded.DegradedBackupPath);
        copy.Save().Should().BeFalse();
    }

    #endregion

    #region Backups

    [Fact]
    public void Load_MigrationThrows_WritesBackupEqualToOriginal()
    {
        var path = WriteConfigFile("throwing.json", V1FileContent);
        NoireConfigManager.RegisterMigration<ThrowingConfig>(new ThrowingMigration());

        var config = NewConfigAt<ThrowingConfig>(path);
        config.Load();

        var backupPath = BackupPathFor(path, 1);
        File.Exists(backupPath).Should().BeTrue("the file is copied before the migration is attempted");
        File.ReadAllText(backupPath).Should().Be(V1FileContent, "the backup is the user's file exactly as it was");

        config.DegradedBackupPath.Should().Be(backupPath, "a degraded instance must be able to tell the user where their configuration went");
    }

    [Fact]
    public void Load_RepeatedFailedMigration_DoesNotClobberTheExistingBackup()
    {
        // The backup is only worth having if a later attempt cannot overwrite it with something worse. By the second
        // start the file may already have been replaced by a forced write of degraded values.
        var path = WriteConfigFile("throwing.json", V1FileContent);
        NoireConfigManager.RegisterMigration<ThrowingConfig>(new ThrowingMigration());

        NewConfigAt<ThrowingConfig>(path).Load();

        var backupPath = BackupPathFor(path, 1);
        File.ReadAllText(backupPath).Should().Be(V1FileContent);

        // A second start, against a file whose contents have since been damaged but which is still at version 1.
        File.WriteAllText(path, "{\"Version\":1,\"Value\":\"damaged\"}");
        NewConfigAt<ThrowingConfig>(path).Load();

        File.ReadAllText(backupPath).Should().Be(V1FileContent, "the earliest backup taken at this schema version is the trustworthy one and must be kept");
    }

    [Fact]
    public void Load_BackupIsNamedForTheVersionItWasTakenAt()
    {
        // The name carries the source version rather than a timestamp, so retrying a failing migration on every start
        // keeps exactly one backup per schema version instead of accumulating copies.
        var path = WriteConfigFile("throwing.json", V1FileContent);
        NoireConfigManager.RegisterMigration<ThrowingConfig>(new ThrowingMigration());

        NewConfigAt<ThrowingConfig>(path).Load();
        NewConfigAt<ThrowingConfig>(path).Load();
        NewConfigAt<ThrowingConfig>(path).Load();

        Directory.GetFiles(tempDirectory, "*.bak").Should().ContainSingle().Which.Should().Be(BackupPathFor(path, 1));
    }

    #endregion

    #region The happy path still works

    [Fact]
    public void Load_SuccessfulMigration_AppliesMigrationAndSavesToDisk()
    {
        var path = WriteConfigFile("working.json", V1FileContent);
        NoireConfigManager.RegisterMigration<WorkingConfig>(new WorkingMigration());

        var config = NewConfigAt<WorkingConfig>(path);
        config.Load().Should().BeTrue();

        config.IsDegraded.Should().BeFalse("a migration that worked leaves nothing to protect against");
        config.DegradedBackupPath.Should().BeNull();
        config.Value.Should().Be("user-set-value");
        config.AddedInV2.Should().Be("produced-by-migration");

        // The migrated configuration is written back, which is the behavior that must not regress.
        var onDisk = JObject.Parse(File.ReadAllText(path));
        onDisk["Version"]!.Value<int>().Should().Be(2);
        onDisk["Value"]!.Value<string>().Should().Be("user-set-value");
        onDisk["AddedInV2"]!.Value<string>().Should().Be("produced-by-migration");
    }

    [Fact]
    public void Load_SuccessfulMigration_StillBacksUpTheOriginal()
    {
        // The backup is taken before the migration runs, so it exists whatever the outcome. A migration that succeeds
        // but produces the wrong values is recoverable for the same reason one that throws is.
        var path = WriteConfigFile("working.json", V1FileContent);
        NoireConfigManager.RegisterMigration<WorkingConfig>(new WorkingMigration());

        NewConfigAt<WorkingConfig>(path).Load();

        File.ReadAllText(BackupPathFor(path, 1)).Should().Be(V1FileContent);
    }

    [Fact]
    public void Load_FileAlreadyAtCurrentVersion_IsNotDegradedAndTakesNoBackup()
    {
        var path = WriteConfigFile("current.json", "{\"Version\":2,\"Value\":\"v\",\"AddedInV2\":\"a\"}");

        var config = NewConfigAt<NoMigrationsConfig>(path);
        config.Load().Should().BeTrue();

        config.IsDegraded.Should().BeFalse();
        config.Value.Should().Be("v");
        Directory.GetFiles(tempDirectory, "*.bak").Should().BeEmpty("no migration was attempted, so there was nothing to protect");
    }

    [Fact]
    public void Save_OnAHealthyConfig_WritesToDisk()
    {
        var path = Path.Combine(tempDirectory, "healthy.json");

        var config = NewConfigAt<NoMigrationsConfig>(path);
        config.Value = "set-by-plugin";
        config.Save().Should().BeTrue();

        JObject.Parse(File.ReadAllText(path))["Value"]!.Value<string>().Should().Be("set-by-plugin");
    }

    [Fact]
    public void Load_SuccessfulLoad_ClearsAStaleDegradedLatch()
    {
        // The latch reports the outcome of the most recent load, so an instance reloaded from a file that no longer
        // needs migrating must be usable again.
        var path = WriteConfigFile("throwing.json", V1FileContent);
        NoireConfigManager.RegisterMigration<ThrowingConfig>(new ThrowingMigration());

        var config = NewConfigAt<ThrowingConfig>(path);
        config.Load();
        config.IsDegraded.Should().BeTrue();

        File.WriteAllText(path, "{\"Version\":2,\"Value\":\"repaired\",\"AddedInV2\":\"repaired\"}");

        config.Load().Should().BeTrue();

        config.IsDegraded.Should().BeFalse();
        config.DegradedBackupPath.Should().BeNull();
        config.Save().Should().BeTrue();
    }

    #endregion

    #region Escape hatches

    [Fact]
    public void ForceSave_WhileDegraded_WritesAnywayAndClearsTheLatch()
    {
        var path = WriteConfigFile("throwing.json", V1FileContent);
        NoireConfigManager.RegisterMigration<ThrowingConfig>(new ThrowingMigration());

        var config = NewConfigAt<ThrowingConfig>(path);
        config.Load();
        config.IsDegraded.Should().BeTrue();

        config.ForceSave().Should().BeTrue("forcing is the documented way for an owner to persist a degraded instance");

        var onDisk = JObject.Parse(File.ReadAllText(path));
        onDisk["Version"]!.Value<int>().Should().Be(2);
        onDisk["Value"]!.Value<string>().Should().Be("user-set-value");

        config.IsDegraded.Should().BeFalse("the file on disk now matches the instance, so there is nothing left to protect");
        config.DegradedBackupPath.Should().BeNull();
        config.Save().Should().BeTrue("ordinary saving works again once the state is resolved");
    }

    [Fact]
    public void ForceSave_ThatFails_KeepsTheLatch()
    {
        // If the forced write does not land, the file on disk is still the one worth protecting.
        var path = WriteConfigFile("throwing.json", V1FileContent);
        NoireConfigManager.RegisterMigration<ThrowingConfig>(new ThrowingMigration());

        var config = NewConfigAt<ThrowingConfig>(path);
        config.Load();

        var backupPath = config.DegradedBackupPath;
        config.IsDegraded.Should().BeTrue();

        // A configuration that cannot resolve a path cannot be written.
        config.filePathOverride = null;
        config.ForceSave().Should().BeFalse();

        config.IsDegraded.Should().BeTrue("a forced save that failed must not retire the protection");
        config.DegradedBackupPath.Should().Be(backupPath);
    }

    [Fact]
    public void ForceSave_WhenSaveThrows_KeepsTheLatchAndLeavesTheFileByteIdentical()
    {
        // Forcing clears the protection before delegating, so that a derived Save override runs against a consistent
        // state. A write that reports false puts it back; a write that throws has landed just as little and has to put
        // it back too. Otherwise one forced save that faulted retires the protection permanently, and the next ordinary
        // save, which the instance no longer has any reason to refuse, writes the partially defaulted values over the
        // user's file.
        var path = WriteConfigFile("throwing.json", V1FileContent);
        var before = File.ReadAllBytes(path);

        NoireConfigManager.RegisterMigration<SaveThrowsConfig>(new ThrowingMigration());

        var config = NewConfigAt<SaveThrowsConfig>(path);
        config.Load();
        config.IsDegraded.Should().BeTrue();

        var backupPath = config.DegradedBackupPath;

        config.throwOnSave = true;
        var force = () => config.ForceSave();
        force.Should().Throw<InvalidOperationException>("the exception belongs to the caller and must not be swallowed");

        config.IsDegraded.Should().BeTrue("a forced save that faulted wrote nothing, so the file on disk is still the one worth protecting");
        config.DegradedBackupPath.Should().Be(backupPath, "the instance must still be able to tell the user where their configuration went");

        config.throwOnSave = false;
        config.Save().Should().BeFalse("the protection is still in force");
        File.ReadAllBytes(path).Should().Equal(before);
    }

    [Fact]
    public void ForceSave_OnAHealthyConfig_BehavesLikeSave()
    {
        var path = Path.Combine(tempDirectory, "healthy.json");

        var config = NewConfigAt<NoMigrationsConfig>(path);
        config.Value = "forced";
        config.ForceSave().Should().BeTrue();

        config.IsDegraded.Should().BeFalse();
        JObject.Parse(File.ReadAllText(path))["Value"]!.Value<string>().Should().Be("forced");
    }

    [Fact]
    public void ClearDegradedState_AllowsSavingAgainWithoutWriting()
    {
        var path = WriteConfigFile("throwing.json", V1FileContent);
        var before = File.ReadAllBytes(path);

        NoireConfigManager.RegisterMigration<ThrowingConfig>(new ThrowingMigration());

        var config = NewConfigAt<ThrowingConfig>(path);
        config.Load();
        config.IsDegraded.Should().BeTrue();

        config.ClearDegradedState();

        config.IsDegraded.Should().BeFalse();
        config.DegradedBackupPath.Should().BeNull();
        File.ReadAllBytes(path).Should().Equal(before, "clearing the state must not itself write anything");

        // The repair the caller is asserting it has made.
        config.AddedInV2 = "repaired-by-plugin";
        config.Save().Should().BeTrue();
        JObject.Parse(File.ReadAllText(path))["AddedInV2"]!.Value<string>().Should().Be("repaired-by-plugin");
    }

    #endregion

    #region The reported schema version

    [Fact]
    public void Load_TwiceOnAFileThatCannotBeMigrated_DoesNotSkipTheMigrationTheSecondTime()
    {
        // A load copies every public property out of the file, and Version is one of them. If the instance is left
        // reporting the file's version, it is reporting 1 while targeting 2, so this second load compares the file's 1
        // against that 1, concludes there is nothing to migrate, attempts none, and clears the degraded latch. The
        // instance then looks healthy while still holding the values the failed migration left defaulted, and the next
        // save writes them over the user's file. Instances are long-lived, so the second load is an ordinary event.
        var path = WriteConfigFile("throwing.json", V1FileContent);
        var before = File.ReadAllBytes(path);

        NoireConfigManager.RegisterMigration<ThrowingConfig>(new ThrowingMigration());

        var config = NewConfigAt<ThrowingConfig>(path);
        config.Load();
        config.IsDegraded.Should().BeTrue();

        config.Load();

        config.IsDegraded.Should().BeTrue("the file is still at version 1 and migrating it still fails, so a second load must reach the same verdict as the first");
        config.Save().Should().BeFalse("the instance is still a partial view of the user's settings");
        File.ReadAllBytes(path).Should().Equal(before, "no number of loads of a file that cannot be migrated may end with that file overwritten");
    }

    [Fact]
    public void Load_ReportsTheTargetSchemaVersionRatherThanTheFileVersion()
    {
        // The version an instance reports is what the next load measures the file against, so it has to be the schema
        // this build targets rather than whatever the file happened to be written at.
        var path = WriteConfigFile("nopath.json", V1FileContent);

        var config = NewConfigAt<NoMigrationsConfig>(path);
        config.Load();

        config.Version.Should().Be(2, "the instance targets schema 2 even though it just read a file at schema 1");
    }

    [Fact]
    public void Load_AfterASuccessfulMigration_ReportsTheTargetSchemaVersion()
    {
        var path = WriteConfigFile("working.json", V1FileContent);
        NoireConfigManager.RegisterMigration<WorkingConfig>(new WorkingMigration());

        var config = NewConfigAt<WorkingConfig>(path);
        config.Load().Should().BeTrue();

        config.Version.Should().Be(2, "the values have been migrated up to schema 2, which is what the instance now holds");
    }

    [Fact]
    public void Load_OfAFileAlreadyAtTheTargetVersion_LeavesTheVersionAlone()
    {
        var path = WriteConfigFile("current.json", "{\"Version\":2,\"Value\":\"v\",\"AddedInV2\":\"a\"}");

        var config = NewConfigAt<NoMigrationsConfig>(path);
        config.Load().Should().BeTrue();

        config.Version.Should().Be(2);
    }

    #endregion

    #region The volume of a refused save

    [Fact]
    public void Save_RepeatedlyWhileDegraded_ExplainsItselfOnceRatherThanOnEveryCall()
    {
        // Members marked [AutoSave] save on every assignment, so the refusal is not a one-off event: it repeats for as
        // long as the plugin keeps assigning to one of them. The first refusal has to carry the explanation and the
        // backup path, because that is how the user learns their configuration did not survive its migration; the ones
        // after it are the same event and must not each cost an error line.
        var path = WriteConfigFile("throwing.json", V1FileContent);
        var before = File.ReadAllBytes(path);

        NoireConfigManager.RegisterMigration<ThrowingConfig>(new ThrowingMigration());

        var config = NewConfigAt<ThrowingConfig>(path);
        config.Load();
        config.IsDegraded.Should().BeTrue();
        config.HasLoggedDegradedSaveRefusal.Should().BeFalse("nothing has tried to save yet");

        config.Save().Should().BeFalse();
        config.HasLoggedDegradedSaveRefusal.Should().BeTrue("the first refusal is the one that has to be visible");

        for (var i = 0; i < 5; i++)
            config.Save().Should().BeFalse("only the volume of the report is reduced, never the refusal itself");

        File.ReadAllBytes(path).Should().Equal(before);
    }

    [Fact]
    public void Save_AfterTheDegradedStateIsEnteredAgain_ExplainsItselfAgain()
    {
        // The explanation is owed once per degraded state rather than once per instance, so a configuration that is
        // declared repaired and then degrades again still announces the second one.
        var path = WriteConfigFile("throwing.json", V1FileContent);
        NoireConfigManager.RegisterMigration<ThrowingConfig>(new ThrowingMigration());

        var config = NewConfigAt<ThrowingConfig>(path);
        config.Load();
        config.Save().Should().BeFalse();
        config.HasLoggedDegradedSaveRefusal.Should().BeTrue();

        config.ClearDegradedState();
        config.HasLoggedDegradedSaveRefusal.Should().BeFalse("clearing the state ends the episode the explanation belonged to");

        config.Load();
        config.IsDegraded.Should().BeTrue("the file still cannot be migrated");
        config.HasLoggedDegradedSaveRefusal.Should().BeFalse("a freshly decided degraded state owes a fresh explanation");

        config.Save().Should().BeFalse();
        config.HasLoggedDegradedSaveRefusal.Should().BeTrue();
    }

    #endregion

    #region Runtime migration registration

    [Fact]
    public void RegisterMigration_FromManyThreadsAtOnce_KeepsEveryRegistration()
    {
        // Registration is a public entry point onto one process-wide table, and NoireLib is loaded next to unrelated
        // plugins that each register their own migrations as they initialize. Two of them doing so at the same moment
        // must not cost a registration or corrupt the table.
        const int registrationCount = 256;

        Parallel.For(0, registrationCount, index =>
            NoireConfigManager.RegisterMigration<ConcurrencyConfig>(new CountingMigration(index)));

        MigrationExecutor.GetRuntimeMigrations(typeof(ConcurrencyConfig))
            .Should().HaveCount(registrationCount, "every registration that was accepted must still be in the table");
    }

    [Fact]
    public void RegisterMigration_WhileAnotherThreadReadsTheMigrations_NeverFaultsTheRead()
    {
        // The read side of this table is a configuration load, which happens on whichever thread the plugin initializes
        // on. A registration landing at that moment must not fault the load with a collection-modified failure.
        using var cancellation = new CancellationTokenSource();

        var reader = Task.Run(() =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                foreach (var migration in MigrationExecutor.GetRuntimeMigrations(typeof(ConcurrencyConfig)))
                    _ = migration.FromVersion;
            }
        });

        for (var index = 0; index < 512; index++)
            NoireConfigManager.RegisterMigration<ConcurrencyConfig>(new CountingMigration(index));

        cancellation.Cancel();

        var awaitReader = () => reader.GetAwaiter().GetResult();
        awaitReader.Should().NotThrow("a configuration load must not fail because another plugin registered a migration while it ran");
    }

    [Fact]
    public void ClearRuntimeMigrations_EmptiesTheTable()
    {
        NoireConfigManager.RegisterMigration<ConcurrencyConfig>(new CountingMigration(1));
        MigrationExecutor.GetRuntimeMigrations(typeof(ConcurrencyConfig)).Should().NotBeEmpty();

        NoireConfigManager.ClearMigrations();

        MigrationExecutor.GetRuntimeMigrations(typeof(ConcurrencyConfig)).Should().BeEmpty();
    }

    #endregion

    #region Auto-save around the copy onto the wrapper

    [Fact]
    public void Instance_CopyOntoTheAutoSaveWrapper_DoesNotPersistTheValuesItJustRead()
    {
        // The suppression exists for this: the copy assigns through the wrapper's intercepted setters, so every member
        // marked [AutoSave] would write back the file it was just read from, once per member.
        SaveCountingConfig.PathOverride = Path.Combine(tempDirectory, "save-counting.json");

        var config = SaveCountingConfig.Instance;

        config.Should().NotBeNull();
        SaveCountingConfig.SaveCount.Should().Be(0, "the copy carries the values that were just loaded, so writing them back is redundant");
    }

    [Fact]
    public void Instance_AssigningAnAutoSaveMember_Persists()
    {
        // The baseline the rest of this region measures against. It also catches the wrapper silently not being a
        // wrapper at all, which is what a configuration type the proxy generator cannot see would produce.
        SaveCountingConfig.PathOverride = Path.Combine(tempDirectory, "save-counting.json");

        var config = SaveCountingConfig.Instance;
        SaveCountingConfig.SaveCount = 0;

        config.Tripwire = "set-by-plugin";

        SaveCountingConfig.SaveCount.Should().Be(1, "assigning a member marked [AutoSave] is what persists it");
    }

    [Fact]
    public void Instance_WhenTheCopyThrows_DoesNotLeaveAutoSaveSuppressed()
    {
        // The copy reflects over the configuration's members and runs whatever a derived class does in a setter, so it
        // can throw. The suppression it turns on is consulted by every auto-save that follows, so one that outlives its
        // copy disables auto-save wholesale: settings apply in memory, never reach disk, and report no error.
        ThrowOnCopyConfig.PathOverride = Path.Combine(tempDirectory, "throw-on-copy.json");
        ThrowOnCopyConfig.ThrowOnSet = true;

        bool suppressedAfterTheThrow;

        try
        {
            var reachInstance = () => ThrowOnCopyConfig.Instance;
            reachInstance.Should().Throw<Exception>("the test only means anything if the copy actually failed");
        }
        finally
        {
            ThrowOnCopyConfig.ThrowOnSet = false;

            // Read and then cleared on the thread that ran the copy, which is the thread the flag belongs to. Clearing
            // it here rather than asserting first keeps a run against code that leaves it set from spoiling every test
            // that follows on this thread.
            suppressedAfterTheThrow = NoireConfigBase.IsInternalCopying;
            NoireConfigBase.IsInternalCopying = false;
        }

        suppressedAfterTheThrow.Should().BeFalse("a copy that threw must leave auto-save enabled");
    }

    [Fact]
    public void Instance_WhenTheCopyThrows_LeavesAutoSaveWorkingAfterwards()
    {
        // The observable half of the same defect, stated as what a user would notice: they change a setting, it does
        // not persist, and nothing says so.
        ThrowOnCopyConfig.PathOverride = Path.Combine(tempDirectory, "throw-on-copy.json");
        ThrowOnCopyConfig.ThrowOnSet = true;

        try
        {
            var reachInstance = () => ThrowOnCopyConfig.Instance;
            reachInstance.Should().Throw<Exception>();

            ThrowOnCopyConfig.ThrowOnSet = false;

            // The failed copy cached nothing, so this builds the wrapper again, this time to completion.
            var config = ThrowOnCopyConfig.Instance;
            ThrowOnCopyConfig.SaveCount = 0;

            config.Tripwire = "set-after-the-failed-copy";

            ThrowOnCopyConfig.SaveCount.Should().Be(1, "auto-save must survive a copy that failed");
        }
        finally
        {
            ThrowOnCopyConfig.ThrowOnSet = false;
            NoireConfigBase.IsInternalCopying = false;
        }
    }

    [Fact]
    public void AutoSave_OnTheThreadRunningACopy_IsSuppressed()
    {
        // The half of the semantics that must not be lost: the copy's own assignments do not persist.
        SaveCountingConfig.PathOverride = Path.Combine(tempDirectory, "same-thread.json");

        var config = SaveCountingConfig.Instance;
        SaveCountingConfig.SaveCount = 0;

        NoireConfigBase.IsInternalCopying = true;

        try
        {
            config.Tripwire = "assigned-by-a-copy";
        }
        finally
        {
            // Shared by every configuration reached from this thread, so a test that sets it restores it or corrupts
            // the ones that follow.
            NoireConfigBase.IsInternalCopying = false;
        }

        SaveCountingConfig.SaveCount.Should().Be(0, "an assignment made by the copy running on this thread must not persist");
    }

    [Fact]
    public void AutoSave_OnAnotherThreadWhileACopyRuns_StillPersists()
    {
        // The save a copy has to suppress is the one the copy itself raises, and that save is always on the copying
        // thread: the interceptor runs inline inside the assignment the copy makes, with no hop or queue between them.
        // Suppression shared by the whole process would therefore reach further than it has any reason to, as far as an
        // unrelated consumer assigning a setting on another thread at that moment, applying their change in memory and
        // dropping the write with nothing reported.
        SaveCountingConfig.PathOverride = Path.Combine(tempDirectory, "cross-thread.json");

        var config = SaveCountingConfig.Instance;
        SaveCountingConfig.SaveCount = 0;

        Exception? failure = null;

        NoireConfigBase.IsInternalCopying = true;

        try
        {
            // A thread of its own rather than a pool thread, so the assignment cannot land on the thread standing in
            // for the copy.
            var assigningThread = new Thread(() =>
            {
                try
                {
                    config.Tripwire = "set-by-a-user-on-another-thread";
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            assigningThread.Start();
            assigningThread.Join();
        }
        finally
        {
            NoireConfigBase.IsInternalCopying = false;
        }

        failure.Should().BeNull();
        SaveCountingConfig.SaveCount.Should().Be(1, "a change made on another thread is a real change and must be persisted however busy this one is");
    }

    #endregion

    #region What the manager caches

    [Fact]
    public void GetConfig_WhenNoFileExistsYet_CachesTheDefaultInstance()
    {
        // A fresh install has no file, which the load reports the same way it reports a failure. The defaults are the
        // real configuration until something saves them, so the instance holding them is shared like any other and a
        // value set on it has to be visible to the next caller.
        ManagerProbeConfigBase.PathOverride = Path.Combine(tempDirectory, "absent.json");

        var first = NoireConfigManager.GetConfig<CacheProbeConfig>();
        var second = NoireConfigManager.GetConfig<CacheProbeConfig>();

        first.Should().NotBeNull("a plugin that has never saved must still get a usable configuration");
        second.Should().BeSameAs(first, "there is nothing wrong with this instance, so every caller shares it");
    }

    [Fact]
    public void GetConfig_WhenTheFileCannotBeParsed_DoesNotCacheTheFailureAndRecoversLater()
    {
        // Caching an instance whose load failed against a file that is right there memoises the failure: every later
        // caller is handed defaults, the file is never read again, and the first save writes those defaults over it.
        var path = Path.Combine(tempDirectory, "unparseable.json");
        ManagerProbeConfigBase.PathOverride = path;
        File.WriteAllText(path, "{\"Version\":1,\"Value\":");

        var first = NoireConfigManager.GetConfig<CacheProbeConfig>();
        var second = NoireConfigManager.GetConfig<CacheProbeConfig>();

        first.Should().NotBeNull("the caller still needs something to work with");
        second.Should().NotBeSameAs(first, "a failed load must not be memoised, or the file would never be read again");

        File.WriteAllText(path, V1FileContent);

        var repaired = NoireConfigManager.GetConfig<CacheProbeConfig>();
        repaired!.Value.Should().Be("user-set-value", "once the file can be read, the user's settings must be the ones served");
        NoireConfigManager.GetConfig<CacheProbeConfig>().Should().BeSameAs(repaired, "a load that worked is cached");
    }

    [Fact]
    public void GetConfig_WhenNoFilePathCanBeResolved_DoesNotCacheTheFailureAndRecoversLater()
    {
        // The state of a configuration reached before NoireLib is initialized. Caching it would let a single early
        // access shadow the user's real settings for the rest of the session.
        ManagerProbeConfigBase.PathOverride = null;

        var first = NoireConfigManager.GetConfig<CacheProbeConfig>();
        var second = NoireConfigManager.GetConfig<CacheProbeConfig>();

        first.Should().NotBeNull();
        second.Should().NotBeSameAs(first, "a configuration that could not resolve a path has not been loaded, only defaulted");

        var path = WriteConfigFile("resolved.json", V1FileContent);
        ManagerProbeConfigBase.PathOverride = path;

        NoireConfigManager.GetConfig<CacheProbeConfig>()!.Value.Should().Be("user-set-value",
            "once a path resolves, the user's settings must be picked up rather than shadowed by an early failure");
    }

    [Fact]
    public void GetConfig_WhenTheLoadIsDegraded_CachesTheLiveInstance()
    {
        // A degraded load succeeded, partially. The instance is the live one whose saves are being refused on purpose,
        // so dropping it from the cache would hand the next caller a second instance that re-runs the same failed
        // migration, and split the state the refusal depends on across two objects.
        var path = WriteConfigFile("degraded-probe.json", V1FileContent);
        ManagerProbeConfigBase.PathOverride = path;

        NoireConfigManager.RegisterMigration<DegradedProbeConfig>(new ThrowingMigration());

        var first = NoireConfigManager.GetConfig<DegradedProbeConfig>();

        first.Should().NotBeNull();
        first!.IsDegraded.Should().BeTrue();
        NoireConfigManager.GetConfig<DegradedProbeConfig>().Should().BeSameAs(first, "the degraded instance is the live configuration and must stay cached");
    }

    #endregion

    #region The version a save writes

    [Fact]
    public void GetDefaultVersion_OnTheAutoSaveWrapper_ReportsTheSchemaTheClassDeclares()
    {
        // A configuration with members marked [AutoSave] is handed to consumers as a generated subclass, so the type
        // the instance reports is that subclass and not the configuration whose declared schema is wanted. Reading the
        // declared version off the reported type would therefore not be reading it off the configuration at all.
        VersionProbeConfig.PathOverride = Path.Combine(tempDirectory, "version-probe.json");

        var config = VersionProbeConfig.Instance;

        config.GetType().Should().NotBe(typeof(VersionProbeConfig),
            "this only tests anything if the configuration is actually wrapped");
        config.ProbeDefaultVersion().Should().Be(VersionProbeConfig.DeclaredVersion);
    }

    [Fact]
    public void Save_OnTheAutoSaveWrapper_WritesTheSchemaTheClassDeclaresRatherThanAVersionAssignedOverIt()
    {
        // The number in the file records the schema the values in it are at, and it is what the next load measures the
        // file against. A version assigned over the property must not reach the file, or that load compares the file
        // against a number its contents do not match and takes, or skips, a migration on the strength of it.
        var path = Path.Combine(tempDirectory, "version-probe.json");
        VersionProbeConfig.PathOverride = path;

        var config = VersionProbeConfig.Instance;
        config.GetType().Should().NotBe(typeof(VersionProbeConfig), "this only tests anything if the configuration is actually wrapped");

        config.Version = 99;
        config.Save().Should().BeTrue();

        JObject.Parse(File.ReadAllText(path))["Version"]!.Value<int>()
            .Should().Be(VersionProbeConfig.DeclaredVersion, "the file records the schema this build defines, not whatever was assigned over the property");
        config.Version.Should().Be(VersionProbeConfig.DeclaredVersion, "the instance reports the schema it targets once the save has settled it");
    }

    [Fact]
    public void Save_OnAnUnwrappedConfiguration_WritesTheSchemaTheClassDeclaresRatherThanAVersionAssignedOverIt()
    {
        // The same guarantee for a configuration with no [AutoSave] members, which is never wrapped and reaches the
        // lookup with its own type. Both shapes must label the file the same way.
        var path = Path.Combine(tempDirectory, "unwrapped-version.json");

        var config = NewConfigAt<NoMigrationsConfig>(path);
        config.Value = "value";
        config.Version = 99;

        config.Save().Should().BeTrue();

        JObject.Parse(File.ReadAllText(path))["Version"]!.Value<int>().Should().Be(2);
        config.Version.Should().Be(2);
    }

    #endregion
}

/// <summary>
/// Groups every test that walks the whole configuration cache rather than only its own entry in it.<br/>
/// The cache is process-wide and holds one instance per configuration type, and unrelated test classes seed it with
/// configurations pointing at their own temporary files so that the real save path can run without a plugin
/// configuration directory. A walk reaches every one of those, so it must not run beside the classes that own them.
/// Parallelization is disabled for the whole collection to guarantee that, and any future test class that walks or
/// empties the cache must join this collection.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConfigCacheWalkCollection
{
    /// <summary>
    /// The collection name shared by the definition and its member classes.
    /// </summary>
    public const string Name = "NoireConfigManager cache walk";
}

/// <summary>
/// Game-free tests for saving every cached configuration at once, locking the invariant that one configuration cannot
/// cost the others their write.<br/>
/// <see cref="NoireConfigBase.Save"/> is virtual and resolves its file path through another virtual member, so a
/// configuration can throw out of it rather than report false. The cache is walked on behalf of every configuration in
/// it, and those configurations are unrelated to each other, so one that throws must be contained where it is reached.
/// Otherwise it ends the walk and every configuration after it is silently never written, which loses settings
/// belonging to configurations that had nothing wrong with them.<br/>
/// A configuration refusing to save because it is <see cref="NoireConfigBase.IsDegraded"/> is the opposite case: the
/// refusal is the protection working, so the walk owes it only to leave its file alone and carry on.<br/>
/// These tests drive the real save against real files in a temporary directory. Each configuration under test overrides
/// <c>GetConfigFilePath()</c> to point at that directory, which is what keeps them independent of a running game.
/// </summary>
[Collection(ConfigCacheWalkCollection.Name)]
public class NoireConfigSaveAllCachedTests : IDisposable
{
    #region Fixtures

    /// <summary>
    /// A version 1 file holding a value the user set. The migration registered against it throws, which is what leaves
    /// the configuration that reads it degraded and therefore refusing to be written.
    /// </summary>
    private const string V1FileContent = "{\"Version\":1,\"Value\":\"user-set-value\"}";

    private readonly string tempDirectory;

    public NoireConfigSaveAllCachedTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "NoireLibConfigSaveAllCachedTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        NoireConfigManager.ClearMigrations();
        NoireConfigManager.ClearCache();
    }

    public void Dispose()
    {
        NoireConfigManager.ClearMigrations();

        // The configurations cached below point into the directory about to be deleted, so leaving them in a
        // process-wide cache would hand the next walk a set of configurations that cannot be written.
        NoireConfigManager.ClearCache();

        try
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, true);
        }
        catch (IOException)
        {
            // A leftover temporary directory must not fail a test run.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A configuration at schema version 2, resolving a path handed to it rather than a plugin configuration directory.
    /// </summary>
    private class WalkTestConfigBase : NoireConfigBase
    {
        internal string? filePathOverride;

        public override int Version { get; set; } = 2;

        public override string GetConfigFileName() => "walk-config.json";

        protected override string? GetConfigFilePath() => filePathOverride;

        public string Value { get; set; } = "value-default";
    }

    /// <summary>
    /// Stands in for a configuration whose own override of <see cref="NoireConfigBase.Save"/>, or whose file access,
    /// throws rather than reporting false.
    /// </summary>
    private sealed class WalkThrowingConfig : WalkTestConfigBase
    {
        public override bool Save()
            => throw new InvalidOperationException("Simulated defect in a derived Save override.");
    }

    private sealed class WalkBystanderOneConfig : WalkTestConfigBase;

    private sealed class WalkBystanderTwoConfig : WalkTestConfigBase;

    private sealed class WalkDegradedConfig : WalkTestConfigBase;

    private sealed class WalkFailingMigration : IConfigMigration
    {
        public int FromVersion => 1;

        public int ToVersion => 2;

        public string Migrate(JObject jsonObject) => throw new InvalidOperationException("Simulated defect in a migration.");
    }

    /// <summary>
    /// Puts an instance into the cache under its own type, which is the set the walk reads from.<br/>
    /// A load caches the instance it populated by itself, but with add-if-absent semantics, so filling the cache
    /// explicitly is what makes the instance a test is holding the one the walk actually reaches.
    /// </summary>
    /// <param name="config">The configuration to cache.</param>
    private static void CacheConfig(NoireConfigBase config)
        => NoireConfigManager.AddConfigToCache(config.GetType(), config)
            .Should().BeTrue("the cache is emptied for each test, so nothing else can be holding this type");

    private T NewConfigAt<T>(string fileName) where T : WalkTestConfigBase, new()
        => new() { filePathOverride = Path.Combine(tempDirectory, fileName) };

    #endregion

    [Fact]
    public void SaveAllCached_WhenOneConfigurationThrowsOutOfSave_StillSavesTheOthers()
    {
        var thrower = NewConfigAt<WalkThrowingConfig>("thrower.json");

        var first = NewConfigAt<WalkBystanderOneConfig>("first-bystander.json");
        first.Value = "first-bystander-value";

        var second = NewConfigAt<WalkBystanderTwoConfig>("second-bystander.json");
        second.Value = "second-bystander-value";

        CacheConfig(thrower);
        CacheConfig(first);
        CacheConfig(second);

        var result = false;
        var saveAll = () => result = NoireConfigManager.SaveAllCached();

        // The deterministic half of the gate. The cache is walked in no order a test can fix, so which configurations
        // an uncontained throw would strand varies between runs, but the throw reaching the caller does not.
        saveAll.Should().NotThrow("one configuration's defect is not the caller's to handle and must not abandon the rest");

        result.Should().BeFalse("a configuration that could not be written is not a configuration that was saved");

        JObject.Parse(File.ReadAllText(first.filePathOverride!))["Value"]!.Value<string>()
            .Should().Be("first-bystander-value", "an unrelated configuration must not lose its write because another one is defective");
        JObject.Parse(File.ReadAllText(second.filePathOverride!))["Value"]!.Value<string>()
            .Should().Be("second-bystander-value");
    }

    [Fact]
    public void SaveAllCached_WithADegradedConfigurationInTheSet_LeavesItsFileAloneAndSavesTheOthers()
    {
        var degradedPath = Path.Combine(tempDirectory, "degraded.json");
        File.WriteAllText(degradedPath, V1FileContent);
        var before = File.ReadAllBytes(degradedPath);

        NoireConfigManager.RegisterMigration<WalkDegradedConfig>(new WalkFailingMigration());

        var degraded = NewConfigAt<WalkDegradedConfig>("degraded.json");
        degraded.Load();
        degraded.IsDegraded.Should().BeTrue("the version 1 file could not be migrated, which is what the refusal protects");
        degraded.HasLoggedDegradedSaveRefusal.Should().BeFalse("nothing has tried to save it yet");

        var healthy = NewConfigAt<WalkBystanderOneConfig>("healthy.json");
        healthy.Value = "healthy-value";

        // Emptied after the load above, which caches the instance it populated, so that the set walked below is exactly
        // these two configurations.
        NoireConfigManager.ClearCache();
        CacheConfig(degraded);
        CacheConfig(healthy);

        NoireConfigManager.SaveAllCached()
            .Should().BeFalse("a configuration that was deliberately not written is still a configuration that is not on disk");

        File.ReadAllBytes(degradedPath).Should().Equal(before, "the walk must not write a configuration that is refusing to be written");
        JObject.Parse(File.ReadAllText(healthy.filePathOverride!))["Value"]!.Value<string>()
            .Should().Be("healthy-value", "a degraded configuration in the set must not cost an unrelated one its write");

        degraded.HasLoggedDegradedSaveRefusal.Should().BeTrue(
            "the refusal is reached and left to the configuration to explain once for the state it is in, rather than " +
            "reported as a fault by the walk on every pass");
    }

    [Fact]
    public void SaveAllCached_WhenEveryConfigurationSaves_ReportsSuccess()
    {
        // The baseline the rest of this class measures against: the boundary must not turn an ordinary walk into a
        // reported failure.
        var first = NewConfigAt<WalkBystanderOneConfig>("first.json");
        first.Value = "first-value";

        var second = NewConfigAt<WalkBystanderTwoConfig>("second.json");
        second.Value = "second-value";

        CacheConfig(first);
        CacheConfig(second);

        NoireConfigManager.SaveAllCached().Should().BeTrue("every cached configuration was written");

        JObject.Parse(File.ReadAllText(first.filePathOverride!))["Value"]!.Value<string>().Should().Be("first-value");
        JObject.Parse(File.ReadAllText(second.filePathOverride!))["Value"]!.Value<string>().Should().Be("second-value");
    }
}
