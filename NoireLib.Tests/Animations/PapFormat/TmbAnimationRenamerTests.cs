using FluentAssertions;
using NoireLib.Animations.PapFormat.Tmb;
using NoireLib.Animations.PapFormat.Tmb.Entries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Drives <see cref="TmbAnimationRenamer"/> against the REAL vanilla action timeline the landing-60 endgame
/// redirects - <c>chara/action/emote/loop_emot10_loop.tmb</c>, captured verbatim from the 2026-08-14 sqpack
/// (a 181-byte TMLB: TMDH, TMPP 'bossy', TMAL, TMAC, TMTR, and one C010 naming the pap-internal animation
/// 'cbem_loop_emot10_2lp'). Testing the real file rather than a synthetic one proves the preserved-bytes
/// string patch survives the exact layout the game ships, including the untouched TMPP face-library entry.
/// </summary>
public class TmbAnimationRenamerTests
{
    /// <summary> The real vanilla loop_emot10_loop.tmb, base64. Its sole C010 references 'cbem_loop_emot10_2lp'. </summary>
    private const string VanillaLoopEmot10LoopTmb =
        "VE1MQrUAAAAGAAAAVE1ESBAAAAABAAAANwADAFRNUFAMAAAAdgAAAFRNQUwQAAAAZAAAAAEAAABUTUFD" +
        "HAAAAAIAAAAAAAAAAAAAAFYAAAABAAAAVE1UUhgAAAADAAAAPAAAAAEAAAAAAAAAQzAxMCgAAAAEAAAA" +
        "NwAAAAAAAAAAAAAAAAAAAAAAXEIsAAAAAAAAAAIAAwAEAGJvc3N5AGNiZW1fbG9vcF9lbW90MTBfMmxwAA==";

    [Fact]
    public void Rename_RewritesTheC010AnimationReference_ToTheContentUniqueName()
    {
        var vanilla = Convert.FromBase64String(VanillaLoopEmot10LoopTmb);
        var renames = new Dictionary<string, string> { ["cbem_loop_emot10_2lp"] = "bp0123456789_2lp" };

        var result = TmbAnimationRenamer.Rename(vanilla, renames);

        var reparsed = new TmbFile(new BinaryReader(new MemoryStream(result)));
        var c010 = reparsed.AllEntries.OfType<C010>().Single();
        c010.Path.Value.Should().Be("bp0123456789_2lp",
            "the character reads this C010 to know which havok animation to bind; it must name the renamed pap animation");

        reparsed.HeaderTmpp.IsAssigned.Should().BeTrue("the untouched TMPP face-library entry ('bossy') must survive the rewrite");
    }

    [Fact]
    public void Rename_NoMatchingReference_ReturnsAStructurallyValidTmbUnchangedInReference()
    {
        var vanilla = Convert.FromBase64String(VanillaLoopEmot10LoopTmb);

        var result = TmbAnimationRenamer.Rename(vanilla,
            new Dictionary<string, string> { ["not_this_animation"] = "bp_nope_2lp" });

        var reparsed = new TmbFile(new BinaryReader(new MemoryStream(result)));
        reparsed.AllEntries.OfType<C010>().Single().Path.Value.Should().Be("cbem_loop_emot10_2lp",
            "a rename map that names no entry leaves the reference exactly as it was");
    }

    [Fact]
    public void ReadAnimationReferences_ReturnsTheC010AnimationName()
    {
        var vanilla = Convert.FromBase64String(VanillaLoopEmot10LoopTmb);

        TmbAnimationRenamer.ReadAnimationReferences(vanilla).Should().BeEquivalentTo(["cbem_loop_emot10_2lp"]);
    }

    [Fact]
    public void ReadAnimationReferences_UnreadableBytes_ReturnsEmpty()
        => TmbAnimationRenamer.ReadAnimationReferences([0, 1, 2, 3]).Should().BeEmpty();

    [Fact]
    public void Rename_ProducedBytesReReadAsAValidTmb()
    {
        var vanilla = Convert.FromBase64String(VanillaLoopEmot10LoopTmb);
        var renames = new Dictionary<string, string> { ["cbem_loop_emot10_2lp"] = "bpabcdef0123_2lp" };

        var result = TmbAnimationRenamer.Rename(vanilla, renames);

        // A second, independent parse must not throw: a tmb the game cannot read takes the game down with it.
        var act = () => new TmbFile(new BinaryReader(new MemoryStream(result)));
        act.Should().NotThrow();
    }
}
