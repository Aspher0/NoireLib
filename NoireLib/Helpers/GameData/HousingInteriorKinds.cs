namespace NoireLib.Helpers;

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
