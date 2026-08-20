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
