namespace NoireLib.Helpers;

/// <summary>One layer set a level defines, and the territory it belongs to.</summary>
/// <param name="LayerSetId">The layer set's id, in the level editor's own id space.</param>
/// <param name="TerritoryId">The TerritoryType row the layer set belongs to.</param>
public readonly record struct LevelLayerSet(uint LayerSetId, uint TerritoryId);
