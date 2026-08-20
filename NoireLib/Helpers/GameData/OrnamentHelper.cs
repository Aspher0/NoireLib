using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using NoireLib.Enums;

namespace NoireLib.Helpers;

/// <summary>
/// Fashion accessories: which one a character has out, and what carrying it does to the emotes they can play.
/// </summary>
public static class OrnamentHelper
{
    /// <summary>An accessory owning a pose cycle of its own, such as a parasol or the Shovel.</summary>
    public const byte PoseCycleKind = 1;

    /// <summary>An accessory worn passively, such as wings, spectacles and packs.</summary>
    public const byte WornKind = 2;

    /// <summary>
    /// The kind of accessory a character has out, from the <c>Ornament</c> column Lumina calls <c>Unknown4</c>:
    /// 0 is a hand-held prop, 1 owns a pose cycle, 2 is worn passively, 3 is the Gatling Gun.
    /// </summary>
    /// <param name="character">The character to read.</param>
    /// <returns>The kind, or null when nothing is out or the row cannot be read.</returns>
    public static unsafe byte? GetOrnamentKind(ICharacter character)
        => TryGetOrnamentRow(character, out var row) ? row.Unknown4 : null;

    /// <summary>The name of the accessory a character has out.</summary>
    /// <param name="character">The character to read.</param>
    /// <returns>The singular name, or null when nothing is out.</returns>
    public static unsafe string? GetOrnamentName(ICharacter character)
        => TryGetOrnamentRow(character, out var row) ? row.Singular.ExtractText() : null;

    /// <summary>
    /// The state carrying an accessory of a given kind puts a character in. The game reports one
    /// <c>PoseType.Accessory</c> for every accessory, so the kind is the only thing separating them.
    /// </summary>
    /// <param name="ornamentKind">The kind, as <see cref="GetOrnamentKind"/> returns it.</param>
    /// <returns>The emote condition the accessory imposes.</returns>
    public static EmoteCondition ConditionForOrnamentKind(byte ornamentKind) => ornamentKind switch
    {
        PoseCycleKind => EmoteCondition.HoldingUmbrella,
        WornKind => EmoteCondition.WearingFashionAccessory,
        _ => EmoteCondition.HoldingTorch,
    };

    /// <summary>Reads the <c>Ornament</c> row for whatever a character has out.</summary>
    /// <param name="character">The character to read.</param>
    /// <param name="row">The row, when one was found.</param>
    /// <returns>False when nothing is out or the row cannot be read.</returns>
    private static unsafe bool TryGetOrnamentRow(ICharacter character, out Lumina.Excel.Sheets.Ornament row)
    {
        row = default;

        if (character == null || character.Address == 0)
            return false;

        var ornamentId = ((Character*)character.Address)->OrnamentData.OrnamentId;

        if (ornamentId == 0)
            return false;

        // Fully qualified because FFXIVClientStructs declares an Ornament of its own.
        if (!ExcelSheetHelper.TryGetRow<Lumina.Excel.Sheets.Ornament>(ornamentId, out var found) || !found.HasValue)
            return false;

        row = found.Value;
        return true;
    }
}
