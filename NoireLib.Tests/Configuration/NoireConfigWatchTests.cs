using FluentAssertions;
using NoireLib.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the autosave watch: arming, capture of list, dictionary and nested changes, the re-arm on a
/// capturing check, the disarm on a clean one, the degraded skip, and the teardown sweep.<br/>
/// The checks are driven directly, as the framework pump drives them per tick.
/// </summary>
[Collection(ConfigStateCollection.Name)]
public sealed class NoireConfigWatchTests : IDisposable
{
    private readonly string tempDirectory;

    public NoireConfigWatchTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "NoireLibConfigWatchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        ResetState();
    }

    public void Dispose()
    {
        // Drain anything still armed so a leftover check cannot run inside another test class.
        NoireConfigWatch.RunChecks();
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
        NoireConfigManager.UnloadConfig<WatchProbeConfig>();
        WatchProbeConfig.PathOverride = null;
    }

    public sealed class WatchLeaf
    {
        public bool Locked { get; set; }
    }

    public sealed class WatchNested
    {
        public string Label { get; set; } = "label";

        public WatchLeaf Leaf { get; set; } = new();
    }

    /// <summary>
    /// Enrolled through the class-level attribute, so the whole graph is watched.
    /// </summary>
    [AutoSave]
    public sealed class WatchProbeConfig : NoireConfigBase
    {
        public static string? PathOverride;

        public override int Version { get; set; } = 1;

        public override string GetConfigFileName() => "watch-probe.json";

        protected override string? GetConfigFilePath() => PathOverride;

        public bool Flag { get; set; }

        public List<string> Tags { get; set; } = [];

        public Dictionary<uint, WatchNested> Zones { get; set; } = new();

        public WatchNested Ui { get; set; } = new();

        public void MakeDegraded() => degradedLoad = true;
    }

    private WatchProbeConfig CreateReadyProbe(string fileName)
    {
        WatchProbeConfig.PathOverride = Path.Combine(tempDirectory, fileName);

        var config = new WatchProbeConfig();
        config.CompleteLoadSetup(null);
        return config;
    }

    [Fact]
    public void AnAccessWithoutAChange_QueuesNothing()
    {
        var config = CreateReadyProbe("clean.json");

        config.MarkAccessed();
        NoireConfigWatch.RunChecks();

        config.HasPendingSave.Should().BeFalse("reading a config is not a change");
    }

    [Fact]
    public void AListMutationThroughAStashedReference_IsCapturedOnTheNextCheck()
    {
        var config = CreateReadyProbe("list.json");

        config.MarkAccessed();
        config.Tags.Add("limsa");

        NoireConfigWatch.RunChecks();

        config.HasPendingSave.Should().BeTrue("the list add changed the content fingerprint");
        config.FlushPendingSave().Should().BeTrue();
        File.ReadAllText(WatchProbeConfig.PathOverride!).Should().Contain("limsa");
    }

    [Fact]
    public void ADictionaryAndANestedNestedMutation_AreCaptured()
    {
        var config = CreateReadyProbe("deep.json");

        config.MarkAccessed();
        config.Zones[7] = new WatchNested { Label = "zone-seven" };
        config.Ui.Leaf.Locked = true;

        NoireConfigWatch.RunChecks();
        config.FlushPendingSave().Should().BeTrue();

        var text = File.ReadAllText(WatchProbeConfig.PathOverride!);
        text.Should().Contain("zone-seven");
        text.Should().Contain("\"Locked\": true");
    }

    [Fact]
    public void AMutationStreak_KeepsBeingCaught_UntilACleanCheckDisarms()
    {
        var config = CreateReadyProbe("streak.json");

        config.MarkAccessed();
        config.Tags.Add("first");
        NoireConfigWatch.RunChecks();

        // No access here: the capturing check re-armed the config itself.
        config.Tags.Add("second");
        NoireConfigWatch.RunChecks();
        config.FlushPendingSave().Should().BeTrue();
        File.ReadAllText(WatchProbeConfig.PathOverride!).Should().Contain("second");

        // This check comes back clean and disarms.
        NoireConfigWatch.RunChecks();
        config.Tags.Add("third");
        NoireConfigWatch.RunChecks();

        config.HasPendingSave.Should().BeFalse(
            "a clean check disarms the watch, and only a real access arms it again");
    }

    [Fact]
    public void RequestSave_RefreshesTheBaseline_SoTheNextCheckIsClean()
    {
        var config = CreateReadyProbe("baseline.json");

        config.Flag = true;
        config.RequestSave();

        config.RunAutoSaveCheck().Should().BeFalse("RequestSave already captured this state");
    }

    [Fact]
    public void ADegradedConfiguration_IsNeverCaptured()
    {
        var config = CreateReadyProbe("degraded.json");
        config.MakeDegraded();

        config.MarkAccessed();
        config.Tags.Add("must-not-write");
        NoireConfigWatch.RunChecks();

        config.HasPendingSave.Should().BeFalse("a degraded configuration refuses every save, the watch included");
    }

    [Fact]
    public void TheFinalSweep_CatchesASilentMutation_AndFlushesIt()
    {
        var config = CreateReadyProbe("teardown.json");

        // Mutated through a stashed reference, with no access afterwards.
        config.Ui.Label = "changed-before-unload";

        NoireConfigWatch.RunFinalSweep([config]);

        File.ReadAllText(WatchProbeConfig.PathOverride!).Should().Contain("changed-before-unload");
        config.HasPendingSave.Should().BeFalse("the sweep flushes what it captured");
    }
}
