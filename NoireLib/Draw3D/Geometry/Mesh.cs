using NoireLib.Draw3D.Core;
using System;
using System.Numerics;
using TerraFX.Interop.DirectX;

namespace NoireLib.Draw3D.Geometry;

/// <summary>
/// An immutable GPU mesh (vertex + index buffer) with a precomputed bounding sphere.<br/>
/// <b>Creation is synchronous and safe from any thread</b> - D3D11 devices are free-threaded - so a
/// background asset load produces ready-to-draw meshes directly.<br/>
/// <b>Ownership:</b> the creator disposes it. Nodes and renderers only reference meshes; sharing one
/// mesh across a thousand nodes is the intended usage.
/// </summary>
public sealed unsafe class Mesh : IDisposable
{
    private GpuBuffer? vertexBuffer;
    private GpuBuffer? indexBuffer;
    private Mesh[]? lods; // progressively coarser levels (finest first), owned by this mesh; null = none
    private MeshBvh? bvh; // lazily built on the first pick, from the retained CPU geometry (keepCpuData meshes only)
    private bool bvhAttempted;
    private readonly object bvhLock = new();
    private volatile bool disposed;

    /// <summary>Number of vertices.</summary>
    public int VertexCount { get; }

    /// <summary>Number of indices (triangle list).</summary>
    public int IndexCount { get; }

    /// <summary>True when the index buffer is 32-bit (large imported meshes); false = 16-bit.</summary>
    public bool Uses32BitIndices { get; }

    /// <summary>Conservative model-space bounding sphere (culling, picking).</summary>
    public BoundingSphere LocalBounds { get; }

    /// <summary>Optional debug name.</summary>
    public string? Name { get; }

    /// <summary>CPU vertex copy, retained when requested at creation (exact picking).</summary>
    public Vertex3D[]? CpuVertices { get; }

    /// <summary>CPU 16-bit index copy, retained when requested at creation (null for 32-bit meshes).</summary>
    public ushort[]? CpuIndices16 { get; }

    /// <summary>CPU 32-bit index copy, retained when requested at creation (null for 16-bit meshes).</summary>
    public uint[]? CpuIndices32 { get; }

    /// <summary>True once disposed. Draws referencing a disposed mesh are skipped and counted, never a crash.</summary>
    public bool IsDisposed => disposed;

    /// <summary>
    /// Number of coarser level-of-detail meshes attached below this one (0 = none). The renderer draws a lower LOD when
    /// the object is small on screen; culling, picking and bounds always use this full-resolution mesh. Generated
    /// automatically for large imported models (see <see cref="MeshSimplifier.BuildLods"/>), off otherwise.
    /// </summary>
    public int LodCount => lods?.Length ?? 0;

    /// <summary>
    /// Attaches a finest-first chain of coarser LOD meshes, transferring their ownership to this mesh (they are freed
    /// when it is). Replaces any existing chain. Pass an empty array to clear it. Called by the importer; usable
    /// directly for hand-authored LODs.
    /// </summary>
    /// <param name="levels">Coarser meshes, ordered from most to least detailed. Null or empty clears the chain.</param>
    public void SetLods(Mesh[]? levels)
    {
        var old = lods;
        lods = levels != null && levels.Length > 0 ? levels : null;
        if (old != null)
            foreach (var lod in old)
                lod.Dispose();
    }

    /// <summary>
    /// The mesh to draw at the given LOD level: 0 (or no chain) returns this full-resolution mesh; higher levels return
    /// progressively coarser ones, clamped to the coarsest available. The renderer's screen-size selection drives it.
    /// </summary>
    /// <param name="level">0 = full detail; 1..<see cref="LodCount"/> = coarser levels.</param>
    public Mesh SelectLod(int level)
    {
        var chain = lods;
        if (level <= 0 || chain == null || chain.Length == 0)
            return this;

        return chain[Math.Min(level - 1, chain.Length - 1)];
    }

    /// <summary>
    /// Casts a <b>model-space</b> ray against this mesh's triangles via a lazily-built BVH and returns the nearest hit -
    /// the acceleration that keeps hover-picking a dense mesh from scanning every triangle each frame. The direction is
    /// treated as-is, so <paramref name="t"/> comes back in the units of <paramref name="localDirection"/>. False when
    /// the mesh kept no CPU geometry (created without <c>keepCpuData</c>), is disposed, or the ray misses. Any thread.
    /// </summary>
    /// <param name="localOrigin">Ray origin in model space.</param>
    /// <param name="localDirection">Ray direction in model space.</param>
    /// <param name="t">Receives the hit distance along <paramref name="localDirection"/>.</param>
    /// <param name="triangle">Receives the hit triangle index.</param>
    internal bool RayCastLocal(Vector3 localOrigin, Vector3 localDirection, out float t, out int triangle)
    {
        t = 0f;
        triangle = -1;
        if (disposed || CpuVertices == null || (CpuIndices16 == null && CpuIndices32 == null))
            return false;

        var built = bvh;
        if (built == null && !bvhAttempted)
        {
            lock (bvhLock)
            {
                if (bvh == null && !bvhAttempted)
                {
                    bvhAttempted = true; // build once even if the mesh is degenerate (Build returns null)
                    bvh = MeshBvh.Build(CpuVertices, CpuIndices16, CpuIndices32);
                }

                built = bvh;
            }
        }

        return built != null && built.RayCast(localOrigin, localDirection, out t, out triangle);
    }

    internal ID3D11Buffer* Vb
    {
        get
        {
            var vb = vertexBuffer; // local copy: Dispose may null the field concurrently
            return disposed || vb == null ? null : vb.Buffer;
        }
    }

    internal ID3D11Buffer* Ib
    {
        get
        {
            var ib = indexBuffer;
            return disposed || ib == null ? null : ib.Buffer;
        }
    }
    internal DXGI_FORMAT IndexFormat => Uses32BitIndices ? DXGI_FORMAT.DXGI_FORMAT_R32_UINT : DXGI_FORMAT.DXGI_FORMAT_R16_UINT;

    /// <summary>Creates a mesh from builder output.</summary>
    /// <param name="data">CPU mesh data (see <see cref="MeshBuilder"/>).</param>
    /// <param name="keepCpuData">Retain the CPU arrays for exact triangle picking.</param>
    /// <param name="name">Optional debug name.</param>
    public Mesh(MeshData data, bool keepCpuData = false, string? name = null)
        : this(data.Vertices, data.Indices, keepCpuData, name)
    {
    }

    /// <summary>Creates a mesh from vertex and 16-bit index arrays.</summary>
    /// <param name="vertices">Vertex array (up to 65 535 vertices).</param>
    /// <param name="indices">Index array, triangle list, clockwise-front winding.</param>
    /// <param name="keepCpuData">Retain the CPU arrays for exact triangle picking.</param>
    /// <param name="name">Optional debug name.</param>
    public Mesh(Vertex3D[] vertices, ushort[] indices, bool keepCpuData = false, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        VertexCount = vertices.Length;
        IndexCount = indices.Length;
        Uses32BitIndices = false;
        LocalBounds = BoundingSphere.FromVertices(vertices);
        Name = name;

        var device = NoireDraw3D.RequireDevice();
        fixed (Vertex3D* v = vertices)
            vertexBuffer = GpuBuffer.CreateImmutable(device, v, (uint)(vertices.Length * sizeof(Vertex3D)), D3D11_BIND_FLAG.D3D11_BIND_VERTEX_BUFFER);
        fixed (ushort* i = indices)
            indexBuffer = GpuBuffer.CreateImmutable(device, i, (uint)(indices.Length * sizeof(ushort)), D3D11_BIND_FLAG.D3D11_BIND_INDEX_BUFFER);

        if (keepCpuData)
        {
            CpuVertices = vertices;
            CpuIndices16 = indices;
        }
    }

    /// <summary>Creates a mesh from vertex and 32-bit index arrays (large imported meshes).</summary>
    /// <param name="vertices">Vertex array.</param>
    /// <param name="indices">Index array, triangle list, clockwise-front winding.</param>
    /// <param name="keepCpuData">Retain the CPU arrays for exact triangle picking.</param>
    /// <param name="name">Optional debug name.</param>
    public Mesh(Vertex3D[] vertices, uint[] indices, bool keepCpuData = false, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        VertexCount = vertices.Length;
        IndexCount = indices.Length;
        Uses32BitIndices = true;
        LocalBounds = BoundingSphere.FromVertices(vertices);
        Name = name;

        var device = NoireDraw3D.RequireDevice();
        fixed (Vertex3D* v = vertices)
            vertexBuffer = GpuBuffer.CreateImmutable(device, v, (uint)(vertices.Length * sizeof(Vertex3D)), D3D11_BIND_FLAG.D3D11_BIND_VERTEX_BUFFER);
        fixed (uint* i = indices)
            indexBuffer = GpuBuffer.CreateImmutable(device, i, (uint)(indices.Length * sizeof(uint)), D3D11_BIND_FLAG.D3D11_BIND_INDEX_BUFFER);

        if (keepCpuData)
        {
            CpuVertices = vertices;
            CpuIndices32 = indices;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        // Coarser LOD levels are owned by this mesh - release them with it.
        var chain = lods;
        lods = null;
        bvh = null; // managed arrays only; GC reclaims them once unreferenced
        if (chain != null)
            foreach (var lod in chain)
                lod.Dispose();

        // Defer the GPU releases to the render thread so an in-progress frame can never bind a freed buffer.
        var vb = vertexBuffer;
        var ib = indexBuffer;
        vertexBuffer = null;
        indexBuffer = null;
        NoireDraw3D.EnqueueRelease(() =>
        {
            vb?.Dispose();
            ib?.Dispose();
        });
    }
}
