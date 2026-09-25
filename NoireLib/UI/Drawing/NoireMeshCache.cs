using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Records the vertices a drawing produced and replays them at any position, in one reservation. For icons, glows
/// and outlines: never text or CPU-clipped drawing. Everything the vertices depend on goes in the key.
/// </summary>
/// <typeparam name="TKey">What tells two recordings apart. Must compare by value.</typeparam>
public sealed class NoireMeshCache<TKey> where TKey : IEquatable<TKey>
{
    private readonly Dictionary<TKey, Mesh> meshes;
    private readonly int capacity;
    private long clock;

    // Keys seen once. A key records on its second frame: drawing that changes every frame never records.
    private readonly TKey[] sighted = new TKey[SightedSlots];
    private readonly int[] sightedFrames = new int[SightedSlots];
    private readonly bool[] sightedUsed = new bool[SightedSlots];

    private const int SightedSlots = 1024;

    /// <summary>Creates a cache.</summary>
    /// <param name="capacity">How many recordings are kept before the least recently replayed one is dropped.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is below 1.</exception>
    public NoireMeshCache(int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        this.capacity = capacity;
        meshes = new Dictionary<TKey, Mesh>(capacity);
    }

    /// <summary>How many recordings the cache holds.</summary>
    public int Count => meshes.Count;

    /// <summary>How many replays were written.</summary>
    public long Hits => hits;

    /// <summary>How many replays found nothing usable, and drew instead.</summary>
    public long Misses => misses;

    /// <summary>How many recordings were not kept because their triangles spanned several draw commands.</summary>
    public long Rejections => rejections;

    /// <summary>How many recordings were dropped to make room for a new one.</summary>
    public long Evictions => evictions;

    private long hits;
    private long misses;
    private long rejections;
    private long evictions;

    /// <summary>
    /// Writes a recording into a draw list with its origin at <paramref name="origin"/>.
    /// </summary>
    /// <param name="drawList">The draw list.</param>
    /// <param name="key">The recording to replay.</param>
    /// <param name="origin">Where the recording's origin lands.</param>
    /// <returns>Whether a usable recording existed and was written. When <see langword="false"/>, draw and record.</returns>
    public bool TryReplay(ImDrawListPtr drawList, in TKey key, Vector2 origin)
        => Replay(drawList, key, origin, 0u, false);

    /// <summary>
    /// Replays a one-color recording at <paramref name="origin"/>, repainted in <paramref name="color"/> with opacity
    /// scaled against the recorded color. Keep the color out of the key.
    /// </summary>
    /// <param name="drawList">The draw list.</param>
    /// <param name="key">The recording to replay.</param>
    /// <param name="origin">Where the recording's origin lands.</param>
    /// <param name="color">The packed color every vertex takes.</param>
    /// <returns>Whether a recording was written. When false, draw and record.</returns>
    public bool TryReplay(ImDrawListPtr drawList, in TKey key, Vector2 origin, uint color)
        => Replay(drawList, key, origin, color, true);

    private unsafe bool Replay(ImDrawListPtr drawList, in TKey key, Vector2 origin, uint color, bool recolor)
    {
        if (drawList.IsNull || !meshes.TryGetValue(key, out var mesh) || mesh.Rejected || !mesh.Stamp.Equals(MeshStamp.Of(drawList)))
        {
            misses++;
            return false;
        }

        // Repainting needs the color the recording was made in.
        if (recolor && mesh.BaseAlpha == 0u)
        {
            misses++;
            return false;
        }

        hits++;
        mesh.Used = ++clock;

        if (mesh.VertexCount == 0)
            return true;

        drawList.PrimReserve(mesh.IndexCount, mesh.VertexCount);

        // Read after the reservation, never before: reserving can roll the index offset over.
        var native = drawList.Handle;
        var vertex = native->VtxWritePtr;
        var index = native->IdxWritePtr;
        var first = native->VtxCurrentIdx;

        var rgb = color & 0x00FFFFFFu;
        var scale = recolor ? ((color >> 24) << 16) / mesh.BaseAlpha : 0u;

        fixed (ImDrawVert* source = mesh.Vertices)
        {
            for (var i = 0; i < mesh.VertexCount; i++)
            {
                vertex[i] = source[i];
                vertex[i].Pos += origin;

                if (recolor)
                {
                    var alpha = Math.Min(255u, (((source[i].Col >> 24) * scale) + 0x8000u) >> 16);
                    vertex[i].Col = rgb | (alpha << 24);
                }
            }
        }

        fixed (ushort* source = mesh.Indices)
            NoireShapes.RebaseIndices(source, index, mesh.IndexCount, first);

        NoireShapes.AdvancePrimWrite(drawList, mesh.VertexCount, mesh.IndexCount);

        return true;
    }

    /// <summary>
    /// Starts recording what is drawn into <paramref name="drawList"/> until the returned recording is disposed.
    /// </summary>
    /// <param name="drawList">The draw list the drawing goes into. The drawing still lands there as usual.</param>
    /// <param name="key">What the recording is kept under. Replaces any recording already under it.</param>
    /// <param name="origin">The point the recording is kept relative to.</param>
    /// <returns>The recording. Dispose it right after the drawing.</returns>
    public NoireMeshRecording<TKey> Record(ImDrawListPtr drawList, TKey key, Vector2 origin)
        => Record(drawList, key, origin, 0u);

    /// <summary>
    /// Starts recording drawing done in one color, for replays repainted with
    /// <see cref="TryReplay(ImDrawListPtr, in TKey, Vector2, uint)"/>.
    /// </summary>
    /// <param name="drawList">The draw list the drawing goes into. The drawing still lands there as usual.</param>
    /// <param name="key">What the recording is kept under. Replaces any recording already under it.</param>
    /// <param name="origin">The point the recording is kept relative to.</param>
    /// <param name="color">The packed color the drawing is done in.</param>
    /// <returns>The recording. Dispose it right after the drawing.</returns>
    public NoireMeshRecording<TKey> Record(ImDrawListPtr drawList, TKey key, Vector2 origin, uint color)
    {
        if (meshes.TryGetValue(key, out var mesh))
        {
            // A rejected key draws plainly for a while before recording is tried again.
            if (mesh.Rejected && NoireUI.FrameCount - mesh.RejectedFrame < RetryFrames)
                return default;

            return new(this, drawList, key, origin, color);
        }

        if (!Sighted(key))
            return default;

        return new(this, drawList, key, origin, color);
    }

    // The same key twice in one frame proves nothing about the next.
    private bool Sighted(in TKey key)
    {
        var frame = NoireUI.FrameCount;
        var slot = key.GetHashCode() & (SightedSlots - 1);

        if (sightedUsed[slot] && sighted[slot].Equals(key))
            return sightedFrames[slot] != frame;

        sighted[slot] = key;
        sightedFrames[slot] = frame;
        sightedUsed[slot] = true;

        return false;
    }

    private const int RetryFrames = 120;

    /// <summary>Drops every recording.</summary>
    public void Clear()
    {
        meshes.Clear();
        Array.Clear(sighted);
        Array.Clear(sightedUsed);
    }

    internal unsafe void Keep(ImDrawListPtr drawList, TKey key, Vector2 origin, in MeshStart start)
    {
        var native = drawList.Handle;

        if (!meshes.TryGetValue(key, out var mesh))
        {
            if (meshes.Count >= capacity)
                Evict();

            mesh = new Mesh();
            meshes[key] = mesh;
        }

        var vertexCount = native->VtxBuffer.Size - start.Vertices;
        var indexCount = native->IdxBuffer.Size - start.Indices;
        var command = native->CmdBuffer.Size > 0 ? &native->CmdBuffer.Data[native->CmdBuffer.Size - 1] : null;

        // Every triangle must share the starting command's texture, clip rectangle and index offset.
        mesh.Rejected = command == null
            || native->CmdBuffer.Size != start.Commands
            || command->IdxOffset != start.CommandOffset
            || command->ElemCount != start.CommandElements + (uint)indexCount
            || native->CmdHeader.VtxOffset != start.VertexOffset
            || native->CmdHeader.TextureId.Handle != start.Texture
            || native->CmdHeader.ClipRect != start.Clip
            || vertexCount < 0
            || indexCount < 0
            || vertexCount > ushort.MaxValue;

        mesh.Used = ++clock;

        if (mesh.Rejected)
        {
            rejections++;
            mesh.RejectedFrame = NoireUI.FrameCount;
            mesh.VertexCount = 0;
            mesh.IndexCount = 0;
            return;
        }

        if (mesh.Vertices.Length < vertexCount)
            mesh.Vertices = new ImDrawVert[vertexCount];

        if (mesh.Indices.Length < indexCount)
            mesh.Indices = new ushort[indexCount];

        var vertices = native->VtxBuffer.Data + start.Vertices;
        var indices = native->IdxBuffer.Data + start.Indices;

        for (var i = 0; i < vertexCount; i++)
        {
            var copy = vertices[i];
            copy.Pos -= origin;
            mesh.Vertices[i] = copy;
        }

        for (var i = 0; i < indexCount; i++)
            mesh.Indices[i] = (ushort)(indices[i] - start.FirstIndex);

        mesh.VertexCount = vertexCount;
        mesh.IndexCount = indexCount;
        mesh.Stamp = start.Stamp;
        mesh.BaseAlpha = start.Color >> 24;
    }

    private void Evict()
    {
        var oldest = default(TKey);
        var oldestUsed = long.MaxValue;
        var found = false;

        foreach (var (key, mesh) in meshes)
        {
            if (mesh.Used >= oldestUsed)
                continue;

            oldest = key;
            oldestUsed = mesh.Used;
            found = true;
        }

        if (found)
        {
            meshes.Remove(oldest!);
            evictions++;
        }
    }

    private sealed class Mesh
    {
        public ImDrawVert[] Vertices = [];
        public ushort[] Indices = [];
        public int VertexCount;
        public int IndexCount;
        public MeshStamp Stamp;
        public long Used;
        public bool Rejected;
        public int RejectedFrame;
        public uint BaseAlpha;
    }
}

/// <summary>
/// A recording in progress, started by <see cref="NoireMeshCache{TKey}.Record(ImDrawListPtr, TKey, Vector2)"/>. Disposing it keeps what was drawn.
/// </summary>
/// <typeparam name="TKey">The cache's key type.</typeparam>
public ref struct NoireMeshRecording<TKey> where TKey : IEquatable<TKey>
{
    private readonly NoireMeshCache<TKey>? cache;
    private readonly ImDrawListPtr drawList;
    private readonly TKey key;
    private readonly Vector2 origin;
    private readonly MeshStart start;

    internal unsafe NoireMeshRecording(NoireMeshCache<TKey> cache, ImDrawListPtr drawList, TKey key, Vector2 origin, uint color)
    {
        this.drawList = drawList;
        this.key = key;
        this.origin = origin;

        if (drawList.IsNull)
        {
            this.cache = null;
            start = default;
            return;
        }

        var native = drawList.Handle;
        var command = native->CmdBuffer.Size > 0 ? &native->CmdBuffer.Data[native->CmdBuffer.Size - 1] : null;

        this.cache = cache;
        start = new MeshStart(
            native->VtxBuffer.Size,
            native->IdxBuffer.Size,
            native->CmdBuffer.Size,
            native->VtxCurrentIdx,
            native->CmdHeader.VtxOffset,
            native->CmdHeader.TextureId.Handle,
            native->CmdHeader.ClipRect,
            MeshStamp.Of(drawList),
            color,
            command != null ? command->IdxOffset : uint.MaxValue,
            command != null ? command->ElemCount : 0u);
    }

    /// <summary>Keeps what was drawn since the recording started. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (cache == null)
            return;

        cache.Keep(drawList, key, origin, start);
        this = default;
    }
}

internal readonly record struct MeshStart(
    int Vertices,
    int Indices,
    int Commands,
    uint FirstIndex,
    uint VertexOffset,
    ulong Texture,
    Vector4 Clip,
    MeshStamp Stamp,
    uint Color,
    uint CommandOffset,
    uint CommandElements);

// What ImGui reads besides the caller's positions when it builds antialiased geometry.
internal readonly record struct MeshStamp(ImDrawListFlags Flags, float FringeScale, Vector2 WhitePixel)
{
    public static unsafe MeshStamp Of(ImDrawListPtr drawList)
    {
        var native = drawList.Handle;
        var shared = native->Data;

        return new MeshStamp(native->Flags, native->FringeScale, shared != null ? shared->TexUvWhitePixel : default);
    }
}
