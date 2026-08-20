namespace NoireLib.Helpers;

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
/// <param name="District">
/// The residential district the address is in, or zero when the read did not carry one. An owned address does not
/// need it, since the estate's own teleport entry names the territory; the house the character is currently standing
/// inside does, since an interior names no district of its own.
/// </param>
/// <param name="IsWorkshop">Whether the address is a company workshop rather than a residence.</param>
public readonly record struct HousingAddress(
    bool Owned,
    int Ward,
    int Plot,
    int Room,
    bool IsApartment,
    int Division,
    uint District = 0,
    bool IsWorkshop = false)
{
    /// <summary>An address for a kind of housing the character does not own.</summary>
    public static HousingAddress None => default;

    /// <summary>Whether this address and another name the same plot of the same ward of the same district.</summary>
    /// <param name="other">The address to compare against.</param>
    /// <returns>True when both are owned and name the same plot.</returns>
    public bool SamePlot(HousingAddress other)
        => Owned && other.Owned && !IsApartment && !other.IsApartment
           && Ward == other.Ward && Plot == other.Plot
           && (District == 0 || other.District == 0 || District == other.District);
}
