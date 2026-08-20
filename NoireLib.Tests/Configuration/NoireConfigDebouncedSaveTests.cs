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
/// Locks the contract of the debounced save path. A member marked <see cref="AutoSaveAttribute"/> is assigned on the
/// framework thread, so what that assignment is allowed to do on that thread is the whole point: it may serialize, and
/// it may not touch a file.<br/>
/// The rest of the contract follows from that. A run of changes inside one window costs a single write rather than one
/// per change, the bytes that reach the file are the ones the synchronous <see cref="NoireConfigBase.Save"/> would have
/// written, a change may not sit unwritten indefinitely while further changes arrive, and a shutdown flush leaves
/// nothing behind.
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
        // Drained before the directory goes, so a test that deliberately left a payload queued does not leave a
        // background writer aiming at a path that no longer exists.
        NoireConfigBase.FlushAllPendingSaves();

        // Process-wide, so a test that changes them restores them or skews every test that follows.
        NoireConfigBase.SaveDebounceInterval = originalDebounce;
        NoireConfigBase.MaxSaveDelay = originalMaxDelay;

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
    /// A configuration shaped like a real one, with a collection member among the scalars, since a collection is what
    /// makes serializing away from the owning thread unsafe and therefore what decides where the serialization runs.
    /// </summary>
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

    /// <summary>Exposes the degraded latch, which is otherwise only reached through a failed migration.</summary>
    private sealed class DegradedProbeConfig : ProbeConfig
    {
        public void MarkDegraded() => degradedLoad = true;
    }

    private ProbeConfig NewConfig(string fileName)
        => new() { filePathOverride = Path.Combine(tempDirectory, fileName) };

    /// <summary>
    /// Waits for a condition rather than for a fixed delay, so a slow machine costs time rather than a failure.
    /// </summary>
    /// <param name="condition">The condition to wait for.</param>
    /// <param name="timeout">How long to keep waiting.</param>
    /// <returns>True if the condition held before the timeout elapsed; otherwise, false.</returns>
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

    /// <summary>
    /// Reads one top-level value out of a configuration file as text, so that an assertion names the member it is
    /// about rather than a chunk of JSON.
    /// </summary>
    /// <param name="path">The configuration file to read.</param>
    /// <param name="member">The member to read.</param>
    /// <returns>The member's value as it appears in the file.</returns>
    private static string JsonValueOf(string path, string member)
        => JObject.Parse(File.ReadAllText(path))[member]!.ToString();

    #region A request does not touch the disk

    [Fact]
    public void RequestSave_DoesNotWriteTheFileOnTheCallingThread()
    {
        // The reason the debounce exists: the assignment happens on the framework thread, and a write is disk work
        // whose duration nothing bounds, so it must not have happened by the time the assignment returns.
        NoireConfigBase.SaveDebounceInterval = TimeSpan.FromSeconds(30);

        var config = NewConfig("no-disk-touch.json");
        config.Toggle = true;

        config.RequestSave();

        File.Exists(config.filePathOverride!).Should().BeFalse("a request marks the configuration changed, it does not write it");
        config.HasPendingSave.Should().BeTrue("the change is held for the write that follows");
    }

    [Fact]
    public void RequestSave_ReturnsWellInsideAFrame()
    {
        // Stated as the budget it has to fit into: a frame at 60 fps is about 16 ms, and one setting change must not
        // spend a meaningful part of one.
        NoireConfigBase.SaveDebounceInterval = TimeSpan.FromSeconds(30);

        var config = NewConfig("frame-budget.json");

        // Warmed first: the very first serialization of a type builds its serializer contract and emits the accessors
        // it needs, which is far more expensive than every serialization after it.
        config.Save();

        var watch = Stopwatch.StartNew();
        config.Counter = 7;
        config.RequestSave();
        watch.Stop();

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(5),
            "a queued save serializes and returns, and nothing in that touches a file");
    }

    #endregion

    #region One write per window

    [Fact]
    public void RequestSave_ManyTimesInsideTheWindow_CostsOneWrite()
    {
        // What dragging a slider produces: one request per frame. Every one of them used to be a full write, read-back
        // and atomic replace included.
        NoireConfigBase.SaveDebounceInterval = TimeSpan.FromSeconds(30);

        var config = NewConfig("one-write.json");

        for (var i = 0; i < 25; i++)
        {
            config.Counter = i;
            config.RequestSave();

            File.Exists(config.filePathOverride!).Should().BeFalse(
                "no change inside the window is written on its own, so the whole run so far has cost no write at all");
        }

        config.FlushPendingSave().Should().BeTrue();

        File.Exists(config.filePathOverride!).Should().BeTrue("the one write for the whole run happens when the window closes");
        JsonValueOf(config.filePathOverride!, "Counter").Should().Be("24", "the last change is the one on disk");
    }

    [Fact]
    public void RequestSave_WithNoFurtherChanges_WritesOnItsOwn()
    {
        // The queued write is not waiting for anyone to ask for it: nothing but the window closing has to happen.
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
        // A debounce on its own lets an unbroken run of changes defer the write for as long as the run lasts, which is
        // a setting held only in memory for that whole time.
        NoireConfigBase.SaveDebounceInterval = TimeSpan.FromSeconds(30);
        NoireConfigBase.MaxSaveDelay = TimeSpan.FromMilliseconds(150);

        var config = NewConfig("max-delay.json");
        config.Counter = 1;

        config.RequestSave();

        WaitFor(() => File.Exists(config.filePathOverride!), TimeSpan.FromSeconds(5))
            .Should().BeTrue("the ceiling on how long a change may sit unwritten is what makes a long drag safe");
    }

    #endregion

    #region The bytes are the ones the synchronous path wrote

    [Fact]
    public void RequestSave_WritesTheSameBytesAsSave()
    {
        // Existing users have configuration files on disk, so the format the debounced path produces has to be the one
        // the synchronous path produced, byte for byte.
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
        // A public member added to the base class reaches every configuration file in every plugin built against it.
        // Existing files are read back by users, so the set of names in one is part of the format and not free to grow.
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
        // The same hardening the synchronous path has. The number in the file is what the next load measures the file
        // against, so a version assigned over the property must not reach it.
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
        // The shutdown guarantee in the small: whatever the window is still holding is on disk once this returns.
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
        // What a plugin unload runs. A configuration that is not in the manager's cache is in it too, since a
        // configuration nothing cached still holds the last setting a user changed before quitting.
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
        // A flush reaches every configuration at shutdown, most of which changed nothing, and that must not be
        // reported as a failure to write.
        var config = NewConfig("flush-empty.json");

        config.FlushPendingSave().Should().BeTrue();
        File.Exists(config.filePathOverride!).Should().BeFalse("nothing was queued, so nothing is written");
    }

    [Fact]
    public void Save_AfterARequest_LeavesTheQueuedWriteUnableToOvertakeIt()
    {
        // A synchronous save carries everything the queued payload held, so letting the queued one land afterwards
        // would put older values back on disk.
        NoireConfigBase.SaveDebounceInterval = TimeSpan.FromSeconds(30);

        var config = NewConfig("supersede.json");

        config.Counter = 1;
        config.RequestSave();

        config.Counter = 2;
        config.Save().Should().BeTrue();

        config.HasPendingSave.Should().BeFalse("the synchronous write supersedes the queued one");

        // Long enough that a surviving queued write would have had its window several times over.
        Thread.Sleep(200);

        JsonValueOf(config.filePathOverride!, "Counter").Should().Be("2");
    }

    [Fact]
    public void RequestSave_WhileDegraded_RefusesTheWaySaveDoes()
    {
        // A configuration a failed migration left partially defaulted must not reach the file by the debounced path
        // either, or the protection is only half there.
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
