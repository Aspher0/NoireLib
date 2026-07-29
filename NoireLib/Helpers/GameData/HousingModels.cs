using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// What a housing interior territory is, read straight from the game's <c>HousingIndoorTerritory</c> sheet rather
/// than inferred from its name or level file. Every residential district has exactly one territory of each kind;
/// the three estate kinds are the plot sizes (large=mansion, medium=house, small=cottage).<br/>
/// Values are the sheet's own: a kind added later reads as unknown rather than being silently misfiled.
/// </summary>
public enum HousingInteriorKind : byte
{
    /// <summary>A small plot's interior.</summary>
    Cottage = 0,

    /// <summary>A medium plot's interior.</summary>
    House = 1,

    /// <summary>A large plot's interior.</summary>
    Mansion = 2,

    /// <summary>The private chambers reached from inside an estate, one room per resident.</summary>
    PrivateChambers = 3,

    /// <summary>The Free Company workshop reached from inside a company estate.</summary>
    CompanyWorkshop = 4,

    /// <summary>An apartment room inside an apartment building.</summary>
    Apartment = 5,

    /// <summary>The apartment building's ground floor, entered from the district and leading to the rooms.</summary>
    ApartmentLobby = 255,
}

/// <summary>Shorthands over <see cref="HousingInteriorKind"/> for the groupings housing code is written in terms of.</summary>
public static class HousingInteriorKinds
{
    /// <summary>
    /// Whether the kind is a plot's own interior, being the three sizes a plot can be built at. These are the
    /// interiors reached from a plot door in the district rather than from inside another interior.
    /// </summary>
    /// <param name="kind">The housing kind.</param>
    /// <returns>True for a cottage, house, or mansion.</returns>
    public static bool IsEstate(this HousingInteriorKind kind)
        => kind is HousingInteriorKind.Cottage or HousingInteriorKind.House or HousingInteriorKind.Mansion;

    /// <summary>Whether the kind belongs to an apartment building rather than to a plot.</summary>
    /// <param name="kind">The housing kind.</param>
    /// <returns>True for an apartment room or an apartment lobby.</returns>
    public static bool IsApartmentBuilding(this HousingInteriorKind kind)
        => kind is HousingInteriorKind.Apartment or HousingInteriorKind.ApartmentLobby;

    /// <summary>Resolves the estate kind a plot of the given size leads into.</summary>
    /// <param name="plotSize">The plot size from the land-set row: 0 small, 1 medium, 2 large.</param>
    /// <returns>The matching estate kind, or null when the size is not one the sheet describes.</returns>
    public static HousingInteriorKind? FromPlotSize(byte plotSize) => plotSize switch
    {
        0 => HousingInteriorKind.Cottage,
        1 => HousingInteriorKind.House,
        2 => HousingInteriorKind.Mansion,
        _ => null,
    };
}

/// <summary>
/// The kind of an owned housing teleport target, as read from the logged-in character's teleport list. The list is
/// the source of truth for what is reachable: an estate only appears once it is teleportable (a private or shared
/// estate needs its garden aetheryte placed; an apartment always is), so nothing here re-derives that.
/// </summary>
public enum EstateKind
{
    /// <summary>The character's own private estate.</summary>
    PrivateEstate,

    /// <summary>The character's Free Company estate; its chambers and workshop are reached from here on foot.</summary>
    FreeCompanyEstate,

    /// <summary>A rented apartment, always teleportable.</summary>
    Apartment,

    /// <summary>A shared estate the character has access to.</summary>
    SharedEstate,
}

/// <summary>One housing interior territory paired with what the game's own sheet says it is.</summary>
/// <param name="TerritoryId">The interior's TerritoryType row id.</param>
/// <param name="Kind">What kind of interior it is.</param>
public readonly record struct HousingInteriorInfo(uint TerritoryId, HousingInteriorKind Kind);

/// <summary>One plot in a residential district, as the land-set sheet describes it.</summary>
/// <param name="Index">The plot's zero-based index within the district, which is also its map-marker index.</param>
/// <param name="Kind">The estate kind the plot's size leads into.</param>
public readonly record struct HousingPlot(int Index, HousingInteriorKind Kind);

/// <summary>
/// One district's plots, together with the level-file instance ids that identify which district the row belongs to.
/// The land-set sheet is keyed by an anonymous district index, so the district is recovered by matching these
/// instance ids against the ones actually placed in a district's level file rather than by assuming row order.
/// </summary>
/// <param name="LandSetId">The land-set row id, an opaque district index.</param>
/// <param name="Plots">The district's plots in marker order.</param>
/// <param name="MarkerInstanceIds">The level-file instance ids the row references, used to identify its district.</param>
public readonly record struct HousingLandSetInfo(
    uint LandSetId,
    IReadOnlyList<HousingPlot> Plots,
    IReadOnlyList<uint> MarkerInstanceIds);

/// <summary>One placed housing door. <see cref="Found"/> is false when the level file held none.</summary>
/// <param name="Position">The door's world position.</param>
/// <param name="InteractObjectId">The EObj row id of the object to interact with.</param>
/// <param name="Found">Whether a door was resolved at all.</param>
public readonly record struct HousingDoor(Vector3 Position, uint InteractObjectId, bool Found = true);

/// <summary>An interior's two doors: the one back out, and the one further in when the interior has one.</summary>
/// <param name="TerritoryId">The interior territory.</param>
/// <param name="Outward">The door leading back the way the character came.</param>
/// <param name="Inward">The door leading deeper in, unfound for an interior with only one door.</param>
public readonly record struct HousingInteriorDoors(uint TerritoryId, HousingDoor Outward, HousingDoor Inward);

/// <summary>
/// A character's own housing address, as the game's housing data states it. Ward and plot are held <b>zero-based</b>
/// the way the game stores them and are shown one-based, which <see cref="HousingHelper.FormatAddress"/> takes care
/// of; a room number is already the number the game displays.
/// </summary>
/// <param name="Owned">Whether the character owns anything of this kind at all. Every other field is meaningless when false.</param>
/// <param name="Ward">The zero-based ward index.</param>
/// <param name="Plot">The zero-based plot index, meaningless for an apartment.</param>
/// <param name="Room">The apartment room number, already one-based.</param>
/// <param name="IsApartment">Whether the address is an apartment room rather than a plot.</param>
/// <param name="Division">The apartment's division: zero for the main division, non-zero for the subdivision.</param>
public readonly record struct HousingAddress(
    bool Owned,
    int Ward,
    int Plot,
    int Room,
    bool IsApartment,
    int Division)
{
    /// <summary>An address for a kind of housing the character does not own.</summary>
    public static HousingAddress None => default;
}
