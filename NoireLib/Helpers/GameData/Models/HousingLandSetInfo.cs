using System.Collections.Generic;

namespace NoireLib.Helpers;

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
