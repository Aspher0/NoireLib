namespace NoireLib.Helpers;

/// <summary>What a vertex element means. A single UV element carries two sets, packed as xy and zw.</summary>
public enum GameVertexUsage : byte
{
    /// <summary>Model-space position.</summary>
    Position = 0,
    /// <summary>Skinning weights.</summary>
    BlendWeights = 1,
    /// <summary>Skinning bone indices into the mesh's bone table.</summary>
    BlendIndices = 2,
    /// <summary>Model-space normal.</summary>
    Normal = 3,
    /// <summary>Texture coordinates, two sets per element.</summary>
    Uv = 4,
    /// <summary>Secondary tangent frame.</summary>
    Tangent2 = 5,
    /// <summary>Primary tangent frame, stored as a normalized byte vector.</summary>
    Tangent1 = 6,
    /// <summary>Per-vertex color, used by the game as shader data, not albedo.</summary>
    Color = 7,
}
