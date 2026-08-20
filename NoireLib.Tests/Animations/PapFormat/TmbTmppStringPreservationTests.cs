using FluentAssertions;
using NoireLib.Animations.PapFormat.Tmb;
using NoireLib.Animations.PapFormat.Tmb.Entries;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Pins the landing-61 fix for the in-game 05:38 failure: <see cref="TmbFile.ToBytes"/>'s preserved-bytes
/// string rebuild did not track the TMPP face-library string, so a C009/C010 rename whose rebuild truncated
/// the string section at a point BEFORE the TMPP string left the TMPP offset dangling past the end of the
/// file - the re-read then died in Tmpp with an EndOfStreamException and the whole rename was refused
/// (slump/conduct in the 05:38 log). The vanilla files happened to store the TMPP string FIRST, before any
/// C009/C010 string, which is the only reason every earlier rename survived: the truncation point always fell
/// after it. This builder deliberately writes the C009 string BEFORE the TMPP string, so the truncation
/// orphans TMPP exactly as the failing paps did.
/// </summary>
public class TmbTmppStringPreservationTests
{
    /// <summary>
    /// A single-actor, single-track TMB WITH a TMPP whose face-library string sits AFTER the C009 string in
    /// the string table. Structure mirrors PapRetargeterTests' builder (same item shapes, same trailing id
    /// block wiring), plus the TMPP between TMDH and TMAL where the game writes it.
    /// </summary>
    private static byte[] BuildTmbWithTmppStringAfterC009String(string c009Path, string tmppPath)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        void WriteItemHeader(string magic, int size)
        {
            writer.Write(Encoding.ASCII.GetBytes(magic));
            writer.Write(size);
        }

        // TMLB: TMDH, TMPP, TMAL, TMAC, TMTR, C009.
        writer.Write(Encoding.ASCII.GetBytes("TMLB"));
        var sizePos = stream.Position;
        writer.Write(0); // total size, backpatched
        writer.Write(6); // item count, TMPP included

        // TMDH (id=1)
        WriteItemHeader("TMDH", 0x10);
        writer.Write((short)1); // id
        writer.Write((short)0); // Unk1
        writer.Write((short)0); // Length
        writer.Write((short)0); // Unk3

        // TMPP: magic + size + face library path offset (backpatched below).
        var tmppStart = stream.Position;
        WriteItemHeader("TMPP", 0x0C);
        var tmppOffsetFieldPos = stream.Position;
        writer.Write(0); // path offset placeholder

        // TMAL
        var tmalStart = stream.Position;
        WriteItemHeader("TMAL", 0x10);
        var tmalOffsetFieldPos = stream.Position;
        writer.Write(0); // offset to the actor-id block
        writer.Write(1); // 1 actor

        // TMAC (id=2)
        var tmacStart = stream.Position;
        WriteItemHeader("TMAC", 0x1C);
        writer.Write((short)2); // id
        writer.Write((short)0); // time
        writer.Write(0);        // AbilityDelay
        writer.Write(0);        // Unk2
        var tmacOffsetFieldPos = stream.Position;
        writer.Write(0); // offset to the track-id block
        writer.Write(1); // 1 track

        // TMTR (id=3)
        var tmtrStart = stream.Position;
        WriteItemHeader("TMTR", 0x18);
        writer.Write((short)3); // id
        writer.Write((short)0); // time
        var tmtrOffsetFieldPos = stream.Position;
        writer.Write(0); // offset to the entry-id block
        writer.Write(1); // 1 entry
        writer.Write(0); // lua condition

        // C009 (id=4)
        var c009Start = stream.Position;
        WriteItemHeader("C009", 0x18);
        writer.Write((short)4); // id
        writer.Write((short)0); // time
        writer.Write(50);       // Duration
        writer.Write(0);        // Unk1
        var c009PathOffsetFieldPos = stream.Position;
        writer.Write(0); // Path offset placeholder

        // Trailing id block.
        var actorIdsPos = stream.Position;
        writer.Write((short)2);
        var trackIdsPos = stream.Position;
        writer.Write((short)3);
        var entryIdsPos = stream.Position;
        writer.Write((short)4);

        // String table: the C009 string FIRST, the TMPP string AFTER it - the order that orphans TMPP when
        // the rebuild truncates at the first tracked string.
        var c009StringPos = stream.Position;
        writer.Write(Encoding.UTF8.GetBytes(c009Path));
        writer.Write((byte)0);
        var tmppStringPos = stream.Position;
        writer.Write(Encoding.UTF8.GetBytes(tmppPath));
        writer.Write((byte)0);

        var endPos = stream.Position;

        void Patch(long fieldPos, long itemStart, long targetPos)
        {
            stream.Position = fieldPos;
            writer.Write((int)(targetPos - (itemStart + 8)));
        }

        Patch(tmppOffsetFieldPos, tmppStart, tmppStringPos);
        Patch(tmalOffsetFieldPos, tmalStart, actorIdsPos);
        Patch(tmacOffsetFieldPos, tmacStart, trackIdsPos);
        Patch(tmtrOffsetFieldPos, tmtrStart, entryIdsPos);
        Patch(c009PathOffsetFieldPos, c009Start, c009StringPos);

        stream.Position = sizePos;
        writer.Write((int)endPos);

        stream.Position = endPos;
        writer.Flush();
        return stream.ToArray();
    }

    [Fact]
    public void RenamingC009_KeepsTheTmppFaceLibraryString_WhenItSitsAfterTheC009String()
    {
        var source = BuildTmbWithTmppStringAfterC009String("cbem_old_name_2lp", "bossy");

        var file = new TmbFile(new BinaryReader(new MemoryStream(source)));
        file.HeaderTmpp.IsAssigned.Should().BeTrue("the builder writes a real TMPP");

        // A different-length rename forces the string-table rebuild, the branch that truncated TMPP away.
        file.GetAllC009Entries().Single().Path.Value = "bp0123456789_2lp";

        var rewritten = file.ToBytes();

        // The re-read is what died in game (EndOfStreamException inside Tmpp): it must parse cleanly and
        // still carry both the new C009 name and the untouched face library path.
        var reparsed = new TmbFile(new BinaryReader(new MemoryStream(rewritten)));
        reparsed.GetAllC009Entries().Single().Path.Value.Should().Be("bp0123456789_2lp");
        reparsed.HeaderTmpp.IsAssigned.Should().BeTrue("the TMPP header must survive the string rebuild");
    }

    [Fact]
    public void UnchangedFile_RoundTripsVerbatim_TmppIncluded()
    {
        var source = BuildTmbWithTmppStringAfterC009String("cbem_old_name_2lp", "bossy");

        var file = new TmbFile(new BinaryReader(new MemoryStream(source)));

        file.ToBytes().Should().Equal(source, "an unmodified timeline must reproduce its original bytes verbatim");
    }
}
