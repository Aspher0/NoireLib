using FluentAssertions;
using NoireLib.Helpers;
using System;
using System.IO;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks what the sound helper does with no game behind it: nothing throws, a bad path is refused rather than opened,
/// and a refused playback hands back nothing to leak.
/// <br/>
/// The playback tests use a silent file so a test run stays quiet, which still exercises the whole open and play path
/// because the audio backend refuses a malformed file long before it reaches a speaker.
/// </summary>
public sealed class SoundHelperTests
{
    /// <summary>Writes a short silent 8 bit PCM wav, the smallest thing the audio backend accepts as playable.</summary>
    private static void WriteSilentWav(string path)
    {
        const int sampleRate = 8000;
        const int samples = sampleRate / 5;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var writer = new BinaryWriter(File.Create(path));

        writer.Write("RIFF"u8);
        writer.Write(36 + samples);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);              // Chunk size for PCM.
        writer.Write((short)1);        // PCM, uncompressed.
        writer.Write((short)1);        // Mono.
        writer.Write(sampleRate);
        writer.Write(sampleRate);      // Byte rate, which equals the sample rate at one 8 bit mono sample per frame.
        writer.Write((short)1);        // Block align.
        writer.Write((short)8);        // Bits per sample.
        writer.Write("data"u8);
        writer.Write(samples);

        // 0x80 is the midpoint of unsigned 8 bit audio, so the file is silence rather than a click.
        var silence = new byte[samples];
        Array.Fill(silence, (byte)0x80);
        writer.Write(silence);
    }

    /// <summary>A directory of this run's own, so a leftover file never decides whether a test passes.</summary>
    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"noire-sound-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        return path;
    }

    /// <summary>
    /// Nests folders under a root until the file path inside reaches at least the requested length. The folder names
    /// are long on purpose, because that is the shape a real music library or plugin folder has and a path built from
    /// many one-letter folders instead is the worst case rather than the ordinary one.
    /// </summary>
    private static string NestedPath(string root, int length)
    {
        const string fileName = "sound.wav";
        const string folderName = "a_reasonably_long_folder_name";

        var directory = root;

        while (directory.Length + 1 + fileName.Length < length)
            directory = Path.Combine(directory, folderName);

        return Path.Combine(directory, fileName);
    }

    [Fact]
    public void PlayFile_PlaysAFileWhosePathIsPlain()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "sound.wav");
        WriteSilentWav(path);

        try
        {
            using var playback = SoundHelper.PlayFile(path);

            playback.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void PlayFile_PlaysAFileWhoseNameHasSpaces()
    {
        // The path a player actually picks: music on disk is named with spaces far more often than not.
        var directory = NewDirectory();
        var path = Path.Combine(directory, "my sound file.wav");
        WriteSilentWav(path);

        try
        {
            using var playback = SoundHelper.PlayFile(path);

            playback.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void PlayFile_PlaysAFileWhoseFolderHasSpaces()
    {
        var directory = Path.Combine(NewDirectory(), "a folder with spaces");
        var path = Path.Combine(directory, "sound.wav");
        WriteSilentWav(path);

        try
        {
            using var playback = SoundHelper.PlayFile(path);

            playback.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(directory)!, true);
        }
    }

    [Fact]
    public void PlayFile_PlaysAFileWhosePathIsNotAscii()
    {
        // Any player whose account name or music carries an accent lands here, which is most of them outside English.
        var directory = NewDirectory();
        var path = Path.Combine(directory, "sonorité.wav");
        WriteSilentWav(path);

        try
        {
            using var playback = SoundHelper.PlayFile(path);

            playback.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(150)]
    [InlineData(200)]
    [InlineData(250)]
    [InlineData(300)]
    public void PlayFile_PlaysAFileWhosePathIsLongerThanTheCommandLimit(int pathLength)
    {
        // The multimedia interface refuses a path past 126 characters, and a plugin's own configuration folder spends
        // more than half of that before a file name is added, so this is the ordinary case rather than an exotic one.
        // The 300 case also carries the path past the 260 Windows reads by default.
        var root = NewDirectory();
        var path = NestedPath(root, pathLength);
        WriteSilentWav(path);

        try
        {
            path.Length.Should().BeGreaterThan(126, "the test is meaningless unless the path is over the limit");

            using var playback = SoundHelper.PlayFile(path);

            playback.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PlayFile_RefusesAPathNoShortFormCanRescue()
    {
        // A tree of one-letter folders has no shorter form to fall back on, so the helper has to give up. What is
        // pinned is that it gives up quietly rather than throwing, and hands back nothing to leak.
        var root = NewDirectory();
        var directory = root;

        while (directory.Length < 220)
            directory = Path.Combine(directory, "d");

        var path = Path.Combine(directory, "sound.wav");
        WriteSilentWav(path);

        try
        {
            var play = () => SoundHelper.PlayFile(path);

            play.Should().NotThrow();
            play().Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PlayFile_KeepsThePathTheCallerGave()
    {
        // The short form used to satisfy the multimedia interface must not leak back out: a caller displaying the
        // file name would otherwise show a mangled one.
        var root = NewDirectory();
        var path = NestedPath(root, 160);
        WriteSilentWav(path);

        try
        {
            using var playback = SoundHelper.PlayFile(path);

            playback.Should().NotBeNull();
            playback!.Path.Should().Be(path);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PlayFile_RefusesAPathThatIsNotThere()
        => SoundHelper.PlayFile(Path.Combine(Path.GetTempPath(), "noire-no-such-sound.wav")).Should().BeNull();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PlayFile_RefusesAnEmptyPath(string path) => SoundHelper.PlayFile(path).Should().BeNull();

    [Fact]
    public void PlayFile_RefusesAFileWindowsCannotOpen()
    {
        // A real file whose contents are not audio: the open has to fail, and failing must not leave a device behind.
        var path = Path.Combine(Path.GetTempPath(), $"noire-not-audio-{System.Guid.NewGuid():N}.wav");
        File.WriteAllText(path, "this is not a sound");

        try
        {
            SoundHelper.PlayFile(path).Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GameSounds_DoNothingWithoutAClient()
    {
        // Every one of these reaches game code, so with no client they must return quietly rather than throw: a
        // notification sound is never worth taking a plugin down for.
        var play = () =>
        {
            SoundHelper.PlayEffect(0);
            SoundHelper.PlayEffect(1);
            SoundHelper.PlayChatEffect(0);
            SoundHelper.PlayChatEffect(1);
        };

        play.Should().NotThrow();
    }

    /// <summary>Writes a file that opens with an ID3 tag of the requested length and nothing else worth reading.</summary>
    private static void WriteTaggedFile(string path, int tagLength)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var writer = new BinaryWriter(File.Create(path));

        writer.Write("ID3"u8);
        writer.Write((byte)3);      // Version 2.3.
        writer.Write((byte)0);
        writer.Write((byte)0);      // Flags.

        // Seven bits per byte, most significant first.
        writer.Write((byte)((tagLength >> 21) & 0x7F));
        writer.Write((byte)((tagLength >> 14) & 0x7F));
        writer.Write((byte)((tagLength >> 7) & 0x7F));
        writer.Write((byte)(tagLength & 0x7F));

        writer.Write(new byte[tagLength]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]           // The first length that spills into a second byte.
    [InlineData(16383)]
    [InlineData(16384)]         // The first length that spills into a third byte.
    [InlineData(119_308)]       // A tag Windows reads past happily.
    [InlineData(357_683)]       // The tag from a file Windows refuses.
    public void ReadId3Length_ReadsALengthSevenBitsAtATime(int tagLength)
    {
        // The length is stored seven bits per byte, so reading it as plain bytes silently works for small tags and
        // gives a wrong answer for exactly the large ones this is here to recognise.
        var directory = NewDirectory();
        var path = Path.Combine(directory, "tagged.mp3");
        WriteTaggedFile(path, tagLength);

        try
        {
            SoundHelper.ReadId3Length(path).Should().Be(tagLength + 10, "the ten byte header counts toward the tag");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ReadId3Length_ReportsNothingForAFileWithoutATag()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "sound.wav");
        WriteSilentWav(path);

        try
        {
            SoundHelper.ReadId3Length(path).Should().Be(0);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ReadId3Length_ReportsNothingForAFileTooShortToHoldAHeader()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "tiny.mp3");
        File.WriteAllBytes(path, "ID3"u8.ToArray());

        try
        {
            SoundHelper.ReadId3Length(path).Should().Be(0);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Writes a file shaped like an mp3 whose audio sits behind a tag of the requested size: a real frame header so
    /// the start of the audio can be found, then filler standing in for the rest of it.
    /// </summary>
    private static void WriteTaggedMp3(string path, int tagLength)
    {
        WriteTaggedFile(path, tagLength);

        using var stream = new FileStream(path, FileMode.Append);

        // A valid MPEG-1 Layer III header: sync, then 128 kbps at 44.1 kHz.
        stream.Write([0xFF, 0xFB, 0x90, 0x00]);
        stream.Write(new byte[512]);
    }

    [Fact]
    public void TryStripTag_CopiesTheAudioOutFromBehindAnOversizedTag()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "tagged.mp3");
        WriteTaggedMp3(path, 200_000);

        try
        {
            SoundHelper.TryStripTag(path, out var stripped).Should().BeTrue();

            File.Exists(stripped).Should().BeTrue();

            // What is left is the audio and nothing in front of it.
            var written = File.ReadAllBytes(stripped);
            written.Length.Should().Be(516);
            written[0].Should().Be(0xFF);
            written[1].Should().Be(0xFB);

            stripped.Length.Should().BeLessThanOrEqualTo(126, "the copy has to be openable in its own right");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void TryStripTag_ReusesTheCopyItAlreadyMade()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "tagged.mp3");
        WriteTaggedMp3(path, 200_000);

        try
        {
            SoundHelper.TryStripTag(path, out var first).Should().BeTrue();
            var writtenAt = File.GetLastWriteTimeUtc(first);

            SoundHelper.TryStripTag(path, out var second).Should().BeTrue();

            second.Should().Be(first);
            File.GetLastWriteTimeUtc(second).Should().Be(writtenAt, "a second play must not copy the file again");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void TryStripTag_RefusesAFileWindowsCouldAlreadyRead()
    {
        // Under the window there is nothing to fix, and copying a file Windows can already open would be waste.
        var directory = NewDirectory();
        var path = Path.Combine(directory, "tagged.mp3");
        WriteTaggedMp3(path, 1000);

        try
        {
            SoundHelper.TryStripTag(path, out _).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void TryStripTag_RefusesAnythingThatIsNotAnMp3()
    {
        // The window belongs to the mp3 reader, so no other format has this problem to solve.
        var directory = NewDirectory();
        var path = Path.Combine(directory, "tagged.wav");
        WriteTaggedMp3(path, 200_000);

        try
        {
            SoundHelper.TryStripTag(path, out _).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void StopAllFiles_IsSafeWithNothingPlaying()
    {
        var stop = SoundHelper.StopAllFiles;

        stop.Should().NotThrow();
        stop.Should().NotThrow();
    }
}
