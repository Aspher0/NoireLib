using NoireLib.Animations.PapFormat.Tmb;
using NoireLib.Animations.PapFormat.Tmb.Entries;
using System;
using System.Collections.Generic;
using System.IO;

namespace NoireLib.Animations.PapFormat;

/// <summary>
/// Adds the timeline commands that bring a character's weapons into their hands for the length of an animation
/// and send them back to their stowed point at its end. These commands drive the equipped object's attach point
/// and its own animation, never the character's stance.
/// </summary>
public static class PapWeaponHold
{
    private const string LogPrefix = "[PapWeaponHold] ";

    private const string ScaleMagic = "C015";

    // Weapon Position: the .atch attach point the object hangs from.
    private const string PositionMagic = "C014";

    // Summon Animation: the weapon timeline the object plays as it travels.
    private const string SummonMagic = "C031";

    private const int Enabled = 1;
    private const int ScaleDuration = 10;

    // Object Position and ATCH Object Scale read 1 in hand and 0 stowed.
    private const int InHand = 1;
    private const int Stowed = 0;

    // WeaponTimeline rows: weapon/active and weapon/deactive.
    private const int DrawTimelineRow = 119;
    private const int SheatheTimelineRow = 7;

    private const int MainHand = 0;
    private const int OffHand = 1;

    private const short HoldFrame = 0;

    // Anything that ends a timeline hands the weapons back, including an overlapping play.
    private const int ReassertInterval = 10;

    // A magic outside this set may point at data by an offset nothing here knows to move.
    private static readonly HashSet<string> RewritableMagics = new(StringComparer.Ordinal)
    {
        "C009", "C010", "C012", "C042", ScaleMagic, PositionMagic, SummonMagic,
    };

    /// <summary>Returns a copy of a .pap whose every animation brings the character's weapons into their hands.</summary>
    /// <param name="papBytes">The .pap's bytes, never modified.</param>
    /// <param name="offHand">Whether the character carries a second weapon.</param>
    /// <param name="stowAtEnd">Whether each animation stows the weapons on its last frame.</param>
    /// <param name="withTravel">Whether the weapon plays its own travel animation. An effect bound to a summon shows once per summon.</param>
    /// <returns>The rewritten .pap's bytes.</returns>
    /// <exception cref="InvalidDataException">The produced bytes do not read back as a valid .pap.</exception>
    public static byte[] Apply(byte[] papBytes, bool offHand, bool stowAtEnd = true, bool withTravel = false)
    {
        var pap = PapFile.FromBytes(papBytes);

        var objects = new List<int> { MainHand };
        if (offHand)
            objects.Add(OffHand);

        var touched = false;

        foreach (var animation in pap.Animations)
        {
            if (animation.Tmb is not { } timeline || timeline.Actors.Count == 0)
                continue;

            // An unrewritable animation leaves the others rewritten.
            if (UnrewritableMagic(timeline) is { } blocker)
            {
                NoireLogger.LogDebug($"'{animation.GetName()}' carries a {blocker} entry, which a rewritten "
                    + "timeline cannot be trusted to carry; its weapons are left where they are.", LogPrefix);

                continue;
            }

            HoldThrough(timeline, timeline.Actors[0], objects, stowAtEnd, withTravel);

            timeline.InvalidateSourceLayout();
            timeline.RefreshIds();
            touched = true;
        }

        if (!touched)
            return papBytes;

        var result = pap.ToBytes();
        Verify(result);

        return result;
    }

    private static string? UnrewritableMagic(TmbFile timeline)
    {
        foreach (var entry in timeline.AllEntries)
        {
            if (!RewritableMagics.Contains(entry.Magic))
                return entry.Magic;
        }

        return null;
    }

    // Each command gets its own track, like the game's own draw animation.
    private static void HoldThrough(TmbFile timeline, Tmac actor, IReadOnlyList<int> objects, bool stowAtEnd,
        bool withTravel)
    {
        var length = timeline.HeaderTmdh.GetLength();
        var lastFrame = (short)(length - 1);
        var stows = stowAtEnd && lastFrame > HoldFrame;

        foreach (var objectControl in objects)
        {
            var scale = AddTrack(timeline, actor);
            var position = AddTrack(timeline, actor);

            foreach (var frame in HoldFrames(length))
            {
                AddCommand(timeline, scale, ScaleMagic, frame, [ScaleDuration, 0, InHand, objectControl]);
                AddCommand(timeline, position, PositionMagic, frame, [Enabled, 0, InHand, objectControl]);
            }

            if (stows)
            {
                AddCommand(timeline, scale, ScaleMagic, lastFrame, [ScaleDuration, 0, Stowed, objectControl]);
                AddCommand(timeline, position, PositionMagic, lastFrame, [Enabled, 0, Stowed, objectControl]);
            }

            // Repeating the travel on the beat would draw the weapon over and over.
            if (!withTravel)
                continue;

            var summon = AddTrack(timeline, actor);

            AddCommand(timeline, summon, SummonMagic, HoldFrame,
                [Enabled, 0, (objectControl << 16) | DrawTimelineRow]);

            if (stows)
            {
                AddCommand(timeline, summon, SummonMagic, lastFrame,
                    [Enabled, 0, (objectControl << 16) | SheatheTimelineRow]);
            }
        }
    }

    private static IEnumerable<short> HoldFrames(short length)
    {
        yield return HoldFrame;

        for (var frame = HoldFrame + ReassertInterval; frame < length - 1; frame += ReassertInterval)
            yield return (short)frame;
    }

    private static Tmtr AddTrack(TmbFile timeline, Tmac actor)
    {
        var track = new Tmtr(timeline);

        actor.Tracks.Add(track);
        timeline.AllTracks.Add(track);

        return track;
    }

    private static void AddCommand(TmbFile timeline, Tmtr track, string magic, short frame, int[] payload)
    {
        var entry = new TmbEntryRaw(timeline, magic, frame, payload);

        track.Entries.Add(entry);
        timeline.AllEntries.Add(entry);
    }

    private static void Verify(byte[] papBytes)
    {
        try
        {
            var reread = PapFile.FromBytes(papBytes);

            if (reread.Animations.Count == 0)
                throw new InvalidDataException("the rewritten .pap declares no animation.");
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException($"the rewritten .pap does not read back ({ex.Message}).", ex);
        }
    }
}
