namespace NoireLib.Helpers;

/// <summary>One of a territory's maps, carrying the projection its markers and coordinates are expressed in.</summary>
/// <param name="MapId">The Map row id.</param>
/// <param name="MapMarkerRange">The map's marker range, which is the key into the MapMarker subrow sheet.</param>
/// <param name="SizeFactor">The map's SizeFactor, the zoom the projection is drawn at.</param>
/// <param name="OffsetX">The map's OffsetX.</param>
/// <param name="OffsetY">The map's OffsetY, which offsets the world Z axis.</param>
public readonly record struct MapProjection(uint MapId, uint MapMarkerRange, float SizeFactor, float OffsetX, float OffsetY);
