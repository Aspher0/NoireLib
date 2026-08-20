namespace NoireLib.Helpers;

/// <summary>One plot in a residential district, as the land-set sheet describes it.</summary>
/// <param name="Index">The plot's zero-based index within the district, which is also its map-marker index.</param>
/// <param name="Kind">The estate kind the plot's size leads into.</param>
public readonly record struct HousingPlot(int Index, HousingInteriorKind Kind);
