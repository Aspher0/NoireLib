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
/// Builds minimal but structurally complete .paps entirely in memory, the same way <see cref="PapRetargeterTests"/>
/// does (the builder is deliberately duplicated per test file, not shared), then drives them through
/// <see cref="PapFaceLibrary"/>. The fixtures mirror the two vanilla shapes the face-preservation investigation
/// dumped from the game: a charmed-style TMB (C010 face entry, C009 body entry, and a raw C012 vfx entry carrying
/// its own offset string, no TMPP) and a balldance-style TMB (TMPP first, then the same entries). The C012 is the
/// load-bearing fixture: a raw entry AFTER the TMPP insertion point whose string offset must survive injection
/// untouched, which is exactly the guarantee the surgical splice exists to give and a parse-and-rebuild would break.
/// </summary>
public class PapFaceLibraryTests
{
    /// <summary> "pap " little-endian, the .pap container magic. </summary>
    private const int PapMagic = 0x20706170;

    /// <summary> Fixed name field width inside a .pap animation entry. </summary>
    private const int NameFieldLength = 32;

    /// <summary> Offset of the footer field (where the embedded TMB region starts) inside a .pap header. </summary>
    private const int FooterFieldOffset = 22;

    /// <summary> Where an injected TMPP lands inside a TMB: right after the 12-byte TMLB header and the 0x10-byte TMDH. </summary>
    private const int TmppInsertOffset = 0x1C;

    /// <summary> A realistic avfx path for the raw C012 fixture entry; the exact value only has to survive unchanged. </summary>
    private const string AvfxPath = "vfx/common/eff/dk05th_stup0t.avfx";

    /// <summary>
    /// A single-actor, single-track TMB: TMDH, optional TMPP, TMAL, one TMAC, one TMTR, then an optional C010 (face
    /// play entry), a C009 (always present, like in every real pap TMB) and an optional raw C012 sharing that track.
    /// IDs and offset-timelines are wired by hand the same way TmbWriter wires them: a body of fixed-size items, a
    /// trailing block of int16 ids the offset fields point into, then the string section (TMPP's string first, the
    /// way vanilla files store it).
    /// </summary>
    /// <param name="tmppName">
    /// When non-null, a TMPP header item naming this face library is written between TMDH and TMAL. An empty string
    /// still writes the TMPP with a real offset to an empty string, so presence and content can be tested apart.
    /// </param>
    /// <param name="c010Path"> When non-null, a C010 entry (Flags=1, the face play form) naming this animation. </param>
    /// <param name="c009Path"> When non-null, the C009's path as a real offset string; when null, the offset-0 empty-string shortcut. </param>
    /// <param name="c012Path">
    /// When non-null, a raw C012 vfx entry whose string offset field sits at +0x14 (matching TmbFile's C012
    /// string-reference table) and points at this path in the trailing string section.
    /// </param>
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

        // TMLB
        writer.Write(Encoding.ASCII.GetBytes("TMLB"));
        var sizePos = stream.Position;
        writer.Write(0); // total size, backpatched once everything below is written
        writer.Write(itemCount);

        // TMDH (id=1; nothing looks it up, so any id is fine)
        WriteItemHeader("TMDH", 0x10);
        writer.Write((short)1); // id
        writer.Write((short)0); // Unk1
        writer.Write((short)0); // Length
        writer.Write((short)0); // Unk3

        // TMPP: magic, size, one offset string; no id or time. Sits between TMDH and TMAL when present.
        var tmppStart = stream.Position;
        var tmppOffsetFieldPos = 0L;
        if (tmppName != null)
        {
            WriteItemHeader("TMPP", 0x0C);
            tmppOffsetFieldPos = stream.Position;
            writer.Write(0); // face library path offset, backpatched below
        }

        // TMAL
        var tmalStart = stream.Position;
        WriteItemHeader("TMAL", 0x10);
        var tmalOffsetFieldPos = stream.Position;
        writer.Write(0); // offset to the actor-id block, backpatched below
        writer.Write(1); // 1 actor

        // TMAC (id=2)
        var tmacStart = stream.Position;
        WriteItemHeader("TMAC", 0x1C);
        writer.Write((short)2); // id
        writer.Write((short)0); // time
        writer.Write(0);        // AbilityDelay
        writer.Write(0);        // Unk2
        var tmacOffsetFieldPos = stream.Position;
        writer.Write(0); // offset to the track-id block, backpatched below
        writer.Write(1); // 1 track

        // TMTR (id=3)
        var tmtrStart = stream.Position;
        WriteItemHeader("TMTR", 0x18);
        writer.Write((short)3); // id
        writer.Write((short)0); // time
        var tmtrOffsetFieldPos = stream.Position;
        writer.Write(0); // offset to the entry-id block, backpatched below
        writer.Write(entryCount);
        writer.Write(0); // lua condition (unsupported, always 0)

        short nextEntryId = 4;
        var entryIds = new List<short>();

        // C010, the face play entry (Flags=1 inside a pap TMB names a cfxf_* animation from the loaded library).
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
            writer.Write(0);             // Path offset, backpatched below
            writer.Write(0);             // Unk2
        }

        // C009, the body animation entry.
        var c009Start = stream.Position;
        WriteItemHeader("C009", 0x18);
        entryIds.Add(nextEntryId);
        writer.Write(nextEntryId++); // id
        writer.Write((short)0);      // time
        writer.Write(50);            // Duration
        writer.Write(0);             // Unk1
        var c009OffsetFieldPos = stream.Position;
        writer.Write(0); // Path offset, backpatched below when a real path was asked for

        // C012, a vfx entry NoireLib only ever reads back raw (magic, size, id, time, then an opaque payload). Its
        // string offset field sits at +0x14 from the item start; everything past it is zero-filled padding here.
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
            writer.Write(0);             // Path offset, backpatched below
            for (var i = 0x18; i < 0x48; i++)
                writer.Write((byte)0);   // rest of the payload
        }

        // Trailing id block the offset-timelines above point into.
        var actorIdsPos = stream.Position;
        writer.Write((short)2); // TMAC's id

        var trackIdsPos = stream.Position;
        writer.Write((short)3); // TMTR's id

        var entryIdsPos = stream.Position;
        foreach (var id in entryIds)
            writer.Write(id);

        // String section, TMPP's string first the way vanilla stores it.
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

        // Offsets are relative to (itemStart + 8), i.e. right after that item's own magic+size.
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

    /// <summary>
    /// One .pap holding one TMB-backed animation per entry in <paramref name="animationNames"/> (paired by index
    /// with <paramref name="tmbs"/>), no havok payload; the same builder <see cref="PapRetargeterTests"/> uses.
    /// </summary>
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
            writer.Write((byte)0); // post-animation padding, keeps the havok/footer section 4-aligned

        var havokPos = stream.Position; // no havok payload: havok and footer coincide
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

    /// <summary>
    /// Mirrors <c>PapFile</c>'s own private inter-TMB padding rule: every gap but the last is padded out to a 4-byte
    /// boundary, measured against the TMB region's own alignment rather than absolute zero.
    /// </summary>
    private static int Pad4(long position, int footerOffset)
    {
        var leftover = (position - footerOffset % 4) % 4;
        return (int)(leftover == 0 ? 0 : 4 - leftover);
    }

    /// <summary> A structurally broken variant of a valid single-animation pap, one break per <paramref name="kind"/>. </summary>
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

    /// <summary> First index where the four ASCII bytes of <paramref name="magic"/> appear, or -1. </summary>
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

    /// <summary> The NUL-terminated UTF-8 string starting at <paramref name="offset"/>. </summary>
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

    /// <summary>
    /// Pins the byte recipe itself: 12-byte TMPP right after TMDH, name appended at the very end, item count +1,
    /// size += 12 + name + NUL, and NOTHING else. The expected bytes are assembled here independently, so any
    /// deviation (rebuilt string section, shifted item, rewritten offset) fails on the exact byte that moved.
    /// </summary>
    [Fact]
    public void Inject_SplicesExactlyTheRecipe_NoOtherByteMoves()
    {
        var tmb = BuildTmb(c010Path: "cfxf_wrysmile", c009Path: "cbem_sp09_2lp", c012Path: AvfxPath);
        var pap = BuildPap(["cbem_sp09_2lp"], [tmb]);

        var result = PapFaceLibrary.Inject(pap, "wrysmile");

        var footer = BitConverter.ToInt32(pap, FooterFieldOffset);
        var nameBytes = Encoding.UTF8.GetBytes("wrysmile");

        var expectedTmb = tmb.Take(TmppInsertOffset)
            .Concat(Encoding.ASCII.GetBytes("TMPP"))
            .Concat(BitConverter.GetBytes(0x0C))
            .Concat(BitConverter.GetBytes(tmb.Length + 0x0C - (TmppInsertOffset + 8)))
            .Concat(tmb.Skip(TmppInsertOffset))
            .Concat(nameBytes)
            .Concat(new byte[] { 0 })
            .ToArray();

        // The only two fields the growth rewrites in place: the TMB's total size and its item count.
        BitConverter.GetBytes(tmb.Length + 0x0C + nameBytes.Length + 1).CopyTo(expectedTmb, 4);
        BitConverter.GetBytes(BitConverter.ToInt32(tmb, 8) + 1).CopyTo(expectedTmb, 8);

        var expected = pap.Take(footer).Concat(expectedTmb).ToArray();
        result.Should().Equal(expected);
    }

    /// <summary>
    /// THE regression the surgical splice exists to prevent: a raw entry (here a C012 vfx entry, which NoireLib
    /// never parses beyond magic and size) keeps a string offset relative to its own position. Inserting 12 bytes
    /// before it moves the entry and its string by the same amount, so the stored relative offset must come out of
    /// injection bit-identical and still resolve to the same path. A parse-and-rebuild re-emits raw entries with
    /// stale internal offsets and breaks exactly this.
    /// </summary>
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
        resultRelative.Should().Be(originalRelative, "an offset whose base and target both moved 12 bytes must not be rewritten");

        ReadNulTerminated(result, c012InResult + 8 + resultRelative).Should().Be(AvfxPath);

        // And the existing machinery hands those exact bytes back: parse + re-serialize is byte-stable.
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
        result.Should().NotBeSameAs(pap, "the injection is a pure function returning a fresh array");
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

        // The second TMB must be byte-identical even though the first one grew in front of it; "wrysmile" grew the
        // first TMB by 21 bytes, an odd amount, so the inter-TMB padding had to be recomputed for that to hold.
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
