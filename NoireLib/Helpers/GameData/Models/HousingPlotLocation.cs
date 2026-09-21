using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// Where one residential plot or apartment building stands, resolved from the game's files.<br/>
/// Every ward is laid out identically. <see cref="Ward"/> changes no position.
/// </summary>
/// <param name="District">The residential district's TerritoryType row id.</param>
/// <param name="Ward">The one-based ward, as the game displays it.</param>
/// <param name="Plot">The one-based plot, as the game displays it. Zero for an apartment building.</param>
/// <param name="IsApartment">Whether the address names an apartment building.</param>
/// <param name="Subdivision">Whether the address is in the subdivision, plots 31 to 60.</param>
/// <param name="Kind">The estate kind the plot's size leads into, or null for an apartment building.</param>
/// <param name="Anchor">The plot's map anchor from the housing map-marker sheet. Above the ground, inside the plot.</param>
/// <param name="Entrance">The spot in front of the building's door where an arriving character is placed.</param>
/// <param name="Placard">The plot's placard, or null for an apartment building.</param>
/// <param name="ExitLanding">Where a character stepping out of the estate lands, or null for an apartment building.</param>
/// <param name="Found">Whether the address resolved at all.</param>
public readonly record struct HousingPlotLocation(
    uint District,
    int Ward,
    int Plot,
    bool IsApartment,
    bool Subdivision,
    HousingInteriorKind? Kind,
    Vector3 Anchor,
    Vector3 Entrance,
    Vector3? Placard,
    Vector3? ExitLanding,
    bool Found = true)
{
    /// <summary>An address that resolved to nothing.</summary>
    public static HousingPlotLocation None => default;
}
