namespace NoireLib.Helpers;

/// <summary>One field of a vertex, describing where it sits in a stream and how it is stored.</summary>
/// <param name="Stream">Which of the mesh's vertex streams holds this element.</param>
/// <param name="Offset">Byte offset within that stream's vertex stride.</param>
/// <param name="Type">Storage format.</param>
/// <param name="Usage">Semantic meaning.</param>
/// <param name="UsageIndex">Distinguishes multiple elements sharing a usage.</param>
public readonly record struct GameVertexElement(
    byte Stream,
    byte Offset,
    GameVertexType Type,
    GameVertexUsage Usage,
    byte UsageIndex);
