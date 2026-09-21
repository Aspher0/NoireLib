using FluentAssertions;
using NoireLib.Animations.PapFormat;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Builds minimal but structurally complete .paps entirely in memory, then drives them through
/// <see cref="PapFaceLibrary"/>. The fixtures mirror two vanilla TMB shapes: a charmed-style one (C010 face entry,
/// C009 body entry, and a raw C012 vfx entry carrying its own offset string, no TMPP) and a balldance-style one
/// (TMPP first, then the same entries). The C012 is load-bearing: a raw entry after the TMPP insertion point whose
/// string offset must survive injection untouched.
/// </summary>
public class PapFaceLibraryTests
{
    // "pap " little-endian.
    private const int PapMagic = 0x20706170;

    private const int NameFieldLength = 32;

    private const int FooterFieldOffset = 22;

    // After the 12-byte TMLB header and the 0x10-byte TMDH.
    private const int TmppInsertOffset = 0x1C;

    private const string AvfxPath = "vfx/common/eff/dk05th_stup0t.avfx";

    // A single-actor, single-track TMB: TMDH, optional TMPP, TMAL, one TMAC, one TMTR, then an optional C010, a C009
    // and an optional raw C012 on that track. Strings come last, TMPP's first, like vanilla files.
    private static byte[] BuildTmb(string? tmppName = null, string? c010Path = null, string? c009Path = null, string? c012Path = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        void WriteItemHeader(string magic, int size)
        {
            writer.Write(Encoding.ASCII.GetBytes(magic));
            writer.Write(size);
        }

        var entryCount = (c010Path != null ? 1 : 0) + 1 + (c012Path != null ? 1 : 0);
        var itemCount = 4 + (tmppName != null ? 1 : 0) + entryCount; // TMDH, [TMPP], TMAL, TMAC, TMTR + entries

        writer.Write(Encoding.ASCII.GetBytes("TMLB"));
        var sizePos = stream.Position;
        writer.Write(0); // total size, backpatched
        writer.Write(itemCount);

        WriteItemHeader("TMDH", 0x10);
        writer.Write((short)1); // id
        writer.Write((short)0); // Unk1
        writer.Write((short)0); // Length
        writer.Write((short)0); // Unk3

        // TMPP: magic, size, one offset string. No id or time.
        var tmppStart = stream.Position;
        var tmppOffsetFieldPos = 0L;
        if (tmppName != null)
        {
            WriteItemHeader("TMPP", 0x0C);
            tmppOffsetFieldPos = stream.Position;
            writer.Write(0); // backpatched
        }

        var tmalStart = stream.Position;
        WriteItemHeader("TMAL", 0x10);
        var tmalOffsetFieldPos = stream.Position;
        writer.Write(0); // backpatched
        writer.Write(1);

        var tmacStart = stream.Position;
        WriteItemHeader("TMAC", 0x1C);
        writer.Write((short)2); // id
        writer.Write((short)0); // time
        writer.Write(0);        // AbilityDelay
        writer.Write(0);        // Unk2
        var tmacOffsetFieldPos = stream.Position;
        writer.Write(0); // backpatched
        writer.Write(1);

        var tmtrStart = stream.Position;
        WriteItemHeader("TMTR", 0x18);
        writer.Write((short)3); // id
        writer.Write((short)0); // time
        var tmtrOffsetFieldPos = stream.Position;
        writer.Write(0); // backpatched
        writer.Write(entryCount);
        writer.Write(0);

        short nextEntryId = 4;
        var entryIds = new List<short>();

        // C010 with Flags=1 names a cfxf_* animation from the loaded library.
        var c010Start = 0L;
        var c010OffsetFieldPos = 0L;
        if (c010Path != null)
        {
            c010Start = stream.Position;
            WriteItemHeader("C010", 0x28);
            entryIds.Add(nextEntryId);
            writer.Write(nextEntryId++); // id
            writer.Write((short)0);      // time
            writer.Write(50);            // Duration
            writer.Write(0);             // Unk1
            writer.Write(1);             // Flags
            writer.Write(0f);            // Animation start frame
            writer.Write(0f);            // Animation end frame
            c010OffsetFieldPos = stream.Position;
            writer.Write(0);             // Path offset, backpatched
            writer.Write(0);             // Unk2
        }

        var c009Start = stream.Position;
        WriteItemHeader("C009", 0x18);
        entryIds.Add(nextEntryId);
        writer.Write(nextEntryId++); // id
        writer.Write((short)0);      // time
        writer.Write(50);            // Duration
        writer.Write(0);             // Unk1
        var c009OffsetFieldPos = stream.Position;
        writer.Write(0); // backpatched

        // C012 is only read back raw. Its string offset sits at +0x14 from the item start.
        var c012Start = 0L;
        var c012OffsetFieldPos = 0L;
        if (c012Path != null)
        {
            c012Start = stream.Position;
            WriteItemHeader("C012", 0x48);
            entryIds.Add(nextEntryId);
            writer.Write(nextEntryId++); // id
            writer.Write((short)0);      // time
            writer.Write(30);            // Duration
            writer.Write(0);             // Unk1
            c012OffsetFieldPos = stream.Position;
            writer.Write(0);             // Path offset, backpatched
            for (var i = 0x18; i < 0x48; i++)
                writer.Write((byte)0);   // rest of the payload
        }

        var actorIdsPos = stream.Position;
        writer.Write((short)2); // TMAC's id

        var trackIdsPos = stream.Position;
        writer.Write((short)3); // TMTR's id

        var entryIdsPos = stream.Position;
        foreach (var id in entryIds)
            writer.Write(id);

        long WriteString(string value)
        {
            var at = stream.Position;
            writer.Write(Encoding.UTF8.GetBytes(value));
            writer.Write((byte)0);
            return at;
        }

        var tmppStringPos = tmppName != null ? WriteString(tmppName) : 0L;
        var c010StringPos = c010Path != null ? WriteString(c010Path) : 0L;
        var c009StringPos = c009Path != null ? WriteString(c009Path) : 0L;
        var c012StringPos = c012Path != null ? WriteString(c012Path) : 0L;

        var endPos = stream.Position;

        // Offsets are relative to itemStart + 8.
        void Patch(long fieldPos, long itemStart, long targetPos)
        {
            stream.Position = fieldPos;
            writer.Write((int)(targetPos - (itemStart + 8)));
        }

        Patch(tmalOffsetFieldPos, tmalStart, actorIdsPos);
        Patch(tmacOffsetFieldPos, tmacStart, trackIdsPos);
        Patch(tmtrOffsetFieldPos, tmtrStart, entryIdsPos);

        if (tmppName != null)
            Patch(tmppOffsetFieldPos, tmppStart, tmppStringPos);
        if (c010Path != null)
            Patch(c010OffsetFieldPos, c010Start, c010StringPos);
        if (c009Path != null)
            Patch(c009OffsetFieldPos, c009Start, c009StringPos);
        if (c012Path != null)
            Patch(c012OffsetFieldPos, c012Start, c012StringPos);

        stream.Position = sizePos;
        writer.Write((int)endPos);

        stream.Position = endPos;
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildPap(IReadOnlyList<string> animationNames, IReadOnlyList<byte[]> tmbs)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        var count = animationNames.Count;

        writer.Write(PapMagic);
        writer.Write(0x00020001); // version
        writer.Write((short)count);
        writer.Write((short)0);   // model id
        writer.Write((byte)0);    // model type
        writer.Write((byte)0);    // variant

        var offsetsPos = stream.Position;
        writer.Write(0); // info offset placeholder
        writer.Write(0); // havok offset placeholder
        writer.Write(0); // footer (TMB) offset placeholder

        var infoPos = stream.Position;

        foreach (var animationName in animationNames)
        {
            var nameBytes = Encoding.UTF8.GetBytes(animationName);
            writer.Write(nameBytes);
            for (var i = 0; i < NameFieldLength - nameBytes.Length; i++)
                writer.Write((byte)0); // terminator + padding
            writer.Write((short)0); // type
            writer.Write((short)0); // havok index
            writer.Write(0);        // face animation flag
        }

        while (stream.Position % 4 != 0)
            writer.Write((byte)0); // keeps the havok and footer section 4-aligned

        var havokPos = stream.Position;
        var footerPos = stream.Position;

        for (var index = 0; index < count; index++)
        {
            writer.Write(tmbs[index]);

            var padding = index < count - 1 ? Pad4(stream.Position, (int)footerPos) : 0;
            for (var i = 0; i < padding; i++)
                writer.Write((byte)0);
        }

        var endPos = stream.Position;

        stream.Position = offsetsPos;
        writer.Write((int)infoPos);
        writer.Write((int)havokPos);
        writer.Write((int)footerPos);

        stream.Position = endPos;
        writer.Flush();
        return stream.ToArray();
    }

    // Mirrors PapFile: every gap but the last pads to 4 bytes, aligned to the TMB region's start.
    private static int Pad4(long position, int footerOffset)
    {
        var leftover = (position - footerOffset % 4) % 4;
        return (int)(leftover == 0 ? 0 : 4 - leftover);
    }

    private static byte[] BuildMalformed(string kind)
    {
        var pap = BuildPap(["cbbm_source"], [BuildTmb(c009Path: "cbbm_source")]);
        var footer = BitConverter.ToInt32(pap, FooterFieldOffset);

        switch (kind)
        {
            case "not-a-pap":
                return Encoding.ASCII.GetBytes("these bytes are anything but a pap file.");
            case "truncated-header":
                return pap.Take(12).ToArray();
            case "bad-tmb-magic":
                Encoding.ASCII.GetBytes("XMLB").CopyTo(pap, footer);
                return pap;
            case "second-item-unrecognized":
                Encoding.ASCII.GetBytes("XXAL").CopyTo(pap, footer + TmppInsertOffset);
                return pap;
            case "truncated-tmb":
                return pap.Take(footer + 16).ToArray();
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static int IndexOfMagic(byte[] data, string magic)
    {
        var pattern = Encoding.ASCII.GetBytes(magic);
        for (var i = 0; i <= data.Length - pattern.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length && match; j++)
                match = data[i + j] == pattern[j];
            if (match)
                return i;
        }
        return -1;
    }

    private static string ReadNulTerminated(byte[] data, int offset)
    {
        var end = offset;
        while (end < data.Length && data[end] != 0)
            end++;
        return Encoding.UTF8.GetString(data, offset, end - offset);
    }

    [Fact]
    public void Read_PapWithoutAnyTmpp_ReturnsEmpty()
    {
        var pap = BuildPap(["cbem_sp09_2lp"],
            [BuildTmb(c010Path: "cfxf_wrysmile", c009Path: "cbem_sp09_2lp", c012Path: AvfxPath)]);

        PapFaceLibrary.Read(pap).Should().BeEmpty("no embedded TMB declares a face library");
    }

    [Fact]
    public void Read_PapWithTmpp_ReturnsTheDeclaredLibrary()
    {
        var pap = BuildPap(["cbbm_dance01_loop"],
            [BuildTmb(tmppName: "smile", c010Path: "cfxf_smile", c009Path: "cbbm_dance01_loop")]);

        PapFaceLibrary.Read(pap).Should().Equal("smile");
    }

    [Fact]
    public void Read_TwoTmbsDeclaringTheSameLibrary_ReportsItOnce()
    {
        var pap = BuildPap(
            ["cbbm_dance01_start", "cbbm_dance01_loop"],
            [BuildTmb(tmppName: "smile", c009Path: "cbbm_dance01_start"),
             BuildTmb(tmppName: "smile", c009Path: "cbbm_dance01_loop")]);

        PapFaceLibrary.Read(pap).Should().Equal("smile");
    }

    [Theory]
    [InlineData("not-a-pap")]
    [InlineData("truncated-header")]
    [InlineData("bad-tmb-magic")]
    [InlineData("second-item-unrecognized")]
    [InlineData("truncated-tmb")]
    public void Read_MalformedBytes_ThrowsInvalidDataException(string kind)
    {
        var bytes = BuildMalformed(kind);

        var act = () => PapFaceLibrary.Read(bytes);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Inject_TmbWithoutTmpp_DeclaresTheLibraryAndStillParses()
    {
        var pap = BuildPap(["cbem_sp09_2lp"],
            [BuildTmb(c010Path: "cfxf_wrysmile", c009Path: "cbem_sp09_2lp", c012Path: AvfxPath)]);

        var result = PapFaceLibrary.Inject(pap, "wrysmile");

        PapFaceLibrary.Read(result).Should().Equal("wrysmile");
        PapAnimationNames.Read(result).Should().Equal("cbem_sp09_2lp");

        using var reader = new BinaryReader(new MemoryStream(result));
        var parsed = new PapFile(reader);
        parsed.Animations.Should().ContainSingle();
        parsed.Animations[0].GetName().Should().Be("cbem_sp09_2lp", "injection must not touch the animation entries");
        parsed.Animations[0].Tmb.HeaderTmpp.IsAssigned.Should().BeTrue("the existing TMB machinery must see the injected TMPP");
        parsed.Animations[0].Tmb.GetAllC009Entries().Should().ContainSingle()
            .Which.Path.Value.Should().Be("cbem_sp09_2lp", "the C009 path string must still resolve after the splice");
    }

    /// <summary>Pins the byte recipe VFXEditor writes: TMPP after TMDH, its name first in the string section, every string offset moved by the name's length.</summary>
    [Fact]
    public void Inject_SplicesExactlyTheRecipe_NoOtherByteMoves()
    {
        var tmb = BuildTmb(c010Path: "cfxf_wrysmile", c009Path: "cbem_sp09_2lp", c012Path: AvfxPath);
        var pap = BuildPap(["cbem_sp09_2lp"], [tmb]);

        var result = PapFaceLibrary.Inject(pap, "wrysmile");

        var footer = BitConverter.ToInt32(pap, FooterFieldOffset);
        var nameBytes = Encoding.UTF8.GetBytes("wrysmile");
        var shift = nameBytes.Length + 1;

        (string Magic, int Field)[] stringFields = [("C010", 0x20), ("C009", 0x14), ("C012", 0x14)];

        var stringStart = stringFields.Min(field =>
        {
            var item = IndexOfMagic(tmb, field.Magic);
            return item + 8 + BitConverter.ToInt32(tmb, item + field.Field);
        });

        var expectedTmb = tmb.Take(TmppInsertOffset)
            .Concat(Encoding.ASCII.GetBytes("TMPP"))
            .Concat(BitConverter.GetBytes(0x0C))
            .Concat(BitConverter.GetBytes(stringStart + 0x0C - (TmppInsertOffset + 8)))
            .Concat(tmb.Skip(TmppInsertOffset).Take(stringStart - TmppInsertOffset))
            .Concat(nameBytes)
            .Concat(new byte[] { 0 })
            .Concat(tmb.Skip(stringStart))
            .ToArray();

        BitConverter.GetBytes(tmb.Length + 0x0C + shift).CopyTo(expectedTmb, 4);
        BitConverter.GetBytes(BitConverter.ToInt32(tmb, 8) + 1).CopyTo(expectedTmb, 8);

        foreach (var (magic, field) in stringFields)
        {
            var item = IndexOfMagic(tmb, magic);
            BitConverter.GetBytes(BitConverter.ToInt32(tmb, item + field) + shift).CopyTo(expectedTmb, item + 0x0C + field);
        }

        var expected = pap.Take(footer).Concat(expectedTmb).ToArray();
        result.Should().Equal(expected);
    }

    /// <summary>A raw entry's string offset grows by exactly the name's length and still resolves to the same path.</summary>
    [Fact]
    public void Inject_RawEntryAfterTheInsertionPoint_StillReadsTheSameString()
    {
        var tmb = BuildTmb(c010Path: "cfxf_wrysmile", c009Path: "cbem_sp09_2lp", c012Path: AvfxPath);
        var pap = BuildPap(["cbem_sp09_2lp"], [tmb]);

        var result = PapFaceLibrary.Inject(pap, "wrysmile");

        var footer = BitConverter.ToInt32(pap, FooterFieldOffset);

        var c012InTmb = IndexOfMagic(tmb, "C012");
        c012InTmb.Should().BeGreaterThan(TmppInsertOffset, "the fixture only means anything with the raw entry after the insertion point");

        var c012InResult = IndexOfMagic(result, "C012");
        c012InResult.Should().Be(footer + c012InTmb + 12, "every item after the insertion point shifts by exactly the 12 injected bytes");

        var originalRelative = BitConverter.ToInt32(tmb, c012InTmb + 0x14);
        var resultRelative = BitConverter.ToInt32(result, c012InResult + 0x14);
        resultRelative.Should().Be(originalRelative + "wrysmile".Length + 1,
            "the entry moved 12 bytes and its string moved 12 bytes plus the name written ahead of it");

        ReadNulTerminated(result, c012InResult + 8 + resultRelative).Should().Be(AvfxPath);

        using var reader = new BinaryReader(new MemoryStream(result));
        new PapFile(reader).ToBytes().Should().Equal(result);
    }

    [Fact]
    public void Inject_TmbAlreadyDeclaringALibrary_ReturnsByteIdenticalBytes()
    {
        var pap = BuildPap(["cbbm_dance01_loop"],
            [BuildTmb(tmppName: "smile", c010Path: "cfxf_smile", c009Path: "cbbm_dance01_loop")]);

        var result = PapFaceLibrary.Inject(pap, "wrysmile");

        result.Should().Equal(pap, "a TMB that already declares a face library is left exactly as the animator wired it");
        result.Should().BeSameAs(pap, "nothing needs a library and the input is handed back without a copy");
    }

    [Fact]
    public void Inject_TmppPresentButNamingNothing_CountsAsPresentAndIsLeftAlone()
    {
        var pap = BuildPap(["cbbm_loop"], [BuildTmb(tmppName: "", c009Path: "cbbm_loop")]);

        PapFaceLibrary.Inject(pap, "wrysmile").Should().Equal(pap, "the injection gate is TMPP presence, not content");
        PapFaceLibrary.Read(pap).Should().BeEmpty("a face library naming nothing loads nothing worth reporting");
    }

    [Fact]
    public void Inject_MultiTmbPap_OnlyTheTmppLessTmbGainsOne()
    {
        var tmbWithout = BuildTmb(c010Path: "cfxf_wrysmile", c009Path: "cbbm_dance01_start", c012Path: AvfxPath);
        var tmbWith = BuildTmb(tmppName: "smile", c010Path: "cfxf_smile", c009Path: "cbbm_dance01_loop");
        var pap = BuildPap(["cbbm_dance01_start", "cbbm_dance01_loop"], [tmbWithout, tmbWith]);

        var result = PapFaceLibrary.Inject(pap, "wrysmile");

        PapFaceLibrary.Read(result).Should().Equal("wrysmile", "smile");

        // "wrysmile" grows the first TMB by an odd 21 bytes. The inter-TMB padding is recomputed.
        var footer = BitConverter.ToInt32(result, FooterFieldOffset);
        var firstSize = BitConverter.ToInt32(result, footer + 4);
        var secondStart = footer + firstSize + Pad4(footer + firstSize, footer);
        result.Skip(secondStart).ToArray().Should().Equal(tmbWith, "the TMB that already had a TMPP must survive byte for byte");

        using var reader = new BinaryReader(new MemoryStream(result));
        var parsed = new PapFile(reader);
        parsed.Animations.Should().HaveCount(2);
        parsed.Animations[0].Tmb.HeaderTmpp.IsAssigned.Should().BeTrue();
        parsed.Animations[1].Tmb.HeaderTmpp.IsAssigned.Should().BeTrue();
        PapAnimationNames.Read(result).Should().Equal("cbbm_dance01_start", "cbbm_dance01_loop");
    }

    [Fact]
    public void Inject_SubfolderLibraryName_RoundTrips()
    {
        var pap = BuildPap(["cbbm_emot_joy"], [BuildTmb(c009Path: "cbbm_emot_joy")]);

        var result = PapFaceLibrary.Inject(pap, "emot/joy");

        PapFaceLibrary.Read(result).Should().Equal("emot/joy");

        using var reader = new BinaryReader(new MemoryStream(result));
        new PapFile(reader).Animations.Should().ContainSingle();
    }

    [Theory]
    [InlineData("not-a-pap")]
    [InlineData("truncated-header")]
    [InlineData("bad-tmb-magic")]
    [InlineData("second-item-unrecognized")]
    [InlineData("truncated-tmb")]
    public void Inject_MalformedBytes_ThrowsInvalidDataException(string kind)
    {
        var bytes = BuildMalformed(kind);

        var act = () => PapFaceLibrary.Inject(bytes, "wrysmile");

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Inject_NeverMutatesItsInput_OnSuccessOrOnFailure()
    {
        var pap = BuildPap(["cbem_sp09_2lp"], [BuildTmb(c009Path: "cbem_sp09_2lp")]);
        var snapshot = pap.ToArray();

        PapFaceLibrary.Inject(pap, "wrysmile");
        pap.Should().Equal(snapshot, "injection must build a fresh array, not edit the caller's");

        var malformed = BuildMalformed("bad-tmb-magic");
        var malformedSnapshot = malformed.ToArray();

        var act = () => PapFaceLibrary.Inject(malformed, "wrysmile");

        act.Should().Throw<InvalidDataException>();
        malformed.Should().Equal(malformedSnapshot, "a refused input must be handed back untouched");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wry\0smile")]
    public void Inject_UnusableLibraryName_ThrowsArgumentException(string? name)
    {
        var pap = BuildPap(["cbem_sp09_2lp"], [BuildTmb(c009Path: "cbem_sp09_2lp")]);

        var act = () => PapFaceLibrary.Inject(pap, name!);

        act.Should().Throw<ArgumentException>();
    }
}
