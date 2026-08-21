using NoireLib.Animations.PapFormat.Tmb;
using NoireLib.Animations.PapFormat.Tmb.Entries;
using System;
using System.Collections.Generic;
using System.IO;

namespace NoireLib.Animations.PapFormat;

/// <summary>
/// Adds the timeline commands that bring a character's weapons into their hands for the length of an animation
/// and send them back to their stowed point at its end.
/// </summary>
/// <remarks>
/// These are the three commands the game's own draw and sheathe animations carry, with the same values, so the
/// weapon travels as it does when the game moves it. They drive the equipped object's attach point and its own
/// animation, never the character's stance.
/// </remarks>
public static class PapWeaponHold
{
    private const string LogPrefix = "[PapWeaponHold] ";

    /// <summary> Weapon Size, which the game sets alongside the position. </summary>
    private const string ScaleMagic = "C015";

    /// <summary> Weapon Position: which attach point of the .atch file the object hangs from. </summary>
    private const string PositionMagic = "C014";

    /// <summary> Summon Animation: the weapon timeline the object itself plays as it travels. </summary>
    private const string SummonMagic = "C031";

    private const int Enabled = 1;
    private const int ScaleDuration = 10;

    /// <summary> Object Position and ATCH Object Scale both read 1 in hand and 0 stowed. </summary>
    private const int InHand = 1;
    private const int Stowed = 0;

    /// <summary> WeaponTimeline rows: weapon/active as the draw animation uses it, and weapon/deactive. </summary>
    private const int DrawTimelineRow = 119;
    private const int SheatheTimelineRow = 7;

    /// <summary> Object Control: which equipped object a command addresses. </summary>
    private const int MainHand = 0;
    private const int OffHand = 1;

    private const short HoldFrame = 0;

    /// <summary>
    /// The entry magics a rewritten timeline is known to carry safely: those with a model of their own, those
    /// whose pointing fields <see cref="TmbFile.StringFieldOffsets"/> and
    /// <see cref="TmbFile.ExtraFloatFieldOffsets"/> describe, and those shown to point nowhere. A magic outside
    /// this set may name data by an offset nothing here knows to move.
    /// </summary>
    private static readonly HashSet<string> RewritableMagics = new(StringComparer.Ordinal)
    {
        "C009", "C010", "C012", "C042", ScaleMagic, PositionMagic, SummonMagic,
    };

    /// <summary>
    /// Returns a copy of a .pap whose every animation brings the character's weapons into their hands.
    /// </summary>
    /// <param name="papBytes">The .pap's bytes, never modified.</param>
    /// <param name="offHand">Whether the character carries a second weapon. A two-handed weapon has none.</param>
    /// <param name="stowAtEnd">
    /// Whether each animation sends the weapons back on its last frame. The commands are sticky, so leaving this
    /// off keeps them in hand once the animation ends.
    /// </param>
    /// <returns>The rewritten .pap's bytes, verified to read back.</returns>
    /// <exception cref="InvalidDataException">The produced bytes do not read back as a valid .pap.</exception>
    public static byte[] Apply(byte[] papBytes, bool offHand, bool stowAtEnd = true)
    {
        using var reader = new BinaryReader(new MemoryStream(papBytes));
        var pap = new PapFile(reader);

        var objects = new List<int> { MainHand };
        if (offHand)
            objects.Add(OffHand);

        var touched = false;

        foreach (var animation in pap.Animations)
        {
            if (animation.Tmb is not { } timeline || timeline.Actors.Count == 0)
                continue;

            if (UnrewritableMagic(timeline) is { } blocker)
            {
                NoireLogger.LogDebug($"'{animation.GetName()}' carries a {blocker} entry, which a rewritten "
                    + "timeline cannot be trusted to carry; its weapons are left where they are.", LogPrefix);

                return papBytes;
            }

            var actor = timeline.Actors[0];
            var length = timeline.HeaderTmdh.GetLength();

            foreach (var target in objects)
                AddCommands(timeline, actor, target, HoldFrame, held: true);

            if (stowAtEnd && length > HoldFrame + 1)
            {
                foreach (var target in objects)
                    AddCommands(timeline, actor, target, (short)(length - 1), held: false);
            }

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

    /// <summary> The first entry magic in a timeline that a rewrite cannot be trusted to carry, or null. </summary>
    private static string? UnrewritableMagic(TmbFile timeline)
    {
        foreach (var entry in timeline.AllEntries)
        {
            if (!RewritableMagics.Contains(entry.Magic))
                return entry.Magic;
        }

        return null;
    }

    private static void AddCommands(TmbFile timeline, Tmac actor, int objectControl, short frame, bool held)
    {
        var position = held ? InHand : Stowed;
        var timelineRow = held ? DrawTimelineRow : SheatheTimelineRow;

        AddCommand(timeline, actor, ScaleMagic, frame, [ScaleDuration, 0, position, objectControl]);
        AddCommand(timeline, actor, PositionMagic, frame, [Enabled, 0, position, objectControl]);
        AddCommand(timeline, actor, SummonMagic, frame, [Enabled, 0, (objectControl << 16) | timelineRow]);
    }

    // The game gives every one of these commands a track of its own, so this does too.
    private static void AddCommand(TmbFile timeline, Tmac actor, string magic, short frame, int[] payload)
    {
        var entry = new TmbEntryRaw(timeline, magic, frame, payload);
        var track = new Tmtr(timeline);

        track.Entries.Add(entry);
        actor.Tracks.Add(track);
        timeline.AllTracks.Add(track);
        timeline.AllEntries.Add(entry);
    }

    private static void Verify(byte[] papBytes)
    {
        try
        {
            using var reader = new BinaryReader(new MemoryStream(papBytes));
            var reread = new PapFile(reader);

            if (reread.Animations.Count == 0)
                throw new InvalidDataException("the rewritten .pap declares no animation.");
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException($"the rewritten .pap does not read back ({ex.Message}).", ex);
        }
    }
}
