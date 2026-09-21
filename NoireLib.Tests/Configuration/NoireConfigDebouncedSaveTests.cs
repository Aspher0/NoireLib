using FluentAssertions;
using Newtonsoft.Json.Linq;
using NoireLib.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the contract of the debounced save path. A member marked <see cref="AutoSaveAttribute"/> is assigned on
/// the framework thread and may serialize there without touching a file. One window of changes costs one write,
/// matching what <see cref="NoireConfigBase.Save"/> would produce, nothing sits unwritten indefinitely, and a
/// shutdown flush leaves nothing pending.
/// </summary>
public sealed class NoireConfigDebouncedSaveTests : IDisposable
{
    private readonly string tempDirectory;
    private readonly TimeSpan originalDebounce = NoireConfigBase.SaveDebounceInterval;
    private readonly TimeSpan originalMaxDelay = NoireConfigBase.MaxSaveDelay;

    public NoireConfigDebouncedSaveTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "NoireLibDebouncedSaveTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
    }

    public void Dispose()
    {
        NoireConfigBase.FlushAllPendingSaves();

        // Process-wide statics.
        NoireConfigBase.SaveDebounceInterval = originalDebounce;
        NoireConfigBase.MaxSaveDelay = originalMaxDelay;

        try
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, true);
        }
        catch (IOException)
        {
        }
    }

    // Serializing a collection off the owning thread is unsafe.
    private class ProbeConfig : NoireConfigBase
    {
        internal string? filePathOverride;

        public override int Version { get; set; } = 3;

        public override string GetConfigFileName() => "debounce-probe.json";

        protected override string? GetConfigFilePath() => filePathOverride;

        public bool Toggle { get; set; }

        public int Counter { get; set; }

        public List<uint> Favorites { get; set; } = [50, 95, 96];
    }

    private sealed class DegradedProbeConfig : ProbeConfig
    {
        public void MarkDegraded() => degradedLoad = true;
    }

    private ProbeConfig NewConfig(string fileName)
        => new() { filePathOverride = Path.Combine(tempDirectory, fileName) };

    private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();

        while (watch.Elapsed < timeout)
        {
            if (condition())
                return true;

            Thread.Sleep(10);
        }

        return condition();
    }

    private static string JsonValueOf(string path, string member)
        => JObject.Parse(File.ReadAllText(path))[member]!.ToString();

    // Catches an in-place rewrite too.
    private Dictionary<string, string> SnapshotDirectory()
        => Directory.GetFiles(tempDirectory, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(tempDirectory, path),
                path => Convert.ToHexString(File.ReadAllBytes(path)));

    #region A request does not touch the disk

    [Fact]
    public void RequestSave_DoesNotWriteTheFileOnTheCallingThread()
    {
        NoireConfigBase.SaveDebounceInterval = TimeSpan.FromSeconds(30);

        var config = NewConfig("no-disk-touch.json");
        config.Toggle = true;

        config.RequestSave();

        File.Exists(config.filePathOverride!).Should().BeFalse("a request marks the configuration changed, it does not write it");
        config.HasPendingSave.Should().BeTrue("the change is held for the write that follows");
    }

    [Fact]
    public void RequestSave_OverAFileThatIsAlreadyThere_LeavesItByteForByte()
    {
        NoireConfigBase.SaveDebounceInterval = TimeSpan.FromSeconds(30);

        var config = NewConfig("no-overwrite.json");
        config.Counter = 1;
        config.Save().Should().BeTrue();

        var before = SnapshotDirectory();

        config.Counter = 7;
        config.RequestSave();

        SnapshotDirectory().Should().BeEquivalentTo(before,
            "a queued save serializes and returns without reaching a file");
        config.HasPendingSave.Should().BeTrue("the change is held for the write that follows");
        JsonValueOf(config.filePathOverride!, "Counter").Should().Be("1", "the change is held in memory");
    }

    #endregion

    #region One write per window

    [Fact]
    public void RequestSave_ManyTimesInsideTheWindow_CostsOneWrite()
    {
        NoireConfigBase.SaveDebounceInterval = TimeSpan.FromSeconds(30);

        var config = NewConfig("one-write.json");

        for (var i = 0; i < 25; i++)
        {
            config.Counter = i;
            config.RequestSave();

            File.Exists(config.filePathOverride!).Should().BeFalse(
                "no change inside the window is written on its own");
        }

        config.FlushPendingSave().Should().BeTrue();

        File.Exists(config.filePathOverride!).Should().BeTrue("the one write for the whole run happens when the window closes");
        JsonValueOf(config.filePathOverride!, "Counter").Should().Be("24", "the last change is the one on disk");
    }

    [Fact]
    public void RequestSave_WithNoFurtherChanges_WritesOnItsOwn()
    {
        NoireConfigBase.SaveDebounceInterval = TimeSpan.FromMilliseconds(50);

        var config = NewConfig("self-writing.json");
        config.Counter = 11;

        config.RequestSave();

        WaitFor(() => File.Exists(config.filePathOverride!), TimeSpan.FromSeconds(5))
            .Should().BeTrue("the background writer drains the queued payload without being asked again");

        JsonValueOf(config.filePathOverride!, "Counter").Should().Be("11");
    }

    [Fact]
    public void RequestSave_PastTheMaximumDelay_WritesWithoutWaitingForTheChangesToStop()
    {
        NoireConfigBase.SaveDebounceInterval = TimeSpan.FromSeconds(30);
        NoireConfigBase.MaxSaveDelay = TimeSpan.FromMilliseconds(150);

        var config = NewConfig("max-delay.json");
        config.Counter = 1;

        config.RequestSave();

        WaitFor(() => File.Exists(config.filePathOverride!), TimeSpan.FromSeconds(5))
            .Should().BeTrue("the ceiling on how long a change may sit unwritten keeps a long drag safe");
    }

    #endregion

    #region The bytes are the ones the synchronous path wrote

    [Fact]
    public void RequestSave_WritesTheSameBytesAsSave()
    {
        NoireConfigBase.SaveDebounceInterval = TimeSpan.FromMilliseconds(50);

        var synchronous = NewConfig("bytes-sync.json");
        synchronous.Toggle = true;
        synchronous.Counter = 42;
        synchronous.Favorites = [7, 8, 9];
        synchronous.Save().Should().BeTrue();

        var debounced = NewConfig("bytes-debounced.json");
        debounced.Toggle = true;
        debounced.Counter = 42;
        debounced.Favorites = [7, 8, 9];
        debounced.RequestSave();

        WaitFor(() => File.Exists(debounced.filePathOverride!), TimeSpan.FromSeconds(5)).Should().BeTrue();

        File.ReadAllBytes(debounced.filePathOverride!)
            .Should().Equal(File.ReadAllBytes(synchronous.filePathOverride!),
                "the on-disk format must not change for a file an existing user already has");
    }

    [Fact]
    public void Save_WritesOnlyTheMembersTheConfigurationDeclares()
    {
        var config = NewConfig("member-set.json");
        config.Save().Should().BeTrue();

        var written = JObject.Parse(File.ReadAllText(config.filePathOverride!));

        written.Properties().Select(p => p.Name)
            .Should().BeEquivalentTo(["Version", "Toggle", "Counter", "Favorites"],
                "anything the base class exposes for the save machinery itself has to be kept out of the file");
    }

    [Fact]
    public void RequestSave_WritesTheSchemaTheClassDeclaresRatherThanAVersionAssignedOverIt()
    {
        NoireConfigBase.SaveDebounceInterval = TimeSpan.FromMilliseconds(50);

        var config = NewConfig("version.json");
        config.Version = 99;

        config.RequestSave();

        WaitFor(() => File.Exists(config.filePathOverride!), TimeSpan.FromSeconds(5)).Should().BeTrue();

        JsonValueOf(config.filePathOverride!, "Version").Should().Be("3");
    }

    #endregion

    #region Nothing is lost

    [Fact]
    public void FlushPendingSave_WritesWhatIsStillQueued()
    {
        NoireConfigBase.SaveDebounceInterval = TimeSpan.FromSeconds(30);

        var config = NewConfig("flush-one.json");
        config.Counter = 5;
        config.RequestSave();

        File.Exists(config.filePathOverride!).Should().BeFalse("this only tests anything if the write has not happened yet");

        config.FlushPendingSave().Should().BeTrue();

        JsonValueOf(config.filePathOverride!, "Counter").Should().Be("5");
        config.HasPendingSave.Should().BeFalse();
    }

    [Fact]
    public void FlushPendingSaves_WritesEveryConfigurationHoldingChanges()
    {
        NoireConfigBase.SaveDebounceInterval = TimeSpan.FromSeconds(30);

        var first = NewConfig("flush-all-one.json");
        var second = NewConfig("flush-all-two.json");

        first.Counter = 1;
        first.RequestSave();
        second.Counter = 2;
        second.RequestSave();

        NoireConfigManager.FlushPendingSaves().Should().BeTrue();

        JsonValueOf(first.filePathOverride!, "Counter").Should().Be("1");
        JsonValueOf(second.filePathOverride!, "Counter").Should().Be("2");
    }

    [Fact]
    public void FlushPendingSave_WithNothingQueued_ReportsSuccess()
    {
        var config = NewConfig("flush-empty.json");

        config.FlushPendingSave().Should().BeTrue();
        File.Exists(config.filePathOverride!).Should().BeFalse("nothing was queued");
    }

    [Fact]
    public void Save_AfterARequest_LeavesTheQueuedWriteUnableToOvertakeIt()
    {
        NoireConfigBase.SaveDebounceInterval = TimeSpan.FromSeconds(30);

        var config = NewConfig("supersede.json");

        config.Counter = 1;
        config.RequestSave();

        config.Counter = 2;
        config.Save().Should().BeTrue();

        config.HasPendingSave.Should().BeFalse("the synchronous write supersedes the queued one");

        Thread.Sleep(200);

        JsonValueOf(config.filePathOverride!, "Counter").Should().Be("2");
    }

    [Fact]
    public void RequestSave_WhileDegraded_RefusesTheWaySaveDoes()
    {
        NoireConfigBase.SaveDebounceInterval = TimeSpan.FromMilliseconds(50);

        var config = new DegradedProbeConfig { filePathOverride = Path.Combine(tempDirectory, "degraded.json") };
        config.MarkDegraded();

        config.RequestSave();

        config.HasPendingSave.Should().BeFalse("a degraded configuration queues nothing");

        Thread.Sleep(200);
        File.Exists(config.filePathOverride!).Should().BeFalse("a degraded configuration writes nothing");
    }

    #endregion
}
