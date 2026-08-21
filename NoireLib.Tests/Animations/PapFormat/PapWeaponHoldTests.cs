using FluentAssertions;
using NoireLib.Animations.PapFormat;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Adding the weapon commands restructures a timeline, so the whole thing is written out again rather than
/// patched in place. The .pap built here carries a C012 with a vfx path and four float curves, since a rewrite
/// that loses track of what an entry points at hands the game a file it cannot read.
/// </summary>
public class PapWeaponHoldTests
{
    private const int PapMagic = 0x20706170;
    private const int NameFieldLength = 32;
    private const string VfxPath = "vfx/emote_sp/emt_sp085/eff/emt_sp085_c0c.avfx";

    // The curve values the C012 below carries, distinct so a block that lands on another one is visible.
    private static readonly float[] Scale = [1f, 2f, 3f];
    private static readonly float[] Rotation = [4f, 5f, 6f];
    private static readonly float[] Position = [7f, 8f, 9f];
    private static readonly float[] Colour = [10f, 11f, 12f, 13f];
    private const short TimelineLength = 60;

    // The hold is stated on the first frame and then every ten, so a sixty frame timeline states it six times.
    private const int HoldStatements = 6;

    /// <summary>
    /// One actor, one track, two entries: a C009 with no path and a C012 pointing into the string table.
    /// Offsets are wired by hand exactly as TmbWriter wires them.
    /// </summary>
    private static byte[] BuildTmb(string entryMagic = "C012")
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        void WriteItemHeader(string magic, int size)
        {
            writer.Write(Encoding.ASCII.GetBytes(magic));
            writer.Write(size);
        }

        writer.Write(Encoding.ASCII.GetBytes("TMLB"));
        var sizePos = stream.Position;
        writer.Write(0);
        writer.Write(6); // TMDH, TMAL, TMAC, TMTR, C009, C012

        WriteItemHeader("TMDH", 0x10);
        writer.Write((short)1); // id
        writer.Write((short)0); // Unk1
        writer.Write(TimelineLength);
        writer.Write((short)0); // Unk3

        var tmalStart = stream.Position;
        WriteItemHeader("TMAL", 0x10);
        var tmalOffsetFieldPos = stream.Position;
        writer.Write(0);
        writer.Write(1); // 1 actor

        var tmacStart = stream.Position;
        WriteItemHeader("TMAC", 0x1C);
        writer.Write((short)2); // id
        writer.Write((short)0); // time
        writer.Write(0);        // AbilityDelay
        writer.Write(0);        // Unk2
        var tmacOffsetFieldPos = stream.Position;
        writer.Write(0);
        writer.Write(1); // 1 track

        var tmtrStart = stream.Position;
        WriteItemHeader("TMTR", 0x18);
        writer.Write((short)3); // id
        writer.Write((short)0); // time
        var tmtrOffsetFieldPos = stream.Position;
        writer.Write(0);
        writer.Write(2); // 2 entries
        writer.Write(0); // lua condition

        WriteItemHeader("C009", 0x18);
        writer.Write((short)4); // id
        writer.Write((short)0); // time
        writer.Write(50);       // Duration
        writer.Write(0);        // Unk1
        writer.Write(0);        // Path offset 0 reads back as no string

        // A full-size C012: its path offset sits 0x14 into the item, and four (offset, count) pairs at 0x20,
        // 0x28, 0x30 and 0x38 name its animated scale, rotation, position and rgba curves.
        var c012Start = stream.Position;
        WriteItemHeader(entryMagic, 0x48);
        writer.Write((short)5); // id
        writer.Write((short)0); // time
        writer.Write(30);       // Duration
        writer.Write(0);        // Unknown
        var c012PathFieldPos = stream.Position;
        writer.Write(0);
        writer.Write(-65535);   // bind point
        writer.Write(-65534);   // bind point
        var c012ScalePos = stream.Position;
        writer.Write(0);
        writer.Write(3);
        var c012RotationPos = stream.Position;
        writer.Write(0);
        writer.Write(3);
        var c012PositionPos = stream.Position;
        writer.Write(0);
        writer.Write(3);
        var c012ColourPos = stream.Position;
        writer.Write(0);
        writer.Write(4);
        writer.Write(3);        // visibility
        writer.Write(0);        // Unknown

        // The extra section, which the curves point into and which a rebuild relocates.
        var scaleDataPos = stream.Position;
        foreach (var value in Scale) writer.Write(value);
        var rotationDataPos = stream.Position;
        foreach (var value in Rotation) writer.Write(value);
        var positionDataPos = stream.Position;
        foreach (var value in Position) writer.Write(value);
        var colourDataPos = stream.Position;
        foreach (var value in Colour) writer.Write(value);

        var actorIdsPos = stream.Position;
        writer.Write((short)2);

        var trackIdsPos = stream.Position;
        writer.Write((short)3);

        var entryIdsPos = stream.Position;
        writer.Write((short)4);
        writer.Write((short)5);

        var stringsPos = stream.Position;
        writer.Write(Encoding.UTF8.GetBytes(VfxPath));
        writer.Write((byte)0);

        var endPos = stream.Position;

        void Patch(long fieldPos, long itemStart, long targetPos)
        {
            stream.Position = fieldPos;
            writer.Write((int)(targetPos - (itemStart + 8)));
        }

        Patch(tmalOffsetFieldPos, tmalStart, actorIdsPos);
        Patch(tmacOffsetFieldPos, tmacStart, trackIdsPos);
        Patch(tmtrOffsetFieldPos, tmtrStart, entryIdsPos);
        Patch(c012PathFieldPos, c012Start, stringsPos);
        Patch(c012ScalePos, c012Start, scaleDataPos);
        Patch(c012RotationPos, c012Start, rotationDataPos);
        Patch(c012PositionPos, c012Start, positionDataPos);
        Patch(c012ColourPos, c012Start, colourDataPos);

        stream.Position = sizePos;
        writer.Write((int)endPos);

        stream.Position = endPos;
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildPap(byte[] tmb) => BuildPap([("cbbm_emot09", tmb)]);

    private static byte[] BuildPap(IReadOnlyList<(string Name, byte[] Tmb)> animations)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(PapMagic);
        writer.Write(0x00020001);
        writer.Write((short)animations.Count);
        writer.Write((short)101);
        writer.Write((byte)0);
        writer.Write((byte)0);

        var offsetsPos = stream.Position;
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        var infoPos = stream.Position;

        foreach (var (name, _) in animations)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            writer.Write(nameBytes);
            for (var i = 0; i < NameFieldLength - nameBytes.Length; i++)
                writer.Write((byte)0);
            writer.Write((short)0);
            writer.Write((short)0);
            writer.Write(0);
        }

        while (stream.Position % 4 != 0)
            writer.Write((byte)0);

        var havokPos = stream.Position;
        var footerPos = stream.Position;

        for (var index = 0; index < animations.Count; index++)
        {
            writer.Write(animations[index].Tmb);

            // Every timeline but the last is padded up to the four-byte grid the footer starts on.
            if (index == animations.Count - 1)
                continue;

            while ((stream.Position - footerPos % 4) % 4 != 0)
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

    private static PapFile Reread(byte[] papBytes)
    {
        using var reader = new BinaryReader(new MemoryStream(papBytes));
        return new PapFile(reader);
    }

    private static byte[] Held(bool offHand = false, bool stowAtEnd = true, bool withTravel = false)
        => PapWeaponHold.Apply(BuildPap(BuildTmb()), offHand, stowAtEnd, withTravel);

    [Fact]
    public void Apply_TheResultStillReadsBackAsAPap()
        => Reread(Held()).Animations.Should().ContainSingle()
            .Which.GetName().Should().Be("cbbm_emot09");

    [Fact]
    public void Apply_OneWeapon_AddsThePositionCommandForTheMainHandOnly()
    {
        var entries = Reread(Held()).Animations[0].Tmb.AllEntries;

        // Object Control 0 is the main hand: held on the beat, then sent back on the last frame.
        entries.Count(entry => entry.Magic == "C014").Should().Be(HoldStatements + 1);
        entries.Count(entry => entry.Magic == "C015").Should().Be(HoldStatements + 1);
    }

    // Anything that ends a timeline hands the weapons back, and an overlapping play lands that while this one
    // is still running, so the hold is stated on a beat rather than at one chosen frame.
    [Fact]
    public void Apply_StatesTheHoldOnABeatAcrossTheWholeAnimation()
    {
        var times = Reread(Held(stowAtEnd: false)).Animations[0].Tmb.AllEntries
            .Where(entry => entry.Magic == "C014").Select(entry => entry.GetTime()).ToList();

        times.Should().Equal([0, 10, 20, 30, 40, 50]);
    }

    [Fact]
    public void Apply_DoesNotSummonUnlessTravelIsAskedFor()
        => Reread(Held()).Animations[0].Tmb.AllEntries
            .Count(entry => entry.Magic == "C031").Should().Be(0,
                "the travel command summons the object it animates, and an effect in the file can bind itself to a summon");

    [Fact]
    public void Apply_WithTravel_SummonsOncePerMoment()
        => Reread(Held(withTravel: true)).Animations[0].Tmb.AllEntries
            .Count(entry => entry.Magic == "C031").Should().Be(2);

    // The repeats take the weapons back when an overlapping play stows them early, so they carry no travel
    // command; one flourish per repeat would be seen.
    [Fact]
    public void Apply_WithTravel_TheRepeatsDoNotTravel()
    {
        var entries = Reread(Held(withTravel: true)).Animations[0].Tmb.AllEntries;

        entries.Count(entry => entry.Magic == "C014").Should().Be(HoldStatements + 1);
        entries.Count(entry => entry.Magic == "C031").Should().Be(2);
    }

    [Fact]
    public void Apply_ASecondWeapon_IsAddressedAsWell()
        => Reread(Held(offHand: true)).Animations[0].Tmb.AllEntries
            .Count(entry => entry.Magic == "C014").Should().Be((HoldStatements + 1) * 2);

    [Fact]
    public void Apply_WithoutStowing_OnlyHolds()
        => Reread(Held(stowAtEnd: false)).Animations[0].Tmb.AllEntries
            .Count(entry => entry.Magic == "C014").Should().Be(HoldStatements);

    [Fact]
    public void Apply_GivesEveryCommandAChannelOfItsOwn()
    {
        var timeline = Reread(Held()).Animations[0].Tmb;

        // The one track the file came with, plus the size and the position channel for the one weapon.
        timeline.AllTracks.Should().HaveCount(1 + 2);
    }

    [Fact]
    public void Apply_KeepsTheEntriesTheFileAlreadyHad()
    {
        var magics = Reread(Held()).Animations[0].Tmb.AllEntries.Select(entry => entry.Magic).ToList();

        magics.Should().Contain("C009");
        magics.Should().Contain("C012");
    }

    [Fact]
    public void Apply_KeepsAnUnmodelledEntrysStringPointingAtIt()
        => ReadC012Path(Held()).Should().Be(VfxPath,
            "the rewrite relocates the string table, so an entry with no model still has to point at its own string");

    [Fact]
    public void Apply_KeepsAnEntryWithNoStringReadingAsNone()
        => Reread(Held()).Animations[0].Tmb.GetAllC009Entries()
            .Should().ContainSingle().Which.Path.Value.Should().BeEmpty();

    private static string ReadC012Path(byte[] papBytes)
    {
        var footer = System.BitConverter.ToInt32(papBytes, 22);
        var path = string.Empty;

        WalkItems(papBytes, footer, (magic, offset, size) =>
        {
            if (magic != "C012")
                return;

            var relative = System.BitConverter.ToInt32(papBytes, footer + offset + 0x14);
            var start = footer + offset + 8 + relative;
            var end = start;

            while (end < papBytes.Length && papBytes[end] != 0)
                end++;

            path = Encoding.UTF8.GetString(papBytes, start, end - start);
        });

        return path;
    }

    private static void WalkItems(byte[] papBytes, int footer, System.Action<string, int, int> visit)
    {
        var count = System.BitConverter.ToInt32(papBytes, footer + 8);
        var offset = 12;

        for (var index = 0; index < count; index++)
        {
            var magic = Encoding.ASCII.GetString(papBytes, footer + offset, 4);
            var size = System.BitConverter.ToInt32(papBytes, footer + offset + 4);

            visit(magic, offset, size);
            offset += size;
        }
    }

    // C012 names four float curves by offset, and the rewrite moves the extra section they live in.
    [Fact]
    public void Apply_KeepsAnUnmodelledEntrysFloatCurvesPointingAtThem()
    {
        var held = Held();
        var footer = System.BitConverter.ToInt32(held, 22);
        var curves = new List<float[]>();

        WalkItems(held, footer, (magic, offset, size) =>
        {
            if (magic != "C012")
                return;

            foreach (var field in new[] { 0x20, 0x28, 0x30, 0x38 })
            {
                var relative = System.BitConverter.ToInt32(held, footer + offset + field);
                var count = System.BitConverter.ToInt32(held, footer + offset + field + 4);
                var start = footer + offset + 8 + relative;
                var values = new float[count];

                for (var index = 0; index < count; index++)
                    values[index] = System.BitConverter.ToSingle(held, start + index * 4);

                curves.Add(values);
            }
        });

        curves.Should().HaveCount(4);
        curves[0].Should().Equal(Scale);
        curves[1].Should().Equal(Rotation);
        curves[2].Should().Equal(Position);
        curves[3].Should().Equal(Colour);
    }

    [Fact]
    public void Apply_AnEntryOfAnUnknownKind_LeavesTheFileAlone()
    {
        var source = BuildPap(BuildTmb(entryMagic: "C199"));

        PapWeaponHold.Apply(source, offHand: false).Should().Equal(source,
            "a rewrite could move data an unknown entry names by an offset nothing here knows about");
    }

    [Fact]
    public void Apply_AnEntryOfAnUnknownKind_LeavesOnlyItsOwnAnimationAlone()
    {
        var source = BuildPap([("cbbm_emot09", BuildTmb()), ("cbbm_emot10", BuildTmb(entryMagic: "C199"))]);

        var held = Reread(PapWeaponHold.Apply(source, offHand: false));

        held.Animations.Should().HaveCount(2);
        held.Animations[0].Tmb.AllEntries.Count(entry => entry.Magic == "C014").Should().Be(HoldStatements + 1,
            "one animation a rewrite cannot be trusted with is no reason to leave the rest of the file unheld");
        held.Animations[1].Tmb.AllEntries.Count(entry => entry.Magic == "C014").Should().Be(0);
    }
}
