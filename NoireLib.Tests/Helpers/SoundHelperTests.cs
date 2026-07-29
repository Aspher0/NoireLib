using FluentAssertions;
using NoireLib.Helpers;
using System.IO;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks what the sound helper does with no game and no audio device behind it. Playing a sound cannot be asserted
/// from a test runner, so what is pinned here is the contract that matters to a caller: nothing throws, a bad path is
/// refused rather than opened, and a refused playback hands back nothing to leak.
/// </summary>
public sealed class SoundHelperTests
{
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

    [Fact]
    public void StopAllFiles_IsSafeWithNothingPlaying()
    {
        var stop = SoundHelper.StopAllFiles;

        stop.Should().NotThrow();
        stop.Should().NotThrow();
    }
}
