namespace NoireLib.Helpers;

/// <summary>
/// One map marker as the sheet states it: what it points at, where on the map it sits, and the icon it draws with.
/// </summary>
/// <param name="DataType">What <see cref="DataKey"/> means; see <see cref="MapMarkerDataType"/>.</param>
/// <param name="DataKey">The row the marker keys, interpreted per <see cref="DataType"/>.</param>
/// <param name="MapX">The marker's map X pixel, in the 0-2048 space the projection is expressed in.</param>
/// <param name="MapY">The marker's map Y pixel, which projects onto the world Z axis.</param>
/// <param name="Icon">The marker icon id.</param>
public readonly record struct MapMarkerEntry(int DataType, uint DataKey, float MapX, float MapY, uint Icon);
