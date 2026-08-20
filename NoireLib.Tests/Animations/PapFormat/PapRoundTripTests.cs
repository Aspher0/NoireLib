using FluentAssertions;
using NoireLib.Animations.PapFormat;
using System.IO;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Builds a minimal but structurally complete .pap (one animation, one C009 timeline entry) entirely in memory and
/// pushes it through PapFile twice: parse, serialize, parse again. Nothing here touches disk — <see cref="PapFile"/>
/// is constructed with no hkxTempPath, which is the whole point: a mover/retargeter working on bytes in memory must
/// not be forced to own a temp file just to read a .pap.
/// </summary>
public class PapRoundTripTests
{
    /// <summary> "pap " little-endian, the .pap container magic. </summary>
    private const int PapMagic = 0x20706170;

    /// <summary> Fixed name field width inside a .pap animation entry. </summary>
    private const int NameFieldLength = 32;

    /// <summary>
    /// A single-actor, single-track, single-entry TMB: TMDH, TMAL (no TMPP), one TMAC, one TMTR, one C009. IDs and
    /// offset-timelines are wired by hand the same way TmbWriter wires them: a body of fixed-size items followed by
    /// a trailing block of int16 ids that the offset fields point into.
    /// </summary>
    private static byte[] BuildMinimalTmb()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        void WriteItemHeader(string magic, int size)
        {
            writer.Write(Encoding.ASCII.GetBytes(magic));
            writer.Write(size);
        }

        // TMLB
        writer.Write(Encoding.ASCII.GetBytes("TMLB"));
        var sizePos = stream.Position;
        writer.Write(0); // total size, backpatched once the trailing id block is written
        writer.Write(5); // item count: TMDH, TMAL, TMAC, TMTR, C009 (no TMPP - it is absent, not empty)

        // TMDH (id=1; nothing looks it up, so any id is fine)
        WriteItemHeader("TMDH", 0x10);
        writer.Write((short)1); // id
        writer.Write((short)0); // Unk1
        writer.Write((short)0); // Length
        writer.Write((short)0); // Unk3

        // TMAL: no TMPP is written before it, so Tmpp's magic peek falls through to TMAL and rewinds.
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
        writer.Write(1); // 1 entry
        writer.Write(0); // lua condition (unsupported, always 0)

        // C009 (id=4), empty path: an offset of 0 reads back as "", no string table needed.
        WriteItemHeader("C009", 0x18);
        writer.Write((short)4); // id
        writer.Write((short)0); // time
        writer.Write(50);       // Duration
        writer.Write(0);        // Unk1
        writer.Write(0);        // Path offset = 0 -> empty string, no jump

        // Trailing id block the three offset-timelines above point into.
        var actorIdsPos = stream.Position;
        writer.Write((short)2); // TMAC's id

        var trackIdsPos = stream.Position;
        writer.Write((short)3); // TMTR's id

        var entryIdsPos = stream.Position;
        writer.Write((short)4); // C009's id

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

        stream.Position = sizePos;
        writer.Write((int)endPos);

        stream.Position = endPos;
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary> One animation named <paramref name="animationName"/>, its TMB embedded with no havok payload. </summary>
    private static byte[] BuildMinimalPap(string animationName, byte[] tmb)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(PapMagic);
        writer.Write(0x00020001); // version
        writer.Write((short)1);   // 1 animation
        writer.Write((short)0);   // model id
        writer.Write((byte)0);    // model type
        writer.Write((byte)0);    // variant

        var offsetsPos = stream.Position;
        writer.Write(0); // info offset, backpatched below
        writer.Write(0); // havok offset, backpatched below
        writer.Write(0); // footer (TMB) offset, backpatched below

        var infoPos = stream.Position;

        var nameBytes = Encoding.UTF8.GetBytes(animationName);
        writer.Write(nameBytes);
        for (var i = 0; i < NameFieldLength - nameBytes.Length; i++)
            writer.Write((byte)0); // terminator + padding
        writer.Write((short)0); // type
        writer.Write((short)0); // havok index
        writer.Write(0);        // face animation flag

        while (stream.Position % 4 != 0)
            writer.Write((byte)0); // post-animation padding, keeps the havok/footer section 4-aligned

        var havokPos = stream.Position; // no havok payload: havok and footer coincide
        var footerPos = stream.Position;

        writer.Write(tmb);

        var endPos = stream.Position;

        stream.Position = offsetsPos;
        writer.Write((int)infoPos);
        writer.Write((int)havokPos);
        writer.Write((int)footerPos);

        stream.Position = endPos;
        writer.Flush();
        return stream.ToArray();
    }

    [Fact]
    public void RoundTrip_ParseSerializeReparse_PreservesNameAndProducesStableBytes()
    {
        var original = BuildMinimalPap("cbbm_test", BuildMinimalTmb());

        using var reader1 = new BinaryReader(new MemoryStream(original));
        var pap1 = new PapFile(reader1); // hkxTempPath omitted: nothing to dump to, and nothing should be dumped.

        pap1.Animations.Should().HaveCount(1);
        pap1.Animations[0].GetName().Should().Be("cbbm_test");
        pap1.Animations[0].Tmb.GetAllC009Entries().Should().HaveCount(1);

        var bytes1 = pap1.ToBytes();

        using var reader2 = new BinaryReader(new MemoryStream(bytes1));
        var pap2 = new PapFile(reader2);

        pap2.Animations.Should().HaveCount(1);
        pap2.Animations[0].GetName().Should().Be("cbbm_test", "the name must survive a full parse/serialize/parse cycle");
        pap2.Animations[0].Tmb.GetAllC009Entries().Should().HaveCount(1);

        var bytes2 = pap2.ToBytes();

        bytes2.Length.Should().Be(bytes1.Length, "re-serializing an already-round-tripped file must not drift in size");
        bytes2.Should().Equal(bytes1, "a second pass over stable input must be a fixed point");
    }
}
