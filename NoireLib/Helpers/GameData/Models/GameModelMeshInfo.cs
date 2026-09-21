namespace NoireLib.Helpers;

/// <summary>A drawable range of a game model, addressing a slice of one level of detail's buffers.</summary>
public sealed class GameModelMeshInfo
{
    /// <summary>Number of vertices.</summary>
    public ushort VertexCount { get; init; }

    /// <summary>Number of indices.</summary>
    public uint IndexCount { get; init; }

    /// <summary>Index into the model's material path list.</summary>
    public ushort MaterialIndex { get; init; }

    /// <summary>First index of this mesh within its level of detail's index buffer.</summary>
    public uint StartIndex { get; init; }

    /// <summary>Byte offset of each vertex stream, relative to the level of detail's vertex data.</summary>
    public uint[] VertexBufferOffset { get; init; } = new uint[3];

    /// <summary>Vertex stride of each stream in bytes.</summary>
    public byte[] VertexBufferStride { get; init; } = new byte[3];

    /// <summary>How many of the three streams this mesh actually uses.</summary>
    public byte VertexStreamCount { get; init; }
}
