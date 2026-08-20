using FluentAssertions;
using NoireLib.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks <see cref="FileHelper.ReplaceFileAtomically"/>'s contract, and in particular that each call writes
/// through a temporary of its OWN: a single shared <c>&lt;path&gt;.tmp</c> made two threads writing the same
/// file collide on the temporary - one could not open it while the other held it, or found it already moved
/// away - and report a failure for a write that had nothing wrong with it.
/// </summary>
public sealed class FileHelperTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"NoireLib.Tests.{Guid.NewGuid():N}");

    public FileHelperTests()
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
    public void ReplaceFileAtomically_WritesTheBytesAndLeavesNoTemporaryBehind()
    {
        var path = Path.Combine(_directory, "cache.bin");

        FileHelper.ReplaceFileAtomically(path, Encoding.UTF8.GetBytes("contents")).Should().BeTrue();

        File.ReadAllBytes(path).Should().Equal(Encoding.UTF8.GetBytes("contents"));
        Directory.GetFiles(_directory).Should().ContainSingle().Which.Should().Be(path);
    }

    [Fact]
    public void ReplaceFileAtomically_OverwritesWhatWasThere()
    {
        var path = Path.Combine(_directory, "cache.bin");
        File.WriteAllText(path, "old contents that are much longer than the new ones");

        FileHelper.ReplaceFileAtomically(path, Encoding.UTF8.GetBytes("new")).Should().BeTrue();

        File.ReadAllText(path).Should().Be("new");
    }

    /// <summary> Two calls for the same file never pick the same temporary - the whole point of the per-call name. </summary>
    [Fact]
    public void TemporaryWritePathFor_CalledTwiceForOneFile_PicksTwoDifferentNames()
    {
        var path = Path.Combine(_directory, "cache.bin");

        FileHelper.TemporaryWritePathFor(path).Should().NotBe(FileHelper.TemporaryWritePathFor(path));
    }

    /// <summary> Beside the target (same volume, so the move stays atomic) and recognisable as a temporary. </summary>
    [Fact]
    public void TemporaryWritePathFor_SitsBesideTheTargetAndEndsInTmp()
    {
        var path = Path.Combine(_directory, "cache.bin");

        var temporary = FileHelper.TemporaryWritePathFor(path);

        Path.GetDirectoryName(temporary).Should().Be(_directory);
        temporary.Should().StartWith(path).And.EndWith(".tmp");
    }

    /// <summary>
    /// The safety property under concurrency, which must hold however the temporaries are named: several
    /// threads writing one file leave it holding exactly one payload, whole, and at least one of them
    /// reports the write it made.
    ///
    /// A loser may still report failure: two threads also race over the final move onto the shared target,
    /// and that race is not this helper's to win - a caller that needs every concurrent write accepted has
    /// to serialise them itself. What the per-call temporary buys is measured by
    /// <see cref="TemporaryWritePathFor_CalledTwiceForOneFile_PicksTwoDifferentNames"/>: writers no longer
    /// queue behind ONE temporary they all have open.
    /// </summary>
    [Fact]
    public void ReplaceFileAtomically_SeveralThreadsWritingOneFile_LeavesTheFileWholeRatherThanTorn()
    {
        var path = Path.Combine(_directory, "cache.bin");
        var payloads = Enumerable.Range(0, 8)
            .Select(index => Encoding.UTF8.GetBytes($"payload number {index} {new string((char)('a' + index), index * 32)}"))
            .ToList();

        var results = new bool[payloads.Count];
        Parallel.For(0, payloads.Count, index => results[index] = FileHelper.ReplaceFileAtomically(path, payloads[index]));

        results.Should().Contain(true, "the writer that won the target still wrote it");

        var written = File.ReadAllBytes(path);
        payloads.Should().Contain(payload => payload.SequenceEqual(written),
            "the target must hold one payload whole, never a mixture of several");
        Directory.GetFiles(_directory).Should().ContainSingle().Which.Should().Be(path);
    }

    /// <summary> A stray temporary left by an earlier crash is inert: nothing reads it, and a fresh write ignores it. </summary>
    [Fact]
    public void ReplaceFileAtomically_WithAStrayTemporaryBeside_WritesTheTargetAnyway()
    {
        var path = Path.Combine(_directory, "cache.bin");
        File.WriteAllText(path + ".tmp", "left behind by a crash");

        FileHelper.ReplaceFileAtomically(path, Encoding.UTF8.GetBytes("fresh")).Should().BeTrue();

        File.ReadAllText(path).Should().Be("fresh");
    }

    /// <summary> Nothing is written for arguments that name no file or carry no bytes. </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ReplaceFileAtomically_WithNoUsablePath_ReportsFailure(string path)
        => FileHelper.ReplaceFileAtomically(path, Encoding.UTF8.GetBytes("contents")).Should().BeFalse();
}
