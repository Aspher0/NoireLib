using FluentAssertions;
using Newtonsoft.Json.Linq;
using NoireLib.Configuration;
using NoireLib.Configuration.Migrations;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the save pipeline and the load path: queued writes coalesce on the newest payload, a
/// blocking save discards the queue, the unchanged-skip still recreates a deleted file, migrations take a backup,
/// the degraded latch holds, concurrent first loads share one instance, and a reload reads the disk.
/// </summary>
[Collection(ConfigStateCollection.Name)]
public sealed class NoireConfigSaveAndLoadTests : IDisposable
{
    private readonly string tempDirectory;

    public NoireConfigSaveAndLoadTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "NoireLibConfigSaveLoadTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        ResetState();
    }

    public void Dispose()
    {
        ResetState();

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

    private static void ResetState()
    {
        NoireConfigManager.UnloadConfig<PipeProbeConfig>();
        NoireConfigManager.UnloadConfig<CrtpProbeConfig>();
        NoireConfigManager.UnloadConfig<MigrationProbeConfig>();
        NoireConfigManager.UnloadConfig<DegradedProbeConfig>();
        NoireConfigManager.ClearMigrations();
        PipeProbeConfig.PathOverride = null;
        CrtpProbeConfig.PathOverride = null;
        MigrationProbeConfig.PathOverride = null;
        DegradedProbeConfig.PathOverride = null;
    }

    public sealed class PipeProbeConfig : NoireConfigBase
    {
        public static string? PathOverride;

        public override int Version { get; set; } = 1;

        public override string GetConfigFileName() => "pipe-probe.json";

        protected override string? GetConfigFilePath() => PathOverride;

        public string Value { get; set; } = "default";
    }

    public sealed class CrtpProbeConfig : NoireConfigBase<CrtpProbeConfig>
    {
        public static string? PathOverride;

        public override int Version { get; set; } = 1;

        public override string GetConfigFileName() => "crtp-probe.json";

        protected override string? GetConfigFilePath() => PathOverride;

        public string Value { get; set; } = "default";
    }

    public sealed class MigrationProbeConfig : NoireConfigBase
    {
        public static string? PathOverride;

        public override int Version { get; set; } = 2;

        public override string GetConfigFileName() => "migration-probe.json";

        protected override string? GetConfigFilePath() => PathOverride;

        public string NewName { get; set; } = "default";

        private sealed class RenameMigration : ConfigMigrationBase
        {
            public override int FromVersion => 1;

            public override int ToVersion => 2;

            public override string Migrate(JObject jsonObject)
            {
                return MigrationBuilder.Create()
                    .RenameProperty("OldName", "NewName")
                    .Migrate(jsonObject, ToVersion);
            }
        }
    }

    public sealed class DegradedProbeConfig : NoireConfigBase
    {
        public static string? PathOverride;

        public override int Version { get; set; } = 3;

        public override string GetConfigFileName() => "degraded-probe.json";

        protected override string? GetConfigFilePath() => PathOverride;

        public string Value { get; set; } = "default";
    }

    private string FileFor(string name) => Path.Combine(tempDirectory, name);

    [Fact]
    public void QueuedSaves_Coalesce_AndTheNewestPayloadWins()
    {
        var file = FileFor("newest-wins.json");
        PipeProbeConfig.PathOverride = file;

        var config = new PipeProbeConfig { Value = "first" };
        config.RequestSave();

        config.Value = "second";
        config.RequestSave();

        config.FlushPendingSave().Should().BeTrue();
        File.ReadAllText(file).Should().Contain("second").And.NotContain("first");
    }

    [Fact]
    public void ABlockingSave_DiscardsTheQueuedPayload()
    {
        var file = FileFor("discard.json");
        PipeProbeConfig.PathOverride = file;

        var config = new PipeProbeConfig { Value = "queued" };
        config.RequestSave();

        config.Value = "final";
        config.Save().Should().BeTrue();

        config.HasPendingSave.Should().BeFalse("the blocking save superseded the queue");
        File.ReadAllText(file).Should().Contain("final");
    }

    [Fact]
    public void SavingUnchangedContent_StillRecreatesADeletedFile()
    {
        var file = FileFor("recreate.json");
        PipeProbeConfig.PathOverride = file;

        var config = new PipeProbeConfig { Value = "kept" };
        config.Save().Should().BeTrue();

        File.Delete(file);

        config.Save().Should().BeTrue();
        File.Exists(file).Should().BeTrue("the in-memory unchanged-skip must notice the file is gone");
        File.ReadAllText(file).Should().Contain("kept");
    }

    [Fact]
    public void ASave_LeavesNoTemporaryBeside()
    {
        var file = FileFor("atomic.json");
        PipeProbeConfig.PathOverride = file;

        new PipeProbeConfig { Value = "written" }.Save().Should().BeTrue();

        File.Exists(file + ".tmp").Should().BeFalse("the temporary is renamed onto the target");
    }

    [Fact]
    public void ConcurrentFirstAccesses_ReceiveTheSameInstance()
    {
        var file = FileFor("identity.json");
        PipeProbeConfig.PathOverride = file;
        new PipeProbeConfig { Value = "on-disk" }.Save().Should().BeTrue();

        PipeProbeConfig? first = null;
        PipeProbeConfig? second = null;

        Parallel.Invoke(
            () => first = NoireConfigManager.GetConfig<PipeProbeConfig>(),
            () => second = NoireConfigManager.GetConfig<PipeProbeConfig>());

        first.Should().NotBeNull();
        first.Should().BeSameAs(second, "two racing first accesses must not split the identity across two loads");
        first!.Value.Should().Be("on-disk");
    }

    [Fact]
    public void TheCrtpInstance_AndTheManager_ShareOneIdentity()
    {
        CrtpProbeConfig.PathOverride = FileFor("crtp.json");

        var instance = CrtpProbeConfig.Instance;

        NoireConfigManager.GetConfig<CrtpProbeConfig>().Should().BeSameAs(instance);
    }

    [Fact]
    public void Reload_ReadsTheFileAgain()
    {
        var file = FileFor("reload.json");
        CrtpProbeConfig.PathOverride = file;

        CrtpProbeConfig.Instance.Value = "in-memory";
        CrtpProbeConfig.Instance.Save().Should().BeTrue();

        File.WriteAllText(file, File.ReadAllText(file).Replace("in-memory", "edited-on-disk"));

        CrtpProbeConfig.Reload();

        CrtpProbeConfig.Instance.Value.Should().Be("edited-on-disk",
            "a reload promises the disk state, not the in-memory values handed back through a warm cache");
    }

    [Fact]
    public void AFailedLoadAgainstAnExistingFile_IsNotCached_SoTheNextAccessRetries()
    {
        var file = FileFor("invalid.json");
        PipeProbeConfig.PathOverride = file;
        File.WriteAllText(file, "{ \"Version\": 1 } trailing garbage");

        var first = NoireConfigManager.GetConfig<PipeProbeConfig>();
        var second = NoireConfigManager.GetConfig<PipeProbeConfig>();

        first.Should().NotBeNull("the caller still gets a usable defaults instance");
        first.Should().NotBeSameAs(second, "a failed load must stay uncached so a later access can retry the file");
    }

    [Fact]
    public void AnOlderFile_IsBackedUpAndMigrated()
    {
        var file = FileFor("migrate.json");
        MigrationProbeConfig.PathOverride = file;
        File.WriteAllText(file, "{ \"Version\": 1, \"OldName\": \"carried\" }");

        var config = new MigrationProbeConfig();
        config.Load().Should().BeTrue();

        config.NewName.Should().Be("carried", "the rename migration carries the value across");
        config.IsDegraded.Should().BeFalse();
        File.Exists(file + ".v1.bak").Should().BeTrue("the pre-migration backup is written before anything runs");
        File.ReadAllText(file).Should().Contain("\"Version\": 2", "the migrated file is saved back");
    }

    [Fact]
    public void AnUnmigratableFile_LatchesDegraded_AndSaveRefusesUntilForced()
    {
        var file = FileFor("degraded.json");
        DegradedProbeConfig.PathOverride = file;
        File.WriteAllText(file, "{ \"Version\": 1, \"Value\": \"precious\" }");
        var originalText = File.ReadAllText(file);

        var config = new DegradedProbeConfig();
        config.Load().Should().BeTrue("the values still load; only persisting them is blocked");

        config.IsDegraded.Should().BeTrue();
        config.DegradedBackupPath.Should().NotBeNull();
        config.Save().Should().BeFalse("a degraded configuration must not overwrite the user's file");
        File.ReadAllText(file).Should().Be(originalText, "the refusal is what keeps the file intact");

        config.ForceSave().Should().BeTrue();
        config.IsDegraded.Should().BeFalse("a forced write that landed retires the protection");
    }
}
