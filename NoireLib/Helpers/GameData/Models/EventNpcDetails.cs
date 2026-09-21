namespace NoireLib.Helpers;

/// <summary>What the ENpcBase and ENpcResident sheets say about an event NPC beyond its name.</summary>
/// <param name="Invisibility">The sheet's invisibility value, zero for a visible NPC.</param>
/// <param name="IsImportant">The sheet's important flag.</param>
/// <param name="Scale">The model scale.</param>
/// <param name="ModelCharaId">The ModelChara row, zero for a human NPC built from customisation.</param>
/// <param name="NpcEquipId">The NpcEquip row, zero when the equipment is listed inline.</param>
/// <param name="RaceId">The Race row.</param>
/// <param name="TribeId">The Tribe row.</param>
/// <param name="Gender">The gender byte, zero for male and one for female.</param>
/// <param name="BalloonId">The Balloon row of the speech bubble it shows, zero for none.</param>
/// <param name="ResidentMap">The ENpcResident map byte.</param>
public sealed record EventNpcDetails(
    byte Invisibility,
    bool IsImportant,
    float Scale,
    uint ModelCharaId,
    uint NpcEquipId,
    uint RaceId,
    uint TribeId,
    byte Gender,
    uint BalloonId,
    byte ResidentMap);
