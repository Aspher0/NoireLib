namespace NoireLib.Helpers;

/// <summary>One level of detail: which meshes belong to it and where its buffers start.</summary>
public sealed class GameModelLod
{
    /// <summary>Index of the first mesh belonging to this level.</summary>
    public ushort MeshIndex { get; init; }

    /// <summary>How many meshes belong to this level.</summary>
    public ushort MeshCount { get; init; }

    /// <summary>Byte offset of this level's vertex data within the file.</summary>
    public uint VertexDataOffset { get; init; }

    /// <summary>Byte offset of this level's index data within the file.</summary>
    public uint IndexDataOffset { get; init; }
}
