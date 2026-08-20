using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>One map marker projected into world space, so it can be matched against a placed object's position.</summary>
/// <param name="Marker">The marker itself.</param>
/// <param name="World">The marker's world position. Only X and Z are meaningful; a marker carries no height.</param>
/// <param name="MapId">The map the marker was projected through, since a territory can span several.</param>
public readonly record struct ProjectedMapMarker(MapMarkerEntry Marker, Vector3 World, uint MapId);
