using NoireLib.Draw3D.Core;
using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Materials;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace NoireLib.Draw3D.Im;

/// <summary>
/// The immediate-mode layer. Shapes drawn each frame render through the scene pass.<br/>
/// Calls inside <see cref="Scene.Scene3D.OnPrepareFrame"/> or an <see cref="Scene.ISceneFeature"/> render the same frame. Elsewhere they may render a frame late.
/// </summary>
public sealed class ImDraw3D
{
    private enum Kind { Donut, Circle, Sector, Rect, Chevron, Line, Path, Sphere, Arrow, Mesh }

    private struct Command
    {
        public Kind Kind;
        public Vector3 A, B;
        public float F0, F1, F2;
        public Vector4 Color;
        public ImShapeStyle Style;
        public int PathStart, PathCount;
        public bool Closed;
        public Mesh? Mesh;
        public Material? Material;
        public Matrix4x4 World;
    }

    private const float OutlineWidth = 0.03f;

    private readonly object sync = new();
    private readonly List<Command> commands = new(128);
    private readonly List<Vector3> pathPool = new(256);

    private Mesh? unitBox;
    private Mesh? unitSphere;
    private List<Vector3>? outlinePath; // render thread only

    internal ImDraw3D() { }

    /// <summary>True when commands are waiting to be rendered.</summary>
    public bool HasPending
    {
        get
        {
            lock (sync)
                return commands.Count > 0;
        }
    }

    /// <summary>Draws a ground ring around <paramref name="center"/>.</summary>
    /// <param name="center">World-space centre.</param>
    /// <param name="innerRadius">Inner radius.</param>
    /// <param name="outerRadius">Outer radius.</param>
    /// <param name="color">Color in straight alpha.</param>
    /// <param name="style">Optional placement and blending style.</param>
    public void DrawDonut(Vector3 center, float innerRadius, float outerRadius, Vector4 color, ImShapeStyle? style = null)
        => Add(new Command { Kind = Kind.Donut, A = center, F0 = innerRadius, F1 = outerRadius, Color = color, Style = style ?? default });

    /// <summary>Draws a ground circle at <paramref name="center"/>.</summary>
    /// <param name="center">World-space centre.</param>
    /// <param name="radius">Circle radius.</param>
    /// <param name="color">Color in straight alpha.</param>
    /// <param name="style">Optional placement and blending style.</param>
    public void DrawCircle(Vector3 center, float radius, Vector4 color, ImShapeStyle? style = null)
        => Add(new Command { Kind = Kind.Circle, A = center, F0 = radius, Color = color, Style = style ?? default });

    /// <summary>Draws a ground pie slice around <paramref name="center"/>.</summary>
    /// <param name="center">World-space centre.</param>
    /// <param name="facingRad">Slice direction in radians around +Y, 0 facing +Z.</param>
    /// <param name="halfAngleRad">Half of the slice's opening angle, in radians.</param>
    /// <param name="innerRadius">Inner radius, 0 for a full slice.</param>
    /// <param name="outerRadius">Outer radius.</param>
    /// <param name="color">Color in straight alpha.</param>
    /// <param name="style">Optional placement and blending style.</param>
    public void DrawSector(Vector3 center, float facingRad, float halfAngleRad, float innerRadius, float outerRadius, Vector4 color, ImShapeStyle? style = null)
        => Add(new Command { Kind = Kind.Sector, A = center, F0 = facingRad, F1 = halfAngleRad, F2 = outerRadius, B = new Vector3(innerRadius, 0, 0), Color = color, Style = style ?? default });

    /// <summary>Draws a ground rectangle centered at <paramref name="center"/>, rotated by <paramref name="facingRad"/> around +Y.</summary>
    /// <param name="center">World-space centre.</param>
    /// <param name="facingRad">Rotation in radians around +Y.</param>
    /// <param name="size">Width along local X and depth along local Z.</param>
    /// <param name="color">Color in straight alpha.</param>
    /// <param name="style">Optional placement and blending style.</param>
    public void DrawRect(Vector3 center, float facingRad, Vector2 size, Vector4 color, ImShapeStyle? style = null)
        => Add(new Command { Kind = Kind.Rect, A = center, F0 = facingRad, F1 = size.X, F2 = size.Y, Color = color, Style = style ?? default });

    /// <summary>Draws a ground chevron centered at <paramref name="center"/>, pointing along <paramref name="facingRad"/> around +Y (0 = +Z).</summary>
    /// <param name="center">World-space centre.</param>
    /// <param name="facingRad">Pointing direction in radians around +Y.</param>
    /// <param name="size">Width across and depth along the pointing direction, in world units.</param>
    /// <param name="color">Color in straight alpha.</param>
    /// <param name="stroke">Half the stroke width as a footprint ratio, 0 meaning 0.22.</param>
    /// <param name="style">Optional placement and blending style.</param>
    public void DrawChevron(Vector3 center, float facingRad, Vector2 size, Vector4 color, float stroke = 0f, ImShapeStyle? style = null)
        => Add(new Command { Kind = Kind.Chevron, A = center, F0 = facingRad, F1 = size.X, F2 = size.Y, B = new Vector3(stroke, 0, 0), Color = color, Style = style ?? default });

    /// <summary>Draws a camera-facing line segment of the given world-space width.</summary>
    /// <param name="from">Start point.</param>
    /// <param name="to">End point.</param>
    /// <param name="width">Width in world units.</param>
    /// <param name="color">Color in straight alpha.</param>
    /// <param name="style">Optional depth and blending style.</param>
    public void DrawLine(Vector3 from, Vector3 to, float width, Vector4 color, ImShapeStyle? style = null)
        => Add(new Command { Kind = Kind.Line, A = from, B = to, F0 = width, Color = color, Style = style ?? default });

    /// <summary>Draws a camera-facing ribbon along a polyline.</summary>
    /// <param name="points">Polyline points. Fewer than two draws nothing.</param>
    /// <param name="width">Width in world units.</param>
    /// <param name="color">Color in straight alpha.</param>
    /// <param name="closed">Whether the last point connects back to the first.</param>
    /// <param name="style">Optional depth and blending style.</param>
    public void DrawPath(IReadOnlyList<Vector3> points, float width, Vector4 color, bool closed = false, ImShapeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 2)
            return;

        lock (sync)
        {
            var start = pathPool.Count;
            for (var i = 0; i < points.Count; i++)
                pathPool.Add(points[i]);
            commands.Add(new Command { Kind = Kind.Path, F0 = width, Color = color, Closed = closed, Style = style ?? default, PathStart = start, PathCount = points.Count });
        }
    }

    /// <summary>Draws a translucent sphere.</summary>
    /// <param name="center">World-space centre.</param>
    /// <param name="radius">Sphere radius.</param>
    /// <param name="color">Color in straight alpha.</param>
    /// <param name="style">Optional depth and blending style.</param>
    public void DrawSphere(Vector3 center, float radius, Vector4 color, ImShapeStyle? style = null)
        => Add(new Command { Kind = Kind.Sphere, A = center, F0 = radius, Color = color, Style = style ?? default });

    /// <summary>Draws a 3D arrow from <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <param name="from">Base point.</param>
    /// <param name="to">Tip point.</param>
    /// <param name="width">Shaft width in world units.</param>
    /// <param name="color">Color in straight alpha.</param>
    /// <param name="style">Optional depth and blending style.</param>
    public void DrawArrow(Vector3 from, Vector3 to, float width, Vector4 color, ImShapeStyle? style = null)
        => Add(new Command { Kind = Kind.Arrow, A = from, B = to, F0 = width, Color = color, Style = style ?? default });

    /// <summary>Draws any mesh with any material at a world transform, for this frame only.</summary>
    /// <param name="mesh">The mesh to draw. Referenced, never owned.</param>
    /// <param name="world">The world transform.</param>
    /// <param name="material">The material to draw with.</param>
    public void DrawMesh(Mesh mesh, in Matrix4x4 world, Material material)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(material);
        Add(new Command { Kind = Kind.Mesh, Mesh = mesh, Material = material, World = world, Color = new Vector4(1f, 1f, 1f, 1f) });
    }

    private void Add(in Command command)
    {
        lock (sync)
            commands.Add(command);
    }

    internal void Consume(ScenePass pass, in FrameContext frame, RenderStats stats, bool depthAvailable, bool wireframe = false, bool outlineDecals = false, bool volumeDecals = false)
    {
        Command[] snapshot;
        Vector3[] paths;
        int count, pathCount;
        lock (sync)
        {
            count = commands.Count;
            if (count == 0)
                return;

            snapshot = System.Buffers.ArrayPool<Command>.Shared.Rent(count);
            commands.CopyTo(snapshot);
            commands.Clear();
            pathCount = pathPool.Count;
            paths = System.Buffers.ArrayPool<Vector3>.Shared.Rent(Math.Max(1, pathCount));
            pathPool.CopyTo(paths);
            pathPool.Clear();
        }

        try
        {
            for (var i = 0; i < count; i++)
            {
                ref var cmd = ref snapshot[i];
                switch (cmd.Kind)
                {
                    case Kind.Mesh:
                        if (cmd.Mesh!.IsDisposed || !MaterialData.TryFrom(cmd.Material!, out var meshMat))
                        {
                            stats.DisposedAssetDraws++;
                            break;
                        }

                        pass.AddMeshItem(cmd.Mesh, in meshMat, cmd.Material!.Texture, in cmd.World, cmd.Color * cmd.Material.Color, cmd.Style.Layer, castsDepth: true, stats, depthAvailable);
                        break;

                    case Kind.Sphere:
                        {
                            var mesh = unitSphere ??= new Mesh(MeshBuilder.Sphere(0.5f, 32, 20), name: "Im.UnitSphere");
                            var world = Matrix4x4.CreateScale(cmd.F0 * 2f) * Matrix4x4.CreateTranslation(cmd.A);
                            var mat = FlatData(cmd.Style, cullNone: false);
                            pass.AddMeshItem(mesh, in mat, null, in world, cmd.Color, cmd.Style.Layer, castsDepth: false, stats, depthAvailable);
                            break;
                        }

                    case Kind.Donut:
                    case Kind.Circle:
                    case Kind.Sector:
                    case Kind.Rect:
                    case Kind.Chevron:
                        if (cmd.Style.Placement == ImShapePlacement.Grounded)
                            AddDecal(pass, ref cmd, in frame, stats, depthAvailable, wireframe, outlineDecals, volumeDecals);
                        else
                            AddFlatShape(pass, ref cmd, stats, depthAvailable);
                        break;

                    case Kind.Line:
                    case Kind.Path:
                    case Kind.Arrow:
                        AddDynamicShape(pass, ref cmd, paths, in frame, stats, depthAvailable);
                        break;
                }
            }
        }
        finally
        {
            System.Buffers.ArrayPool<Command>.Shared.Return(snapshot, clearArray: true);
            System.Buffers.ArrayPool<Vector3>.Shared.Return(paths);
        }
    }

    private void AddDecal(ScenePass pass, ref Command cmd, in FrameContext frame, RenderStats stats, bool depthAvailable, bool wireframe, bool outlineDecals, bool volumeDecals)
    {
        var mesh = unitBox ??= new Mesh(MeshBuilder.Box(), name: "Im.UnitBox");
        var height = MathF.Max(cmd.Style.DecalHeight, 0.1f);

        DecalShape shape;
        Vector4 shapeParams;
        Vector3 scale;
        float facing = 0f;
        switch (cmd.Kind)
        {
            case Kind.Donut:
                shape = DecalShape.Ring;
                shapeParams = new Vector4(cmd.F1 > 1e-5f ? cmd.F0 / cmd.F1 : 0f, 0f, 0f, cmd.Style.FillOpacity);
                scale = new Vector3(cmd.F1 * 2f, height, cmd.F1 * 2f);
                break;
            case Kind.Sector:
                shape = DecalShape.Sector;
                shapeParams = new Vector4(cmd.F1, cmd.F2 > 1e-5f ? cmd.B.X / cmd.F2 : 0f, 0f, cmd.Style.FillOpacity);
                scale = new Vector3(cmd.F2 * 2f, height, cmd.F2 * 2f);
                facing = cmd.F0;
                break;
            case Kind.Rect:
                shape = DecalShape.Rect;
                shapeParams = new Vector4(0f, 0f, 0f, cmd.Style.FillOpacity);
                scale = new Vector3(cmd.F1, height, cmd.F2);
                facing = cmd.F0;
                break;
            case Kind.Chevron:
                shape = DecalShape.Chevron;
                shapeParams = new Vector4(cmd.B.X, 0f, 0f, cmd.Style.FillOpacity);
                scale = new Vector3(cmd.F1, height, cmd.F2);
                facing = cmd.F0;
                break;
            default:
                shape = DecalShape.Circle;
                shapeParams = new Vector4(0f, 0f, 0f, cmd.Style.FillOpacity);
                scale = new Vector3(cmd.F0 * 2f, height, cmd.F0 * 2f);
                break;
        }

        var world = Matrix4x4.CreateScale(scale)
                    * Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, facing)
                    * Matrix4x4.CreateTranslation(cmd.A);

        if (wireframe || outlineDecals)
            AddDecalOutline(pass, shape, shapeParams, in world, cmd.Color, cmd.Style.Layer, in frame, stats, depthAvailable);

        // Drawn even in wireframe.
        if (volumeDecals)
            AddDecalVolume(pass, in world, cmd.Color, cmd.Style.Layer, in frame, stats, depthAvailable);

        if (wireframe)
            return; // the pass drops decals in wireframe

        var mat = new MaterialData
        {
            Domain = MaterialDomain.GroundDecal,
            Blend = cmd.Style.Additive ? BlendMode.Additive : BlendMode.Premultiplied,
            Depth = DepthMode.TestOnly,
            WhenDepthUnavailable = DepthUnavailableBehavior.Hide,
            Cull = CullMode.Front,
            Params0 = shapeParams,
            Params1 = new Vector4(0f, (float)shape, cmd.Style.OutlineWidth, 1f),
            OutlineScaleRef = 0.5f * (scale.X + scale.Z),
            DecalOutlineColor = cmd.Style.OutlineColor,
        };

        pass.AddMeshItem(mesh, in mat, null, in world, cmd.Color, cmd.Style.Layer, castsDepth: false, stats, depthAvailable, cmd.Style.ExcludeVolumes);
    }

    // In wireframe nothing projects.
    private void AddDecalOutline(ScenePass pass, DecalShape shape, Vector4 shapeParams, in Matrix4x4 world, Vector4 color, int layer, in FrameContext frame, RenderStats stats, bool depthAvailable)
    {
        var path = outlinePath ??= new List<Vector3>(DecalOutline.Segments * 2 + 8);
        var style = new ImShapeStyle { Placement = ImShapePlacement.Flat, Layer = layer };
        var loops = DecalOutline.LoopCount(shape, shapeParams);
        for (var i = 0; i < loops; i++)
        {
            DecalOutline.BuildLoop(shape, shapeParams, in world, i, path);
            var needed = (path.Count + 1) * 2;
            if (pass.DynamicVertexBudget < needed)
            {
                stats.ImCommandsDropped++;
                return;
            }

            var verts = pass.DynVertices;
            var indices = pass.DynIndices;
            var startIndex = indices.Count;

            var center = Vector3.Zero;
            foreach (var p in path)
                center += p;
            center /= path.Count;

            var boundsRadius = OutlineWidth;
            foreach (var p in path)
                boundsRadius = MathF.Max(boundsRadius, Vector3.Distance(p, center) + OutlineWidth);

            WriteCameraRibbon(verts, indices, CollectionsMarshal.AsSpan(path), OutlineWidth, frame.EyePos, closed: true);
            var identity = Matrix4x4.Identity;
            var mat = FlatData(style, cullNone: true);
            pass.AddDynamicItem(startIndex, indices.Count - startIndex, in mat, color, in identity, layer, center, boundsRadius, stats, depthAvailable);
        }
    }

    private void AddDecalVolume(ScenePass pass, in Matrix4x4 world, Vector4 color, int layer, in FrameContext frame, RenderStats stats, bool depthAvailable)
    {
        Span<Vector3> corners = stackalloc Vector3[DecalOutline.VolumeCorners];
        DecalOutline.BuildVolumeCorners(in world, corners);

        var path = outlinePath ??= new List<Vector3>(DecalOutline.Segments * 2 + 8);
        var style = new ImShapeStyle { Placement = ImShapePlacement.Flat, Layer = layer };

        // Runs 0-1 are the face loops, 2-5 the verticals.
        for (var run = 0; run < 6; run++)
        {
            var closed = run < 2;
            path.Clear();
            if (closed)
            {
                for (var i = 0; i < 4; i++)
                    path.Add(corners[run * 4 + i]);
            }
            else
            {
                path.Add(corners[run - 2]);
                path.Add(corners[run - 2 + 4]);
            }

            var needed = (path.Count + 1) * 2;
            if (pass.DynamicVertexBudget < needed)
            {
                stats.ImCommandsDropped++;
                return;
            }

            var verts = pass.DynVertices;
            var indices = pass.DynIndices;
            var startIndex = indices.Count;

            var center = Vector3.Zero;
            foreach (var p in path)
                center += p;
            center /= path.Count;

            var boundsRadius = OutlineWidth;
            foreach (var p in path)
                boundsRadius = MathF.Max(boundsRadius, Vector3.Distance(p, center) + OutlineWidth);

            WriteCameraRibbon(verts, indices, CollectionsMarshal.AsSpan(path), OutlineWidth, frame.EyePos, closed);
            var identity = Matrix4x4.Identity;
            var mat = FlatData(style, cullNone: true);
            pass.AddDynamicItem(startIndex, indices.Count - startIndex, in mat, color, in identity, layer, center, boundsRadius, stats, depthAvailable);
        }
    }

    private static void AddFlatShape(ScenePass pass, ref Command cmd, RenderStats stats, bool depthAvailable)
    {
        if (cmd.Kind == Kind.Chevron)
        {
            AddFlatChevron(pass, ref cmd, stats, depthAvailable);
            return;
        }

        var segments = Math.Clamp(cmd.Style.Segments, 3, 256);
        var estimate = (segments + 2) * 2;
        if (pass.DynamicVertexBudget < estimate)
        {
            stats.ImCommandsDropped++;
            return;
        }

        var verts = pass.DynVertices;
        var indices = pass.DynIndices;
        var startIndex = indices.Count;
        float facing = 0f;
        float radius;

        switch (cmd.Kind)
        {
            case Kind.Donut:
                MeshBuilder.WriteRing(verts, indices, cmd.F0, cmd.F1, segments);
                radius = cmd.F1;
                break;
            case Kind.Circle:
                MeshBuilder.WriteDisc(verts, indices, cmd.F0, segments);
                radius = cmd.F0;
                break;
            case Kind.Sector:
                MeshBuilder.WriteSector(verts, indices, cmd.F1, cmd.B.X, cmd.F2, segments);
                facing = cmd.F0;
                radius = cmd.F2;
                break;
            default:
                MeshBuilder.WriteQuad(verts, indices, cmd.F1, cmd.F2);
                facing = cmd.F0;
                radius = MathF.Sqrt(cmd.F1 * cmd.F1 + cmd.F2 * cmd.F2) * 0.5f;
                break;
        }

        var world = Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, facing) * Matrix4x4.CreateTranslation(cmd.A);
        var mat = FlatData(cmd.Style, cullNone: true);
        pass.AddDynamicItem(startIndex, indices.Count - startIndex, in mat, cmd.Color, in world, cmd.Style.Layer, cmd.A, radius, stats, depthAvailable);
    }

    private static void AddFlatChevron(ScenePass pass, ref Command cmd, RenderStats stats, bool depthAvailable)
    {
        if (pass.DynamicVertexBudget < 6)
        {
            stats.ImCommandsDropped++;
            return;
        }

        Span<Vector2> corners = stackalloc Vector2[6];
        DecalOutline.ChevronOutline(cmd.B.X, corners);

        var verts = pass.DynVertices;
        var indices = pass.DynIndices;
        var startIndex = indices.Count;
        var baseVertex = verts.Count;

        foreach (var corner in corners)
            verts.Add(new Vertex3D(new Vector3(corner.X * 0.5f * cmd.F1, 0f, corner.Y * 0.5f * cmd.F2), Vector3.UnitY, Vector2.Zero, new Vector4(1f, 1f, 1f, 1f)));

        ReadOnlySpan<int> quads = [0, 1, 4, 0, 4, 5, 1, 2, 3, 1, 3, 4];
        foreach (var index in quads)
            indices.Add((ushort)(baseVertex + index));

        var world = Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, cmd.F0) * Matrix4x4.CreateTranslation(cmd.A);
        var mat = FlatData(cmd.Style, cullNone: true);
        var radius = MathF.Sqrt(cmd.F1 * cmd.F1 + cmd.F2 * cmd.F2) * 0.5f;
        pass.AddDynamicItem(startIndex, indices.Count - startIndex, in mat, cmd.Color, in world, cmd.Style.Layer, cmd.A, radius, stats, depthAvailable);
    }

    private void AddDynamicShape(ScenePass pass, ref Command cmd, Vector3[] paths, in FrameContext frame, RenderStats stats, bool depthAvailable)
    {
        var verts = pass.DynVertices;
        var indices = pass.DynIndices;
        var startIndex = indices.Count;
        Vector3 center;

        if (cmd.Kind == Kind.Arrow)
        {
            if (pass.DynamicVertexBudget < 160)
            {
                stats.ImCommandsDropped++;
                return;
            }

            var dir = cmd.B - cmd.A;
            var length = dir.Length();
            if (length < 1e-5f)
                return;

            MeshBuilder.WriteArrow(verts, indices, length, cmd.F0 * 0.5f, cmd.F0 * 1.4f, MathF.Min(length * 0.35f, cmd.F0 * 4f), 16);
            var world = RotationFromYTo(dir / length) * Matrix4x4.CreateTranslation(cmd.A);
            center = (cmd.A + cmd.B) * 0.5f;
            var arrowMat = FlatData(cmd.Style, cullNone: false);
            pass.AddDynamicItem(startIndex, indices.Count - startIndex, in arrowMat, cmd.Color, in world, cmd.Style.Layer, center, length * 0.5f + cmd.F0, stats, depthAvailable);
            return;
        }

        ReadOnlySpan<Vector3> points = cmd.Kind == Kind.Line
            ? stackalloc Vector3[2] { cmd.A, cmd.B }
            : paths.AsSpan(cmd.PathStart, cmd.PathCount);

        var neededVerts = (points.Length + (cmd.Closed ? 1 : 0)) * 2;
        if (pass.DynamicVertexBudget < neededVerts)
        {
            stats.ImCommandsDropped++;
            return;
        }

        center = Vector3.Zero;
        foreach (var p in points)
            center += p;
        center /= points.Length;

        var boundsRadius = cmd.F0;
        foreach (var p in points)
            boundsRadius = MathF.Max(boundsRadius, Vector3.Distance(p, center) + cmd.F0);

        WriteCameraRibbon(verts, indices, points, cmd.F0, frame.EyePos, cmd.Closed);
        var identity = Matrix4x4.Identity;
        var mat = FlatData(cmd.Style, cullNone: true);
        pass.AddDynamicItem(startIndex, indices.Count - startIndex, in mat, cmd.Color, in identity, cmd.Style.Layer, center, boundsRadius, stats, depthAvailable);
    }

    private static void WriteCameraRibbon(List<Vertex3D> verts, List<ushort> indices, ReadOnlySpan<Vector3> points, float width, Vector3 eye, bool closed)
    {
        var hw = width * 0.5f;
        var baseVertex = verts.Count;
        var count = points.Length;
        var stations = closed ? count + 1 : count;

        for (var i = 0; i < stations; i++)
        {
            var p = points[i % count];
            var prev = points[(i - 1 + count) % count];
            var next = points[(i + 1) % count];
            var dir = i == 0 && !closed ? next - p : i == count - 1 && !closed ? p - prev : next - prev;
            if (dir.LengthSquared() < 1e-10f)
                dir = Vector3.UnitZ;
            dir = Vector3.Normalize(dir);

            var toEye = eye - p;
            var side = Vector3.Cross(dir, toEye);
            side = side.LengthSquared() < 1e-10f ? Vector3.UnitY : Vector3.Normalize(side);
            var normal = Vector3.Normalize(Vector3.Cross(side, dir));

            var u = count > 1 ? (float)i / (stations - 1) : 0f;
            verts.Add(new Vertex3D(p - side * hw, normal, new Vector2(u, 0f), new Vector4(1f, 1f, 1f, 1f)));
            verts.Add(new Vertex3D(p + side * hw, normal, new Vector2(u, 1f), new Vector4(1f, 1f, 1f, 1f)));
        }

        for (var i = 0; i < stations - 1; i++)
        {
            var l0 = baseVertex + i * 2;
            var r0 = l0 + 1;
            var l1 = l0 + 2;
            var r1 = l0 + 3;
            indices.Add((ushort)l0);
            indices.Add((ushort)r0);
            indices.Add((ushort)r1);
            indices.Add((ushort)l0);
            indices.Add((ushort)r1);
            indices.Add((ushort)l1);
        }
    }

    private static MaterialData FlatData(ImShapeStyle style, bool cullNone) => new()
    {
        Domain = MaterialDomain.Unlit,
        Blend = style.Additive ? BlendMode.Additive : BlendMode.Premultiplied,
        Depth = style.IgnoreDepth ? DepthMode.Ignore : style.OnTopOfObjects ? DepthMode.WorldOnly : DepthMode.TestOnly,
        WhenDepthUnavailable = DepthUnavailableBehavior.Ignore,
        Cull = cullNone ? CullMode.None : CullMode.Back,
        UnorderedBatching = true, // identical shapes instance
        Params1 = new Vector4(style.DepthFade, 0f, 0f, 0f),
    };

    private static Matrix4x4 RotationFromYTo(Vector3 dir)
        => Matrix4x4.CreateFromQuaternion(TransformHelper.FromToRotation(Vector3.UnitY, dir));

    internal void DisposeResources()
    {
        unitBox?.Dispose();
        unitBox = null;
        unitSphere?.Dispose();
        unitSphere = null;
        lock (sync)
        {
            commands.Clear();
            pathPool.Clear();
        }
    }
}
