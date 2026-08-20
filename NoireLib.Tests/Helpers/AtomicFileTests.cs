using FluentAssertions;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Ported from PapEdit.Tests: locks AtomicFile's contract now that PenumbraModFormat's writers depend
/// on it never leaving a half-written or BOM-prefixed file behind.
/// </summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"NoireLib.Tests.{Guid.NewGuid():N}");

    public AtomicFileTests()
        => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public void WriteAllText_CreatesFileWithoutBom()
    {
        var path = Path.Combine(_directory, "meta.json");

        AtomicFile.WriteAllText(path, "{\"a\":1}");

        var bytes = File.ReadAllBytes(path);
        Encoding.UTF8.GetString(bytes).Should().Be("{\"a\":1}");
        bytes[0].Should().NotBe((byte)0xEF, "a BOM would add a character Penumbra does not expect at the start of the file");
    }

    [Fact]
    public void WriteAllText_OverwritesExistingFile()
    {
        var path = Path.Combine(_directory, "meta.json");
        File.WriteAllText(path, "old contents that are much longer than the new ones");

        AtomicFile.WriteAllText(path, "new");

        File.ReadAllText(path).Should().Be("new");
    }

    [Fact]
    public void WriteAllText_LeavesNoTemporaryFilesBehind()
    {
        var path = Path.Combine(_directory, "meta.json");

        AtomicFile.WriteAllText(path, "one");
        AtomicFile.WriteAllText(path, "two");

        var files = Directory.GetFiles(_directory);
        files.Should().ContainSingle().Which.Should().Be(path);
    }

    [Fact]
    public void WriteAllText_CreatesMissingDirectories()
    {
        var path = Path.Combine(_directory, "nested", "meta.json");

        AtomicFile.WriteAllText(path, "value");

        File.ReadAllText(path).Should().Be("value");
    }

    [Fact]
    public void WriteAllText_DestinationHeldOpenExclusively_ThrowsAndCleansUpItsTemporaryFile()
    {
        var path = Path.Combine(_directory, "meta.json");
        File.WriteAllText(path, "old");

        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var act = () => AtomicFile.WriteAllText(path, "new");

            act.Should().Throw<IOException>("every replacement strategy needs the holder to let go of the destination");
        }

        File.ReadAllText(path).Should().Be("old", "a failed write must leave the previous contents intact");
        Directory.GetFiles(_directory).Should().ContainSingle().Which.Should().Be(path);
    }

    [Fact]
    public void ReplaceWithFallback_ReplaceSucceeded_RunsNoFallback()
    {
        var calls = new List<string>();

        AtomicFile.ReplaceWithFallback(
            replace: () => calls.Add("replace"),
            deleteThenMove: () => calls.Add("deleteThenMove"),
            copyOver: () => calls.Add("copyOver"));

        calls.Should().Equal("replace");
    }

    [Fact]
    public void ReplaceWithFallback_ReplaceFailed_DeleteThenMoveRecoversWithoutTheLastResort()
    {
        var calls = new List<string>();

        AtomicFile.ReplaceWithFallback(
            replace: () => { calls.Add("replace"); throw new IOException("held open"); },
            deleteThenMove: () => calls.Add("deleteThenMove"),
            copyOver: () => calls.Add("copyOver"));

        calls.Should().Equal("replace", "deleteThenMove");
    }

    [Fact]
    public void ReplaceWithFallback_FirstFallbackFailedToo_CopyRecovers()
    {
        var calls = new List<string>();

        AtomicFile.ReplaceWithFallback(
            replace: () => { calls.Add("replace"); throw new IOException("held open"); },
            deleteThenMove: () => { calls.Add("deleteThenMove"); throw new IOException("still held"); },
            copyOver: () => calls.Add("copyOver"));

        calls.Should().Equal("replace", "deleteThenMove", "copyOver");
    }

    [Fact]
    public void ReplaceWithFallback_EverythingFailed_ThrowsTheExceptionReplaceThrew()
    {
        var replaceFailure = new IOException("held open");

        var act = () => AtomicFile.ReplaceWithFallback(
            replace: () => throw replaceFailure,
            deleteThenMove: () => throw new IOException("still held"),
            copyOver: () => throw new UnauthorizedAccessException("still held"));

        act.Should().Throw<IOException>().Which.Should().BeSameAs(replaceFailure,
            "callers already handle the failure File.Replace reports, not whichever fallback failed last");
    }

    [Fact]
    public void ReplaceWithFallback_NonIoFailure_PropagatesWithoutTryingTheFallbacks()
    {
        var calls = new List<string>();

        var act = () => AtomicFile.ReplaceWithFallback(
            replace: () => throw new UnauthorizedAccessException("no rights, so no point retrying differently"),
            deleteThenMove: () => calls.Add("deleteThenMove"),
            copyOver: () => calls.Add("copyOver"));

        act.Should().Throw<UnauthorizedAccessException>();
        calls.Should().BeEmpty();
    }
}
