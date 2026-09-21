using FluentAssertions;
using NoireLib.Remote;
using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Covers the folder a caller finds a listener through: an atomic write, the two liveness signals, and the reader's
/// behavior on a half-written, unreadable or foreign record.
/// </summary>
public sealed class NoireRemoteDirectoryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "NoireRemoteTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private NoireRemoteInstanceRecord Fresh(string plugin = "TestPlugin")
    {
        using var process = Process.GetCurrentProcess();

        return new NoireRemoteInstanceRecord
        {
            Instance = Guid.NewGuid(),
            Address = "127.0.0.1",
            Port = 47821,
            Token = "a-token",
            Pid = process.Id,
            ProcessStartUtc = process.StartTime.ToUniversalTime(),
            StartedUtc = DateTime.UtcNow,
            HeartbeatUtc = DateTime.UtcNow,
            Plugin = plugin,
            PluginVersion = "1.0.0.0",
            LibraryVersion = "2.0.1",
            Label = "Someone @ Somewhere",
            Endpoints = ["MyApi"],
        };
    }

    [Fact]
    public void Write_ThenReadAll_RoundTripsEveryField()
    {
        var record = Fresh();
        NoireRemoteDirectory.Write(directory, record);

        var read = NoireRemoteDirectory.ReadAll(directory);

        read.Should().HaveCount(1);
        read[0].Instance.Should().Be(record.Instance);
        read[0].Port.Should().Be(47821);
        read[0].Token.Should().Be("a-token");
        read[0].Endpoints.Should().ContainSingle().Which.Should().Be("MyApi");
        read[0].BaseUrl().Should().Be("http://127.0.0.1:47821/");
    }

    [Fact]
    public void Write_LeavesNoTemporaryFileBehind()
    {
        NoireRemoteDirectory.Write(directory, Fresh());

        Directory.GetFiles(directory, "*.tmp").Should().BeEmpty(
            "the bytes land in a temporary file that is moved over the target; a reader never sees half a record");
    }

    [Fact]
    public void Write_Twice_ReplacesTheRecordRatherThanAddingOne()
    {
        var record = Fresh();
        NoireRemoteDirectory.Write(directory, record);

        record.Port = 47999;
        NoireRemoteDirectory.Write(directory, record);

        var read = NoireRemoteDirectory.ReadAll(directory);
        read.Should().HaveCount(1);
        read[0].Port.Should().Be(47999);
    }

    [Fact]
    public void ReadAll_SkipsARecordOfAnotherProtocolVersion()
    {
        var record = Fresh();
        record.Protocol = 99;
        NoireRemoteDirectory.Write(directory, record);

        NoireRemoteDirectory.ReadAll(directory).Should().BeEmpty();
    }

    [Fact]
    public void ReadAll_SkipsAHalfWrittenFile()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, Guid.NewGuid().ToString("D") + ".json"), "{\"protocol\":1,\"port\":47");

        NoireRemoteDirectory.ReadAll(directory).Should().BeEmpty();
    }

    [Fact]
    public void ReadAll_SkipsAFileThatIsNotJson()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, Guid.NewGuid().ToString("D") + ".json"), "not json at all");

        NoireRemoteDirectory.ReadAll(directory).Should().BeEmpty();
    }

    [Fact]
    public void ReadAll_OnAMissingFolder_ReturnsNothing()
    {
        NoireRemoteDirectory.ReadAll(Path.Combine(directory, "never-created")).Should().BeEmpty();
    }

    [Fact]
    public void Delete_RemovesTheRecordAndIgnoresASecondCall()
    {
        var record = Fresh();
        NoireRemoteDirectory.Write(directory, record);

        NoireRemoteDirectory.Delete(directory, record.Instance);
        NoireRemoteDirectory.Delete(directory, record.Instance);

        NoireRemoteDirectory.ReadAll(directory).Should().BeEmpty();
    }

    [Fact]
    public void ProcessIsAlive_ForThisProcess_IsTrue()
    {
        NoireRemoteDirectory.ProcessIsAlive(Fresh()).Should().BeTrue();
    }

    [Fact]
    public void ProcessIsAlive_ForAProcessIdThatIsNotRunning_IsFalse()
    {
        var record = Fresh();
        record.Pid = int.MaxValue - 1;

        NoireRemoteDirectory.ProcessIsAlive(record).Should().BeFalse();
    }

    [Fact]
    public void ProcessIsAlive_WhenTheStartTimeDisagrees_IsFalse()
    {
        var record = Fresh();
        record.ProcessStartUtc = DateTime.UtcNow.AddDays(-7);

        NoireRemoteDirectory.ProcessIsAlive(record).Should().BeFalse(
            "a reused process id is what this check exists to catch");
    }

    [Fact]
    public void HeartbeatIsFresh_ComparesAgainstTheWindow()
    {
        var record = Fresh();
        var now = DateTime.UtcNow;

        NoireRemoteDirectory.HeartbeatIsFresh(record, TimeSpan.FromSeconds(60), now).Should().BeTrue();

        record.HeartbeatUtc = now.AddSeconds(-120);
        NoireRemoteDirectory.HeartbeatIsFresh(record, TimeSpan.FromSeconds(60), now).Should().BeFalse();
    }

    [Fact]
    public void Publishes_MatchesIgnoringCase()
    {
        var record = Fresh();

        record.Publishes("myapi").Should().BeTrue();
        record.Publishes("Other").Should().BeFalse();
    }

    [Fact]
    public void RecordPath_NamesTheFileAfterTheInstance()
    {
        var instance = Guid.NewGuid();

        NoireRemoteDirectory.RecordPath(directory, instance)
            .Should().Be(Path.Combine(directory, instance.ToString("D") + ".json"));
    }
}
