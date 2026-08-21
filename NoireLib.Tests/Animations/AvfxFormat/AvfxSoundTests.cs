using FluentAssertions;
using NoireLib.Animations.AvfxFormat;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// An effect names its sound on the emitter that plays it, and an effect can carry several emitters. The files
/// built here mirror the real layout: chunk tags are stored back to front, bodies are padded to four bytes, and
/// a chunk's body is either a value, a string, or more chunks.
/// </summary>
public class AvfxSoundTests
{
    private const string SoundPath = "sound/vfx/etc/SE_Vfx_Etc_Emote_uzura02.scd";
    private const string OtherSoundPath = "sound/vfx/etc/SE_VFX_Etc_Emot_Megaflare.scd";
    private const string TexturePath = "vfx/emote_sp/emt_sp084/texture/glow001f.atex";

    /// <summary> A chunk: its tag stored back to front, the length of its body, the body, then padding. </summary>
    private static byte[] Chunk(string tag, byte[] body)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        var tagBytes = new byte[4];
        Encoding.ASCII.GetBytes(tag.PadRight(4, '\0')).CopyTo(tagBytes, 0);
        Array.Reverse(tagBytes);

        writer.Write(tagBytes);
        writer.Write(body.Length);
        writer.Write(body);

        for (var padding = (4 - body.Length % 4) % 4; padding > 0; padding--)
            writer.Write((byte)0);

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] Text(string value) => [.. Encoding.ASCII.GetBytes(value), 0];

    private static byte[] Value(int value) => BitConverter.GetBytes(value);

    private static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    /// <summary> An emitter with a sound named on it, as the real ones carry it. </summary>
    private static byte[] Emitter(string? soundPath)
        => Chunk("Emit", Join(
            soundPath == null ? [] : Chunk("SdNm", Text(soundPath)),
            Chunk("SdNo", Value(soundPath == null ? -1 : 0)),
            Chunk("LpSt", Value(0))));

    private static byte[] File(params byte[][] chunks)
        => Chunk("AVFX", Join([Chunk("Ver", Value(0x20110913)), .. chunks]));

    [Fact]
    public void SoundPaths_AnEmitterNamingASound_ReportsIt()
        => AvfxSound.SoundPaths(File(Emitter(SoundPath))).Should().Equal(SoundPath);

    [Fact]
    public void SoundPaths_NoEmitterNamesOne_ReportsNothing()
        => AvfxSound.SoundPaths(File(Emitter(null), Emitter(null))).Should().BeEmpty();

    /// <summary> An effect can carry many emitters and state a sound on any of them, not only the first. </summary>
    [Fact]
    public void SoundPaths_ASoundOnALaterEmitter_IsStillReported()
        => AvfxSound.SoundPaths(File(Emitter(null), Emitter(null), Emitter(OtherSoundPath)))
            .Should().Equal(OtherSoundPath);

    [Fact]
    public void SoundPaths_SeveralEmittersNamingSounds_ReportsEachOfThem()
        => AvfxSound.SoundPaths(File(Emitter(SoundPath), Emitter(null), Emitter(OtherSoundPath)))
            .Should().Equal(SoundPath, OtherSoundPath);

    /// <summary> Textures are named the same way, so only the sound extension counts. </summary>
    [Fact]
    public void SoundPaths_AnotherKindOfFile_IsNotMistakenForASound()
        => AvfxSound.SoundPaths(File(Chunk("Tex", Text(TexturePath)), Emitter(null))).Should().BeEmpty();

    /// <summary> Nothing says a sound has to sit one level down, so the walk follows the nesting. </summary>
    [Fact]
    public void SoundPaths_ASoundNamedDeeperIn_IsStillFound()
        => AvfxSound.SoundPaths(File(Chunk("Schd", Chunk("Item", Chunk("SdNm", Text(SoundPath))))))
            .Should().Equal(SoundPath);

    [Fact]
    public void HasSound_AnEffectThatPlaysOne_ReadsAsSounding()
        => AvfxSound.HasSound(File(Emitter(SoundPath))).Should().BeTrue();

    [Fact]
    public void HasSound_AnEffectThatPlaysNone_ReadsAsSilent()
        => AvfxSound.HasSound(File(Emitter(null))).Should().BeFalse();

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(64)]
    public void SoundPaths_BytesThatAreNotAnEffect_ReportNothing(int length)
        => AvfxSound.SoundPaths(new byte[length]).Should().BeEmpty();

    [Fact]
    public void SoundPaths_ATruncatedEffect_ReportsWhatItCanRatherThanThrowing()
    {
        var whole = File(Emitter(SoundPath), Emitter(OtherSoundPath));
        var cut = whole[..(whole.Length / 2)];

        var reading = () => AvfxSound.SoundPaths(cut);

        reading.Should().NotThrow();
    }

    [Fact]
    public void SoundPaths_AChunkClaimingAnImpossibleLength_ReportsNothingRatherThanThrowing()
    {
        var file = File(Emitter(SoundPath));
        BitConverter.GetBytes(int.MaxValue).CopyTo(file, 4);

        var reading = () => AvfxSound.SoundPaths(file);

        reading.Should().NotThrow();
    }

    /// <summary> A negative length would read backwards through the file if it were trusted. </summary>
    [Fact]
    public void SoundPaths_AChunkClaimingANegativeLength_ReportsNothing()
    {
        var file = File(Emitter(SoundPath));
        BitConverter.GetBytes(-1).CopyTo(file, 4);

        AvfxSound.SoundPaths(file).Should().BeEmpty();
    }

    /// <summary> The reading is the same however many times it is taken. </summary>
    [Fact]
    public void SoundPaths_ReadTwice_ReadsTheSame()
    {
        var file = File(Emitter(SoundPath), Emitter(OtherSoundPath));

        var first = AvfxSound.SoundPaths(file);
        var second = AvfxSound.SoundPaths(file);

        second.Should().Equal((IEnumerable<string>)first);
    }
}
