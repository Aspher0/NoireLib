using FluentAssertions;
using NoireLib.Animations.PapFormat;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Builds minimal but structurally complete .paps entirely in memory, the same way
/// <see cref="PapRoundTripTests"/> does, then drives them through <see cref="PapRetargeter"/>. The builder below is
/// duplicated rather than shared because retargeting needs more than one animation per file to exercise
/// <see cref="PapSharing"/>'s suffix matching, which the round-trip builder (one animation only) cannot produce.
/// </summary>
public class PapRetargeterTests
{
    /// <summary> "pap " little-endian, the .pap container magic. </summary>
    private const int PapMagic = 0x20706170;

    /// <summary> Fixed name field width inside a .pap animation entry. </summary>
    private const int NameFieldLength = 32;

    /// <summary>
    /// A single-actor, single-track TMB: TMDH, TMAL (no TMPP), one TMAC, one TMTR, one C009 and, optionally, one
    /// C125 lock entry sharing that same track. IDs and offset-timelines are wired by hand the same way TmbWriter
    /// wires them: a body of fixed-size items followed by a trailing block of int16 ids that the offset fields
    /// point into. Structurally identical to <see cref="PapRoundTripTests"/>'s builder of the same name when both
    /// optional parameters are left at their defaults.
    /// </summary>
    /// <param name="includeAnimationLock">
    /// Whether to add a raw C125 entry (the lock <see cref="PapAnimationLock"/> looks for by magic alone) to the
    /// track, alongside the C009. A C125 is unparsed wire-format plumbing (read back as a generic raw entry), so
    /// only its magic and the id/time framing every entry carries matter here - no payload beyond that.
    /// </param>
    /// <param name="c009Path">
    /// When non-empty, the C009's path is written as a real offset string into a trailing string table (a valid,
    /// non-zero offset) instead of the default offset-0/empty-string shortcut, so a test can drive
    /// <c>TmbFile.ToBytes</c>'s string-table rebuild branch against an actual original string.
    /// </param>
    /// <param name="includeFace">
    /// Whether to add a C010 face-animation entry (duration 146, the parsed shape production stepdance TMBs
    /// carry) to the track alongside the C009 - the entry class whose unclamped duration kept a one-frame
    /// intro alive for its full length in game (2026-08-16).
    /// </param>
    private static byte[] BuildMinimalTmb(bool includeAnimationLock = false, string? c009Path = null,
        bool includeFace = false, bool includeFootstep = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        void WriteItemHeader(string magic, int size)
        {
            writer.Write(Encoding.ASCII.GetBytes(magic));
            writer.Write(size);
        }

        var entryCount = 1 + (includeAnimationLock ? 1 : 0) + (includeFace ? 1 : 0) + (includeFootstep ? 1 : 0);
        var itemCount  = 4 + entryCount; // TMDH, TMAL, TMAC, TMTR, C009, [C010], [C042], [C125]

        // TMLB
        writer.Write(Encoding.ASCII.GetBytes("TMLB"));
        var sizePos = stream.Position;
        writer.Write(0); // total size, backpatched once the trailing id block is written
        writer.Write(itemCount); // no TMPP - it is absent, not empty

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
        writer.Write(entryCount);
        writer.Write(0); // lua condition (unsupported, always 0)

        // C009 (id=4). Path offset is backpatched below: stays 0 (empty string, no jump) unless c009Path is given.
        var c009Start = stream.Position;
        WriteItemHeader("C009", 0x18);
        writer.Write((short)4); // id
        writer.Write((short)0); // time
        writer.Write(50);       // Duration
        writer.Write(0);        // Unk1
        var c009PathOffsetFieldPos = stream.Position;
        writer.Write(0); // Path offset placeholder

        // C010 (id=6), the face animation: parsed like C009 but with flags and start/end frames after the
        // duration; path offset stays 0 (empty string). Staged LATE (time 74) with a real playback segment
        // (end frame 40) on purpose - the production stepdance values whose unclamped time and segment kept
        // a "one-frame" intro alive in game (2026-08-16).
        if (includeFace)
        {
            WriteItemHeader("C010", 0x28);
            writer.Write((short)6);  // id
            writer.Write((short)74); // time (late-staged, the channel-stretching case)
            writer.Write(146);       // Duration (the production stepdance value)
            writer.Write(0);         // Unk1
            writer.Write(0);         // Flags
            writer.Write(0f);        // Animation Start Frame
            writer.Write(40f);       // Animation End Frame
            writer.Write(0);         // Path offset (empty)
            writer.Write(0);         // Unk2
        }

        // C042 (id=7), a footstep: raw, staged late, and its payload is four plain values with no string
        // offset among them, which is what makes it the one entry a clamp can drop.
        if (includeFootstep)
        {
            WriteItemHeader("C042", 0x1C);
            writer.Write((short)7);  // id
            writer.Write((short)60); // time
            writer.Write(1);
            writer.Write(0);
            writer.Write(35);
            writer.Write(0);
        }

        // C125 (id=5), the animation lock: a raw, unparsed entry, so magic plus the id/time framing every entry
        // carries is the whole thing - PapAnimationLock.HasLock/Remove only ever look at Magic.
        if (includeAnimationLock)
        {
            WriteItemHeader("C125", 0x0C);
            writer.Write((short)5); // id
            writer.Write((short)0); // time
        }

        // Trailing id block the three offset-timelines above point into.
        var actorIdsPos = stream.Position;
        writer.Write((short)2); // TMAC's id

        var trackIdsPos = stream.Position;
        writer.Write((short)3); // TMTR's id

        var entryIdsPos = stream.Position;
        writer.Write((short)4); // C009's id
        if (includeFace)
            writer.Write((short)6); // C010's id
        if (includeFootstep)
            writer.Write((short)7); // C042's id
        if (includeAnimationLock)
            writer.Write((short)5); // C125's id

        // String table: only written when c009Path is non-empty, so the default shape (offset 0 -> "", no table
        // at all) is exactly what it was before this parameter existed.
        var stringTablePos = stream.Position;
        if (!string.IsNullOrEmpty(c009Path))
        {
            writer.Write(Encoding.UTF8.GetBytes(c009Path));
            writer.Write((byte)0); // null terminator
        }

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

        if (!string.IsNullOrEmpty(c009Path))
            Patch(c009PathOffsetFieldPos, c009Start, stringTablePos);

        stream.Position = sizePos;
        writer.Write((int)endPos);

        stream.Position = endPos;
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// One .pap holding one TMB-backed animation per entry in <paramref name="animationNames"/> (paired by index
    /// with <paramref name="tmbs"/>), no havok payload. Generalizes <see cref="PapRoundTripTests"/>'s single-animation
    /// builder so a source file can offer more than one part for <see cref="PapSharing.Match"/> to route between.
    /// </summary>
    private static byte[] BuildMinimalPap(IReadOnlyList<string> animationNames, IReadOnlyList<byte[]> tmbs)
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

            var padding = CalculatePadding(stream.Position, index, count);
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
    /// Mirrors <c>PapFile</c>'s own private padding rule between consecutive TMBs: every gap but the last is padded
    /// out to a 4-byte boundary. Kept in lockstep with that rule so a multi-animation source file this builder
    /// produces parses the same way a real one would.
    /// </summary>
    private static int CalculatePadding(long position, int index, int total)
    {
        if (total > 1 && index < total - 1)
        {
            var leftover = position % 4;
            return leftover == 0 ? 0 : (int)(4 - leftover);
        }

        return 0;
    }

    [Fact]
    public void Retarget_SingleAnimation_RenamesAnimationAndRewritesC009Paths()
    {
        var source = BuildMinimalPap(["cbbm_source"], [BuildMinimalTmb()]);

        var result = PapRetargeter.Retarget(source, ["cbbm_target"], removeAnimationLock: false, out var locksRemoved);

        locksRemoved.Should().Be(0);

        using var reader = new BinaryReader(new MemoryStream(result));
        var pap = new PapFile(reader); // hkxTempPath omitted: a retargeter working on bytes must not require a temp file.

        pap.Animations.Should().HaveCount(1);
        pap.Animations[0].GetName().Should().Be("cbbm_target");

        var c009Entries = pap.Animations[0].Tmb.GetAllC009Entries();
        c009Entries.Should().ContainSingle();
        c009Entries[0].Path.Value.Should().Be("cbbm_target", "the C009 timeline entry must name the same animation the header now does");

        // The output round-trips cleanly: parsing it again and re-serializing neither throws nor loses the name.
        var bytes2 = pap.ToBytes();
        using var reader2 = new BinaryReader(new MemoryStream(bytes2));
        var pap2 = new PapFile(reader2);
        pap2.Animations.Should().HaveCount(1);
        pap2.Animations[0].GetName().Should().Be("cbbm_target");
    }

    /// <summary> A loop must take the loop, whatever order the emote lists its required names in. </summary>
    [Fact]
    public void Retarget_SuffixedRequiredName_FollowsTheSourceAnimationWithThatSuffix()
    {
        // Source order is the opposite of the required-name order below: index 0 ends "_loop", index 1 "_start".
        var source = BuildMinimalPap(
            ["cbbm_abc_loop", "cbbm_abc_start"],
            [BuildMinimalTmb(), BuildMinimalTmb()]);

        var result = PapRetargeter.Retarget(source, ["cbbm_xyz_start", "cbbm_xyz_loop"], removeAnimationLock: false, out _);

        using var reader = new BinaryReader(new MemoryStream(result));
        var pap = new PapFile(reader);

        pap.Animations.Should().HaveCount(2);
        pap.Animations[0].GetName().Should().Be("cbbm_xyz_loop",
            "PapSharing.Match routes by matching suffix first, so the source's own loop animation must take the new loop name");
        pap.Animations[1].GetName().Should().Be("cbbm_xyz_start");
    }

    /// <summary> Retargeting a file that cannot answer every name asked of it applies what it can and refuses nothing. </summary>
    [Fact]
    public void Retarget_MoreRequiredNamesThanAnimations_AppliesOnlyApplicableNamesAndDoesNotThrow()
    {
        var source = BuildMinimalPap(["cbbm_source"], [BuildMinimalTmb()]);

        var result = PapRetargeter.Retarget(source, ["cbbm_target_one", "cbbm_target_two"], removeAnimationLock: false, out var locksRemoved);

        locksRemoved.Should().Be(0);

        using var reader = new BinaryReader(new MemoryStream(result));
        var pap = new PapFile(reader);

        pap.Animations.Should().HaveCount(1, "a file with one animation still has exactly one afterwards: nothing is added or removed");
        pap.Animations[0].GetName().Should().Be("cbbm_target_one",
            "the sole animation takes the first required name once PapSharing.Match runs out of parts for the second");
    }

    // --- RetargetToNames: one output animation per required name, duplicating a source when it must answer
    //     more than one (the two-channel shared pap - a loop-only source serving an intro+loop target from
    //     ONE file, so the intro channel genuinely plays instead of resolving to nothing). ------------------

    /// <summary>
    /// A loop-only source (one animation) retargeted onto an intro+loop target must yield a TWO-animation pap:
    /// the source animation under the intro name AND under the loop name, so the game's intro channel finds a
    /// real animation. The C009 timeline of each output names its own animation.
    /// </summary>
    [Fact]
    public void RetargetToNames_LoopOnlySourceOntoIntroAndLoop_DuplicatesTheSourceUnderBothNames()
    {
        var source = BuildMinimalPap(["cbbm_src_loop"], [BuildMinimalTmb(c009Path: "cbbm_src_loop")]);

        var result = PapRetargeter.RetargetToNames(source, ["cbbm_tgt_start", "cbbm_tgt_loop"],
            removeAnimationLock: false, out var locksRemoved);

        locksRemoved.Should().Be(0);

        using var reader = new BinaryReader(new MemoryStream(result));
        var pap = new PapFile(reader);

        pap.Animations.Should().HaveCount(2, "one animation per required name, the single source duplicated to fill both");
        pap.Animations.ConvertAll(a => a.GetName()).Should().BeEquivalentTo(["cbbm_tgt_start", "cbbm_tgt_loop"]);

        foreach (var anim in pap.Animations)
        {
            var c009 = anim.Tmb.GetAllC009Entries();
            c009.Should().ContainSingle();
            c009[0].Path.Value.Should().Be(anim.GetName(), "each duplicated animation's C009 must name its own new animation");
        }

        // The output round-trips cleanly through an independent parser.
        PapAnimationNames.Read(result).Should().BeEquivalentTo(["cbbm_tgt_start", "cbbm_tgt_loop"]);
    }

    /// <summary>
    /// A source that already has both parts (two animations) maps each required name to its own distinct
    /// source by suffix - NO duplication, and the two outputs are backed by different source animations.
    /// </summary>
    [Fact]
    public void RetargetToNames_TwoPartSource_MapsEachNameToItsOwnSourceWithoutDuplicating()
    {
        var source = BuildMinimalPap(
            ["cbbm_src_start", "cbbm_src_loop"],
            [BuildMinimalTmb(c009Path: "cbbm_src_start"), BuildMinimalTmb(c009Path: "cbbm_src_loop")]);

        var result = PapRetargeter.RetargetToNames(source, ["cbbm_tgt_start", "cbbm_tgt_loop"],
            removeAnimationLock: false, out _);

        using var reader = new BinaryReader(new MemoryStream(result));
        var pap = new PapFile(reader);

        pap.Animations.Should().HaveCount(2);
        pap.Animations[0].GetName().Should().Be("cbbm_tgt_start");
        pap.Animations[1].GetName().Should().Be("cbbm_tgt_loop");
    }

    /// <summary> One required name is exactly the single-rename case, identical in shape to Retarget. </summary>
    [Fact]
    public void RetargetToNames_SingleName_ProducesOneRenamedAnimation()
    {
        var source = BuildMinimalPap(["cbbm_src_loop"], [BuildMinimalTmb(c009Path: "cbbm_src_loop")]);

        var result = PapRetargeter.RetargetToNames(source, ["cbbm_tgt_loop"], removeAnimationLock: false, out _);

        using var reader = new BinaryReader(new MemoryStream(result));
        var pap = new PapFile(reader);

        pap.Animations.Should().ContainSingle();
        pap.Animations[0].GetName().Should().Be("cbbm_tgt_loop");
    }

    /// <summary>
    /// A name listed in oneFrameWhenLentNames whose channel is served by a LENT duplicate (the same source
    /// animation also serves a name outside the set) gets its copy's timeline clamped to a single frame
    /// (TMDH length and every C009 duration), while the other outputs keep their original timing - the
    /// near-zero intro shape: the channel binds a real animation but ends almost immediately, so the loop
    /// takes over right away.
    /// </summary>
    [Fact]
    public void RetargetToNames_OneFrameWhenLentNames_ClampsTheLentDuplicate()
    {
        var source = BuildMinimalPap(["cbbm_src_loop"], [BuildMinimalTmb(c009Path: "cbbm_src_loop")]);

        var result = PapRetargeter.RetargetToNames(source, ["cbbm_tgt_start", "cbbm_tgt_loop"],
            removeAnimationLock: false, out _, oneFrameWhenLentNames: ["cbbm_tgt_start"]);

        using var reader = new BinaryReader(new MemoryStream(result));
        var pap = new PapFile(reader);

        pap.Animations.Should().HaveCount(2);

        var start = pap.Animations.Single(a => a.GetName() == "cbbm_tgt_start");
        start.Tmb.HeaderTmdh.GetLength().Should().Be(1);
        start.Tmb.GetAllC009Entries().Single().GetDuration().Should().Be(1);

        var loop = pap.Animations.Single(a => a.GetName() == "cbbm_tgt_loop");
        loop.Tmb.GetAllC009Entries().Single().GetDuration().Should().Be(50, "the un-clamped copy keeps its original timing");
    }

    /// <summary>
    /// The clamp covers the clips that hold a channel open, not just the C009 duration (the two 2026-08-16
    /// stepdance reports): a C010 face clip's own duration (146), its late START TIME (74 - the channel must
    /// live ~75 frames just to host it), and its playback segment (end frame 40 of animation played by a
    /// 1-duration clip) each kept a "one-frame" intro visibly long on their own. The untouched sibling keeps
    /// all of its timing.
    /// </summary>
    [Fact]
    public void RetargetToNames_OneFrameClamp_AlsoClampsFaceDurationsTimesAndSegments()
    {
        var source = BuildMinimalPap(["cbbm_src_loop"], [BuildMinimalTmb(c009Path: "cbbm_src_loop", includeFace: true)]);

        var result = PapRetargeter.RetargetToNames(source, ["cbbm_tgt_start", "cbbm_tgt_loop"],
            removeAnimationLock: false, out _, oneFrameWhenLentNames: ["cbbm_tgt_start"]);

        using var reader = new BinaryReader(new MemoryStream(result));
        var pap = new PapFile(reader);

        var start = pap.Animations.Single(a => a.GetName() == "cbbm_tgt_start");
        start.Tmb.HeaderTmdh.GetLength().Should().Be(1);
        start.Tmb.GetAllC009Entries().Single().GetDuration().Should().Be(1);

        var startFace = start.Tmb.AllEntries.OfType<NoireLib.Animations.PapFormat.Tmb.Entries.C010>().Single();
        startFace.GetDuration().Should().Be(1, "the face clip would otherwise keep the intro channel alive for its full length");
        startFace.GetTime().Should().Be(0, "a late-staged clip forces the channel to live long enough to host it");
        startFace.GetAnimationEnd().Should().Be(1f, "a 1-duration clip still plays its whole segment otherwise");

        var loop = pap.Animations.Single(a => a.GetName() == "cbbm_tgt_loop");
        var loopFace = loop.Tmb.AllEntries.OfType<NoireLib.Animations.PapFormat.Tmb.Entries.C010>().Single();
        loopFace.GetDuration().Should().Be(146, "the un-clamped copy keeps its original face timing");
        loopFace.GetTime().Should().Be(74);
        loopFace.GetAnimationEnd().Should().Be(40f);
    }

    /// <summary>
    /// A lent copy drops its footsteps. The clamp stages everything at frame 0 and a one-frame channel still
    /// fires what is staged on it, so a dance lent as an intro played its whole run of them in one go
    /// (2026-08-20). The sibling that keeps its own timing keeps its footsteps too.
    /// </summary>
    [Fact]
    public void RetargetToNames_OneFrameClamp_DropsTheFootstepsFromTheLentCopyOnly()
    {
        var source = BuildMinimalPap(["cbbm_src_loop"],
            [BuildMinimalTmb(c009Path: "cbbm_src_loop", includeFootstep: true)]);

        var result = PapRetargeter.RetargetToNames(source, ["cbbm_tgt_start", "cbbm_tgt_loop"],
            removeAnimationLock: false, out _, oneFrameWhenLentNames: ["cbbm_tgt_start"]);

        using var reader = new BinaryReader(new MemoryStream(result));
        var pap = new PapFile(reader);

        var start = pap.Animations.Single(a => a.GetName() == "cbbm_tgt_start");
        start.Tmb.AllEntries.Should().NotContain(entry => entry.Magic == "C042");
        start.Tmb.HeaderTmdh.GetLength().Should().Be(1);
        start.Tmb.GetAllC009Entries().Single().GetDuration().Should().Be(1);

        var loop = pap.Animations.Single(a => a.GetName() == "cbbm_tgt_loop");
        loop.Tmb.AllEntries.Should().ContainSingle(entry => entry.Magic == "C042");
        loop.Tmb.AllEntries.Single(entry => entry.Magic == "C042").GetTime().Should().Be(60);
    }

    /// <summary>
    /// The caller can learn WHICH names were actually clamped (the D56-skip decision rides on it: an output
    /// whose intro is full-length outlives the fade and needs no release wait, a clamped one does not).
    /// </summary>
    [Fact]
    public void RetargetToNames_ClampedNamesCollector_ReportsExactlyTheClampedNames()
    {
        var source = BuildMinimalPap(["cbbm_src_loop"], [BuildMinimalTmb(c009Path: "cbbm_src_loop")]);
        var clamped = new List<string>();

        PapRetargeter.RetargetToNames(source, ["cbbm_tgt_start", "cbbm_tgt_loop"],
            removeAnimationLock: false, out _, oneFrameWhenLentNames: ["cbbm_tgt_start"], clampedNames: clamped);

        clamped.Should().Equal("cbbm_tgt_start");
    }

    /// <summary> A marked name served by its own animation is not clamped and must not be reported as clamped. </summary>
    [Fact]
    public void RetargetToNames_ClampedNamesCollector_StaysEmptyWhenNothingWasClamped()
    {
        var source = BuildMinimalPap(
            ["cbbm_src_start", "cbbm_src_loop"],
            [BuildMinimalTmb(c009Path: "cbbm_src_start"), BuildMinimalTmb(c009Path: "cbbm_src_loop")]);
        var clamped = new List<string>();

        PapRetargeter.RetargetToNames(source, ["cbbm_tgt_start", "cbbm_tgt_loop"],
            removeAnimationLock: false, out _, oneFrameWhenLentNames: ["cbbm_tgt_start"], clampedNames: clamped);

        clamped.Should().BeEmpty();
    }

    /// <summary>
    /// The clamp is CONDITIONAL on the copy actually being lent (the 2026-08-16 /harvestdance report): music
    /// mods commonly redirect BOTH the intro and the loop game path to ONE single-animation file, so no
    /// path shape can tell a real intro from a borrowed loop - only content can. A name in
    /// oneFrameWhenLentNames whose suffix finds its OWN distinct source animation is real intro content and
    /// must keep its full-length timing; clamping it would cut a genuine intro to one frame.
    /// </summary>
    [Fact]
    public void RetargetToNames_MarkedNameWithItsOwnSourceAnimation_KeepsFullLength()
    {
        var source = BuildMinimalPap(
            ["cbbm_src_start", "cbbm_src_loop"],
            [BuildMinimalTmb(c009Path: "cbbm_src_start"), BuildMinimalTmb(c009Path: "cbbm_src_loop")]);

        var result = PapRetargeter.RetargetToNames(source, ["cbbm_tgt_start", "cbbm_tgt_loop"],
            removeAnimationLock: false, out _, oneFrameWhenLentNames: ["cbbm_tgt_start"]);

        using var reader = new BinaryReader(new MemoryStream(result));
        var pap = new PapFile(reader);

        var start = pap.Animations.Single(a => a.GetName() == "cbbm_tgt_start");
        start.Tmb.HeaderTmdh.GetLength().Should().Be(0, "the start channel has its own animation, so nothing is clamped");
        start.Tmb.GetAllC009Entries().Single().GetDuration().Should().Be(50,
            "a marked name served by its OWN source animation is real intro content, not a lent duplicate");
    }

    /// <summary>
    /// The clamp must NOT rebuild the timeline (in-game crash 2026-08-16, ffxiv ResourceGraph.FindResourceHandle
    /// via SoundManager.LoadSoundDataScd): a rebuild relocates the string section while RAW unknown-magic
    /// entries re-emit their payload verbatim, so an embedded sound entry's own path offset dangles and the
    /// game dereferences garbage (again on 2026-08-20, when the clamp dropped entries instead of retiming
    /// them). The clamp patches exactly TWO numeric fields in place on the preserved bytes - the TMDH length
    /// and the C009 duration - and every other byte, the raw C125 entry included, stays identical to the
    /// un-clamped output of the very same retarget.
    /// </summary>
    [Fact]
    public void RetargetToNames_OneFrameClamp_PatchesOnlyTheTwoDurationFieldsAndPreservesEveryOtherByte()
    {
        // The raw C125 stands in for any unknown-magic entry whose payload a rebuild would corrupt.
        var source = BuildMinimalPap(["cbbm_src_loop"], [BuildMinimalTmb(includeAnimationLock: true, c009Path: "cbbm_src_loop")]);

        var plain = PapRetargeter.RetargetToNames(source, ["cbbm_tgt_start", "cbbm_tgt_loop"],
            removeAnimationLock: false, out _);
        var clamped = PapRetargeter.RetargetToNames(source, ["cbbm_tgt_start", "cbbm_tgt_loop"],
            removeAnimationLock: false, out _, oneFrameWhenLentNames: ["cbbm_tgt_start"]);

        using var plainReader = new BinaryReader(new MemoryStream(plain));
        var plainPap = new PapFile(plainReader);
        using var clampedReader = new BinaryReader(new MemoryStream(clamped));
        var clampedPap = new PapFile(clampedReader);

        var plainStart = plainPap.Animations.Single(a => a.GetName() == "cbbm_tgt_start").GetTmbBytes();
        var clampedStart = clampedPap.Animations.Single(a => a.GetName() == "cbbm_tgt_start").GetTmbBytes();

        clampedStart.Length.Should().Be(plainStart.Length, "an in-place patch never changes the timeline's size");

        // TMDH Length is a short at TMB offset 24 (TMLB header 12 + TMDH header 8 + id 2 + Unk1 2); the C009
        // Duration is an int at offset 108 (TMDH 0x10, TMAL 0x10, TMAC 0x1C, TMTR 0x18 precede the C009,
        // whose header 8 + id 2 + time 2 put Duration 12 bytes in).
        var allowed = new[] { 24, 25, 108, 109, 110, 111 };
        var diffs = Enumerable.Range(0, plainStart.Length).Where(i => plainStart[i] != clampedStart[i]).ToList();
        diffs.Should().NotBeEmpty().And.BeSubsetOf(allowed);

        // The clamped values landed, and the raw lock entry survived byte-for-byte (covered by the diff set).
        var start = clampedPap.Animations.Single(a => a.GetName() == "cbbm_tgt_start");
        start.Tmb.HeaderTmdh.GetLength().Should().Be(1);
        start.Tmb.GetAllC009Entries().Single().GetDuration().Should().Be(1);
        PapAnimationLock.HasLock(start).Should().BeTrue();

        // The un-clamped sibling is untouched by the clamp of its neighbor.
        var plainLoop = plainPap.Animations.Single(a => a.GetName() == "cbbm_tgt_loop").GetTmbBytes();
        var clampedLoop = clampedPap.Animations.Single(a => a.GetName() == "cbbm_tgt_loop").GetTmbBytes();
        clampedLoop.Should().Equal(plainLoop);
    }

    /// <summary>
    /// Duplication still strips the animation lock on every produced copy - a stale C125 on a duplicated
    /// intro would re-lock the very channel this feature exists to make play.
    /// </summary>
    [Fact]
    public void RetargetToNames_RemoveLock_StripsLockFromEveryDuplicate()
    {
        var source = BuildMinimalPap(["cbbm_src_loop"], [BuildMinimalTmb(includeAnimationLock: true, c009Path: "cbbm_src_loop")]);

        var result = PapRetargeter.RetargetToNames(source, ["cbbm_tgt_start", "cbbm_tgt_loop"],
            removeAnimationLock: true, out var locksRemoved);

        locksRemoved.Should().Be(2, "both the in-place rename and the duplicate carry a lock that must go");

        using var reader = new BinaryReader(new MemoryStream(result));
        var pap = new PapFile(reader);

        pap.Animations.Should().HaveCount(2);
        foreach (var anim in pap.Animations)
            PapAnimationLock.HasLock(anim).Should().BeFalse();
    }

    /// <summary>
    /// The "a pap the game cannot parse takes the game down with it" verification pass is a no-op on a well-formed
    /// retarget: it must not throw, and an independent reader of the output must see exactly what was asked for.
    /// </summary>
    [Fact]
    public void Retarget_ValidInput_DoesNotThrowAndTheOutputBytesDeclareTheRequiredNames()
    {
        var source = BuildMinimalPap(
            ["cbbm_abc_start", "cbbm_abc_loop"],
            [BuildMinimalTmb(), BuildMinimalTmb()]);

        var result = PapRetargeter.Retarget(source, ["cbbm_xyz_start", "cbbm_xyz_loop"], removeAnimationLock: false, out var locksRemoved);

        locksRemoved.Should().Be(0);

        // PapAnimationNames.Read is a second, independent parser: using it here (rather than PapFile again) checks
        // the output bytes themselves declare the required names, not just that PapFile can make sense of them.
        var declaredNames = PapAnimationNames.Read(result);
        declaredNames.Should().BeEquivalentTo(["cbbm_xyz_start", "cbbm_xyz_loop"]);
    }

    /// <summary>
    /// removeAnimationLock strips every C125 lock entry, which invalidates the TMB's source layout and forces
    /// <c>TmbFile.ToBytes</c> down its full rebuild-from-model path (see <see cref="PapAnimationLock.Remove"/>) -
    /// a code path none of the tests above touch, since they all leave the lock alone. The rename still has to
    /// land correctly once the timeline is rebuilt, and the lock must actually be gone afterwards.
    /// </summary>
    [Fact]
    public void Retarget_RemoveAnimationLockTrue_StripsC125AndStillRewritesTheRename()
    {
        var source = BuildMinimalPap(["cbbm_source"], [BuildMinimalTmb(includeAnimationLock: true)]);

        var result = PapRetargeter.Retarget(source, ["cbbm_target"], removeAnimationLock: true, out var locksRemoved);

        locksRemoved.Should().Be(1);

        using var reader = new BinaryReader(new MemoryStream(result));
        var pap = new PapFile(reader);

        pap.Animations.Should().HaveCount(1);
        pap.Animations[0].GetName().Should().Be("cbbm_target");

        var c009Entries = pap.Animations[0].Tmb.GetAllC009Entries();
        c009Entries.Should().ContainSingle("rebuilding the timeline to drop the lock must not also drop or duplicate the C009");
        c009Entries[0].Path.Value.Should().Be("cbbm_target");

        PapAnimationLock.HasLock(pap.Animations[0]).Should().BeFalse(
            "removeAnimationLock must strip every C125 entry from the rebuilt timeline, not just report a count");
    }

    /// <summary>
    /// Every other test's source C009 uses the offset-0 empty-path shortcut, which never touches
    /// <c>TmbFile.ToBytes</c>'s string-table rebuild branch (<c>TryWritePreservedBytes</c> only rebuilds the string
    /// section when a changed path actually replaces a real original string). A source file authored normally
    /// always has its C009 pointing at a real string, so retargeting has to rewrite that string, not just the
    /// offset-0 case.
    /// </summary>
    [Fact]
    public void Retarget_SourceC009HasNonEmptyPath_RewritesPathThroughStringTableRebuild()
    {
        var source = BuildMinimalPap(["cbbm_source"], [BuildMinimalTmb(c009Path: "cbbm_source")]);

        var result = PapRetargeter.Retarget(source, ["cbbm_target"], removeAnimationLock: false, out var locksRemoved);

        locksRemoved.Should().Be(0);

        using var reader = new BinaryReader(new MemoryStream(result));
        var pap = new PapFile(reader);

        pap.Animations.Should().HaveCount(1);
        pap.Animations[0].GetName().Should().Be("cbbm_target");

        var c009Entries = pap.Animations[0].Tmb.GetAllC009Entries();
        c009Entries.Should().ContainSingle();
        c009Entries[0].Path.Value.Should().Be("cbbm_target",
            "the original non-empty path must be rewritten through the string-table rebuild, not left pointing at the old string");
    }

    // --- RenameInternalAnimations: rename an animation already retargeted onto a target's name to a
    //     content-unique INTERNAL name (landing 60), so the resident havok resource keyed by that internal
    //     name can no longer be reused across two different swap contents (the 59n residual stale). ----------

    /// <summary>
    /// Renames exactly the animations whose current header name is a key in the map, rewriting both the header
    /// name and the C009 timeline that repeats it, and leaves every other animation untouched. This is the
    /// endgame's half that changes the pap-INTERNAL name (the third key, distinct from the retarget name and
    /// the composed pap path) so a resident-by-name havok resource cannot dedup a new content onto an old one.
    /// </summary>
    [Fact]
    public void RenameInternalAnimations_RenamesMappedAnimations_LeavesOthersUntouched()
    {
        var source = BuildMinimalPap(
            ["cbem_loop_emot10_2lp", "cbem_start_emot10_2lp"],
            [BuildMinimalTmb(c009Path: "cbem_loop_emot10_2lp"), BuildMinimalTmb(c009Path: "cbem_start_emot10_2lp")]);

        var renames = new Dictionary<string, string> { ["cbem_loop_emot10_2lp"] = "bp0123456789_2lp" };

        var result = PapRetargeter.RenameInternalAnimations(source, renames);

        using var reader = new BinaryReader(new MemoryStream(result));
        var pap = new PapFile(reader);

        pap.Animations.Should().HaveCount(2);
        pap.Animations[0].GetName().Should().Be("bp0123456789_2lp", "the mapped animation's header name is the content-unique name");
        pap.Animations[0].Tmb.GetAllC009Entries()[0].Path.Value.Should().Be("bp0123456789_2lp",
            "the C009 timeline must name the same animation the header now does, or the game will not play it");
        pap.Animations[1].GetName().Should().Be("cbem_start_emot10_2lp", "an animation whose name is not in the map is left exactly as it was");

        // Independent parser confirms the new name is really declared.
        PapAnimationNames.Read(result).Should().Contain("bp0123456789_2lp");
    }

    /// <summary> A rename map that names nothing in the file changes nothing and still returns readable bytes. </summary>
    [Fact]
    public void RenameInternalAnimations_NoMatchingNames_ReturnsFileUnchangedInName()
    {
        var source = BuildMinimalPap(["cbem_loop_emot10_2lp"], [BuildMinimalTmb(c009Path: "cbem_loop_emot10_2lp")]);

        var result = PapRetargeter.RenameInternalAnimations(source,
            new Dictionary<string, string> { ["not_present"] = "bp_whatever_2lp" });

        PapAnimationNames.Read(result).Should().BeEquivalentTo(["cbem_loop_emot10_2lp"]);
    }

    /// <summary>
    /// The fail-open alias shape (landing 61, from the 05:38 in-game run): with
    /// <c>keepOriginalAsAlias: true</c> the renamed animation is JOINED by a clone that keeps the ORIGINAL
    /// name, sharing the same havok binding, so the pap answers WHICHEVER internal name the character's
    /// timeline asks for - the content-unique one when the redirected action tmb is served, the vanilla one
    /// when it is not (the 05:38 run: the tmb load was never redirected, the binder asked the vanilla name,
    /// the single-name pap could not answer, and the pack load retried forever with nothing on screen).
    /// </summary>
    [Fact]
    public void RenameInternalAnimations_KeepOriginalAsAlias_DeclaresBothNamesOnOneHavokBinding()
    {
        var source = BuildMinimalPap(["cbem_loop_emot10_2lp"], [BuildMinimalTmb(c009Path: "cbem_loop_emot10_2lp")]);

        var result = PapRetargeter.RenameInternalAnimations(source,
            new Dictionary<string, string> { ["cbem_loop_emot10_2lp"] = "bp0123456789_2lp" },
            keepOriginalAsAlias: true);

        using var reader = new BinaryReader(new MemoryStream(result));
        var pap = new PapFile(reader);

        pap.Animations.Should().HaveCount(2, "the renamed animation and its vanilla-named alias clone");
        pap.Animations.ConvertAll(a => a.GetName()).Should()
            .BeEquivalentTo(["bp0123456789_2lp", "cbem_loop_emot10_2lp"]);
        pap.Animations[0].HavokIndex.Should().Be(pap.Animations[1].HavokIndex,
            "the alias plays the SAME motion: one havok binding served under two names");

        foreach (var animation in pap.Animations)
        {
            animation.Tmb.GetAllC009Entries().Single().Path.Value.Should().Be(animation.GetName(),
                "each animation's C009 must name its own animation or the game will not play that channel");
        }
    }

    /// <summary> Alias mode with no matching name behaves exactly like the plain overload: nothing added, nothing renamed. </summary>
    [Fact]
    public void RenameInternalAnimations_KeepOriginalAsAlias_NoMatch_AddsNothing()
    {
        var source = BuildMinimalPap(["cbem_loop_emot10_2lp"], [BuildMinimalTmb(c009Path: "cbem_loop_emot10_2lp")]);

        var result = PapRetargeter.RenameInternalAnimations(source,
            new Dictionary<string, string> { ["not_present"] = "bp_whatever_2lp" },
            keepOriginalAsAlias: true);

        PapAnimationNames.Read(result).Should().BeEquivalentTo(["cbem_loop_emot10_2lp"]);
    }
}
