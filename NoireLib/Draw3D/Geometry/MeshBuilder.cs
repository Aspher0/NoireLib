using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Geometry;

/// <summary>
/// Procedural mesh catalog. Every builder returns CPU data that is unit-sized around the origin,
/// +Y up, clockwise-front winding, outward normals, UVs in [0,1] - scale and orient via the scene node.<br/>
/// Vertex order is deterministic per shape so tests can assert exact counts and windings.
/// </summary>
public static class MeshBuilder
{
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);

    /// <summary>Builds a flat quad on the XZ plane, normal +Y. 4 vertices / 6 indices.</summary>
    /// <param name="width">Extent along X.</param>
    /// <param name="depth">Extent along Z.</param>
    public static MeshData Quad(float width = 1f, float depth = 1f)
    {
        var v = new List<Vertex3D>(4);
        var i = new List<ushort>(6);
        WriteQuad(v, i, width, depth);
        return new MeshData(v.ToArray(), i.ToArray());
    }

    internal static void WriteQuad(List<Vertex3D> verts, List<ushort> indices, float width, float depth)
    {
        int b = verts.Count;
        float hx = width * 0.5f, hz = depth * 0.5f;
        var n = Vector3.UnitY;
        verts.Add(new Vertex3D(new Vector3(-hx, 0, -hz), n, new Vector2(0, 0), White));
        verts.Add(new Vertex3D(new Vector3(+hx, 0, -hz), n, new Vector2(1, 0), White));
        verts.Add(new Vertex3D(new Vector3(+hx, 0, +hz), n, new Vector2(1, 1), White));
        verts.Add(new Vertex3D(new Vector3(-hx, 0, +hz), n, new Vector2(0, 1), White));
        AddTri(indices, b, 0, 1, 2);
        AddTri(indices, b, 0, 2, 3);
    }

    /// <summary>Builds an axis-aligned box centered on the origin. 24 vertices (per-face normals) / 36 indices.</summary>
    /// <param name="size">Full extents per axis; null = unit cube.</param>
    public static MeshData Box(Vector3? size = null)
    {
        var v = new List<Vertex3D>(24);
        var i = new List<ushort>(36);
        WriteBox(v, i, size ?? Vector3.One);
        return new MeshData(v.ToArray(), i.ToArray());
    }

    internal static void WriteBox(List<Vertex3D> verts, List<ushort> indices, Vector3 size)
    {
        var h = size * 0.5f;
        // (normal, right, up) triplets chosen so cross(right, up) == normal, which yields clockwise-front quads.
        WriteBoxFace(verts, indices, new Vector3(+1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, +1, 0), h);
        WriteBoxFace(verts, indices, new Vector3(-1, 0, 0), new Vector3(0, 0, +1), new Vector3(0, +1, 0), h);
        WriteBoxFace(verts, indices, new Vector3(0, +1, 0), new Vector3(+1, 0, 0), new Vector3(0, 0, -1), h);
        WriteBoxFace(verts, indices, new Vector3(0, -1, 0), new Vector3(+1, 0, 0), new Vector3(0, 0, +1), h);
        WriteBoxFace(verts, indices, new Vector3(0, 0, +1), new Vector3(+1, 0, 0), new Vector3(0, +1, 0), h);
        WriteBoxFace(verts, indices, new Vector3(0, 0, -1), new Vector3(-1, 0, 0), new Vector3(0, +1, 0), h);
    }

    private static void WriteBoxFace(List<Vertex3D> verts, List<ushort> indices, Vector3 n, Vector3 right, Vector3 up, Vector3 half)
    {
        int b = verts.Count;
        var c = n * half;
        var r = right * half;
        var u = up * half;
        verts.Add(new Vertex3D(c - r + u, n, new Vector2(0, 0), White));
        verts.Add(new Vertex3D(c + r + u, n, new Vector2(1, 0), White));
        verts.Add(new Vertex3D(c + r - u, n, new Vector2(1, 1), White));
        verts.Add(new Vertex3D(c - r - u, n, new Vector2(0, 1), White));
        AddTri(indices, b, 0, 1, 2);
        AddTri(indices, b, 0, 2, 3);
    }

    /// <summary>Builds a flat disc on the XZ plane (triangle fan around a center vertex). segments+2 vertices / segments*3 indices.</summary>
    /// <param name="radius">Disc radius.</param>
    /// <param name="segments">Number of outer segments (≥ 3).</param>
    public static MeshData Disc(float radius = 0.5f, int segments = 48)
    {
        var v = new List<Vertex3D>(segments + 2);
        var i = new List<ushort>(segments * 3);
        WriteDisc(v, i, radius, segments);
        return new MeshData(v.ToArray(), i.ToArray());
    }

    internal static void WriteDisc(List<Vertex3D> verts, List<ushort> indices, float radius, int segments)
    {
        segments = Math.Max(3, segments);
        int b = verts.Count;
        var n = Vector3.UnitY;
        verts.Add(new Vertex3D(Vector3.Zero, n, new Vector2(0.5f, 0.5f), White));
        for (int k = 0; k <= segments; k++)
        {
            var (sin, cos) = MathF.SinCos(k * MathF.Tau / segments);
            verts.Add(new Vertex3D(new Vector3(cos * radius, 0, sin * radius), n, new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f), White));
        }

        for (int k = 0; k < segments; k++)
            AddTri(indices, b, 0, 1 + k, 2 + k);
    }

    /// <summary>Builds a flat ring (donut) on the XZ plane. 2*(segments+1) vertices / segments*6 indices.</summary>
    /// <param name="innerRadius">Inner radius.</param>
    /// <param name="outerRadius">Outer radius.</param>
    /// <param name="segments">Number of segments (≥ 3).</param>
    public static MeshData Ring(float innerRadius, float outerRadius, int segments = 64)
    {
        var v = new List<Vertex3D>((segments + 1) * 2);
        var i = new List<ushort>(segments * 6);
        WriteRing(v, i, innerRadius, outerRadius, segments);
        return new MeshData(v.ToArray(), i.ToArray());
    }

    internal static void WriteRing(List<Vertex3D> verts, List<ushort> indices, float innerRadius, float outerRadius, int segments)
    {
        segments = Math.Max(3, segments);
        int b = verts.Count;
        var n = Vector3.UnitY;
        for (int k = 0; k <= segments; k++)
        {
            var (sin, cos) = MathF.SinCos(k * MathF.Tau / segments);
            float u = (float)k / segments;
            verts.Add(new Vertex3D(new Vector3(cos * outerRadius, 0, sin * outerRadius), n, new Vector2(u, 0), White));
            verts.Add(new Vertex3D(new Vector3(cos * innerRadius, 0, sin * innerRadius), n, new Vector2(u, 1), White));
        }

        for (int k = 0; k < segments; k++)
        {
            int o0 = k * 2, i0 = k * 2 + 1, o1 = k * 2 + 2, i1 = k * 2 + 3;
            AddTri(indices, b, o0, o1, i1);
            AddTri(indices, b, o0, i1, i0);
        }
    }

    /// <summary>Builds a flat ring slice on the XZ plane, centered on local +Z (matching the decal SDF orientation). 2*(segments+1) vertices / segments*6 indices.</summary>
    /// <param name="halfAngleRad">Half of the slice's opening angle, in radians.</param>
    /// <param name="innerRadius">Inner radius (0 for a full pie slice).</param>
    /// <param name="outerRadius">Outer radius.</param>
    /// <param name="segments">Number of arc segments (≥ 1).</param>
    public static MeshData Sector(float halfAngleRad, float innerRadius, float outerRadius, int segments = 32)
    {
        var v = new List<Vertex3D>((segments + 1) * 2);
        var i = new List<ushort>(segments * 6);
        WriteSector(v, i, halfAngleRad, innerRadius, outerRadius, segments);
        return new MeshData(v.ToArray(), i.ToArray());
    }

    internal static void WriteSector(List<Vertex3D> verts, List<ushort> indices, float halfAngleRad, float innerRadius, float outerRadius, int segments)
    {
        segments = Math.Max(1, segments);
        int b = verts.Count;
        var n = Vector3.UnitY;
        for (int k = 0; k <= segments; k++)
        {
            float phi = -halfAngleRad + k * (2f * halfAngleRad / segments);   // angle from +Z, increasing toward +X
            var (sin, cos) = MathF.SinCos(phi);
            float u = (float)k / segments;
            verts.Add(new Vertex3D(new Vector3(sin * outerRadius, 0, cos * outerRadius), n, new Vector2(u, 0), White));
            verts.Add(new Vertex3D(new Vector3(sin * innerRadius, 0, cos * innerRadius), n, new Vector2(u, 1), White));
        }

        for (int k = 0; k < segments; k++)
        {
            // The (sin, cos) parametrization runs the opposite angular direction to Disc's (cos, sin),
            // so the strip triangles flip to keep clockwise-front with normal +Y.
            int o0 = k * 2, i0 = k * 2 + 1, o1 = k * 2 + 2, i1 = k * 2 + 3;
            AddTri(indices, b, o1, o0, i0);
            AddTri(indices, b, o1, i0, i1);
        }
    }

    /// <summary>Builds a UV sphere. (stacks+1)*(slices+1) vertices / stacks*slices*6 indices (pole quads degenerate harmlessly).</summary>
    /// <param name="radius">Sphere radius.</param>
    /// <param name="slices">Longitudinal segments (≥ 3).</param>
    /// <param name="stacks">Latitudinal segments (≥ 2).</param>
    public static MeshData Sphere(float radius = 0.5f, int slices = 24, int stacks = 16)
    {
        var v = new List<Vertex3D>((stacks + 1) * (slices + 1));
        var i = new List<ushort>(stacks * slices * 6);
        WriteSphere(v, i, radius, slices, stacks);
        return new MeshData(v.ToArray(), i.ToArray());
    }

    internal static void WriteSphere(List<Vertex3D> verts, List<ushort> indices, float radius, int slices, int stacks)
    {
        slices = Math.Max(3, slices);
        stacks = Math.Max(2, stacks);
        int b = verts.Count;
        for (int i = 0; i <= stacks; i++)
        {
            float theta = MathF.PI * i / stacks;            // 0 at +Y pole
            var (st, ct) = MathF.SinCos(theta);
            for (int j = 0; j <= slices; j++)
            {
                float phi = MathF.Tau * j / slices;
                var (sp, cp) = MathF.SinCos(phi);
                var n = new Vector3(st * cp, ct, st * sp);
                verts.Add(new Vertex3D(n * radius, n, new Vector2((float)j / slices, (float)i / stacks), White));
            }
        }

        int stride = slices + 1;
        for (int i = 0; i < stacks; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                int a = i * stride + j, c = (i + 1) * stride + j + 1;
                AddTri(indices, b, a, c, a + 1);
                AddTri(indices, b, a, (i + 1) * stride + j, c);
            }
        }
    }

    /// <summary>Builds a cylinder along Y, centered on the origin.</summary>
    /// <param name="radius">Cylinder radius.</param>
    /// <param name="height">Cylinder height.</param>
    /// <param name="segments">Radial segments (≥ 3).</param>
    /// <param name="caps">Whether to close the top and bottom.</param>
    public static MeshData Cylinder(float radius = 0.5f, float height = 1f, int segments = 24, bool caps = true)
    {
        var v = new List<Vertex3D>();
        var i = new List<ushort>();
        WriteCylinder(v, i, radius, height, segments, caps);
        return new MeshData(v.ToArray(), i.ToArray());
    }

    internal static void WriteCylinder(List<Vertex3D> verts, List<ushort> indices, float radius, float height, int segments, bool caps)
    {
        segments = Math.Max(3, segments);
        int b = verts.Count;
        float hy = height * 0.5f;
        for (int k = 0; k <= segments; k++)
        {
            var (sin, cos) = MathF.SinCos(k * MathF.Tau / segments);
            var n = new Vector3(cos, 0, sin);
            float u = (float)k / segments;
            verts.Add(new Vertex3D(new Vector3(cos * radius, +hy, sin * radius), n, new Vector2(u, 0), White));
            verts.Add(new Vertex3D(new Vector3(cos * radius, -hy, sin * radius), n, new Vector2(u, 1), White));
        }

        for (int k = 0; k < segments; k++)
        {
            int t0 = k * 2, b0 = k * 2 + 1, t1 = k * 2 + 2, b1 = k * 2 + 3;
            AddTri(indices, b, t0, b0, b1);
            AddTri(indices, b, t0, b1, t1);
        }

        if (caps)
        {
            WriteCap(verts, indices, radius, +hy, segments, up: true);
            WriteCap(verts, indices, radius, -hy, segments, up: false);
        }
    }

    private static void WriteCap(List<Vertex3D> verts, List<ushort> indices, float radius, float y, int segments, bool up)
    {
        int b = verts.Count;
        var n = up ? Vector3.UnitY : -Vector3.UnitY;
        verts.Add(new Vertex3D(new Vector3(0, y, 0), n, new Vector2(0.5f, 0.5f), White));
        for (int k = 0; k <= segments; k++)
        {
            var (sin, cos) = MathF.SinCos(k * MathF.Tau / segments);
            verts.Add(new Vertex3D(new Vector3(cos * radius, y, sin * radius), n, new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f), White));
        }

        for (int k = 0; k < segments; k++)
        {
            if (up) AddTri(indices, b, 0, 1 + k, 2 + k);
            else AddTri(indices, b, 0, 2 + k, 1 + k);
        }
    }

    /// <summary>Builds a cone along Y (apex at +height/2, base at -height/2).</summary>
    /// <param name="radius">Base radius.</param>
    /// <param name="height">Cone height.</param>
    /// <param name="segments">Radial segments (≥ 3).</param>
    /// <param name="cap">Whether to close the base.</param>
    public static MeshData Cone(float radius = 0.5f, float height = 1f, int segments = 24, bool cap = true)
    {
        var v = new List<Vertex3D>();
        var i = new List<ushort>();
        WriteCone(v, i, radius, height, segments, cap);
        return new MeshData(v.ToArray(), i.ToArray());
    }

    internal static void WriteCone(List<Vertex3D> verts, List<ushort> indices, float radius, float height, int segments, bool cap)
    {
        segments = Math.Max(3, segments);
        int b = verts.Count;
        float hy = height * 0.5f;
        float slant = MathF.Sqrt(height * height + radius * radius);
        // Per-segment apex vertices keep smooth radial normals around the rim.
        for (int k = 0; k <= segments; k++)
        {
            var (sin, cos) = MathF.SinCos(k * MathF.Tau / segments);
            var n = new Vector3(height * cos / slant, radius / slant, height * sin / slant);
            float u = (float)k / segments;
            verts.Add(new Vertex3D(new Vector3(0, +hy, 0), n, new Vector2(u, 0), White));
            verts.Add(new Vertex3D(new Vector3(cos * radius, -hy, sin * radius), n, new Vector2(u, 1), White));
        }

        for (int k = 0; k < segments; k++)
        {
            int a0 = k * 2, b0 = k * 2 + 1, b1 = k * 2 + 3;
            AddTri(indices, b, a0, b0, b1);
        }

        if (cap)
            WriteCap(verts, indices, radius, -hy, segments, up: false);
    }

    /// <summary>Builds a 3D torus (donut) around the Y axis. (segMajor+1)*(segMinor+1) vertices / segMajor*segMinor*6 indices.</summary>
    /// <param name="majorRadius">Distance from the origin to the tube center.</param>
    /// <param name="minorRadius">Tube radius.</param>
    /// <param name="segMajor">Segments around the main ring (≥ 3).</param>
    /// <param name="segMinor">Segments around the tube (≥ 3).</param>
    public static MeshData Torus(float majorRadius, float minorRadius, int segMajor = 48, int segMinor = 16)
    {
        var v = new List<Vertex3D>((segMajor + 1) * (segMinor + 1));
        var i = new List<ushort>(segMajor * segMinor * 6);
        WriteTorus(v, i, majorRadius, minorRadius, segMajor, segMinor);
        return new MeshData(v.ToArray(), i.ToArray());
    }

    internal static void WriteTorus(List<Vertex3D> verts, List<ushort> indices, float majorRadius, float minorRadius, int segMajor, int segMinor)
    {
        segMajor = Math.Max(3, segMajor);
        segMinor = Math.Max(3, segMinor);
        int b = verts.Count;
        for (int i = 0; i <= segMajor; i++)
        {
            float theta = MathF.Tau * i / segMajor;
            var (st, ct) = MathF.SinCos(theta);
            for (int j = 0; j <= segMinor; j++)
            {
                float phi = MathF.Tau * j / segMinor;
                var (sp, cp) = MathF.SinCos(phi);
                var n = new Vector3(cp * ct, sp, cp * st);
                var p = new Vector3((majorRadius + minorRadius * cp) * ct, minorRadius * sp, (majorRadius + minorRadius * cp) * st);
                verts.Add(new Vertex3D(p, n, new Vector2((float)i / segMajor, (float)j / segMinor), White));
            }
        }

        int stride = segMinor + 1;
        for (int i = 0; i < segMajor; i++)
        {
            for (int j = 0; j < segMinor; j++)
            {
                int a = i * stride + j, bb = i * stride + j + 1, c = (i + 1) * stride + j + 1, d = (i + 1) * stride + j;
                AddTri(indices, b, a, c, bb);
                AddTri(indices, b, a, d, c);
            }
        }
    }

    /// <summary>Builds an arrow along +Y: shaft cylinder + cone head, base at the origin, tip at +length.</summary>
    /// <param name="length">Total arrow length.</param>
    /// <param name="shaftRadius">Shaft radius.</param>
    /// <param name="headRadius">Head base radius.</param>
    /// <param name="headLength">Head length (clamped below <paramref name="length"/>).</param>
    /// <param name="segments">Radial segments (≥ 3).</param>
    public static MeshData Arrow(float length = 1f, float shaftRadius = 0.05f, float headRadius = 0.12f, float headLength = 0.25f, int segments = 16)
    {
        var v = new List<Vertex3D>();
        var i = new List<ushort>();
        WriteArrow(v, i, length, shaftRadius, headRadius, headLength, segments);
        return new MeshData(v.ToArray(), i.ToArray());
    }

    internal static void WriteArrow(List<Vertex3D> verts, List<ushort> indices, float length, float shaftRadius, float headRadius, float headLength, int segments)
    {
        headLength = MathF.Min(headLength, length * 0.9f);
        float shaftLen = length - headLength;

        // Shaft: cylinder is Y-centered, so offset vertices up by half the shaft length after writing.
        int shaftStart = verts.Count;
        WriteCylinder(verts, indices, shaftRadius, shaftLen, segments, caps: true);
        for (int k = shaftStart; k < verts.Count; k++)
        {
            var vv = verts[k];
            vv.Position.Y += shaftLen * 0.5f;
            verts[k] = vv;
        }

        int headStart = verts.Count;
        WriteCone(verts, indices, headRadius, headLength, segments, cap: true);
        for (int k = headStart; k < verts.Count; k++)
        {
            var vv = verts[k];
            vv.Position.Y += shaftLen + headLength * 0.5f;
            verts[k] = vv;
        }
    }

    /// <summary>
    /// Builds a flat ribbon along a polyline (mitered corners, beveled above a ~150° turn to avoid spikes).<br/>
    /// The ribbon lies flat (+Y normal); point Y coordinates are honored, with the width applied horizontally.
    /// </summary>
    /// <param name="points">Polyline points (≥ 2).</param>
    /// <param name="width">Ribbon width.</param>
    /// <param name="closed">Whether the last point connects back to the first.</param>
    public static MeshData ExtrudePath(IReadOnlyList<Vector3> points, float width, bool closed = false)
    {
        var v = new List<Vertex3D>();
        var i = new List<ushort>();
        WriteExtrudePath(v, i, points, width, closed);
        return new MeshData(v.ToArray(), i.ToArray());
    }

    /// <summary>Miter-to-bevel switch: when the miter would extend beyond this factor of the half-width (turn sharper than ~150°), a bevel joint is emitted instead.</summary>
    internal const float MiterLimit = 3.8637f; // 1/sin(15°) - miter length at a 150° turn

    internal static void WriteExtrudePath(List<Vertex3D> verts, List<ushort> indices, IReadOnlyList<Vector3> points, float width, bool closed)
    {
        int count = points.Count;
        if (count < 2)
            return;

        int b = verts.Count;
        float hw = width * 0.5f;
        var n = Vector3.UnitY;
        int segLimit = closed ? count : count - 1;

        // Left/right pair per emitted station; bevel joints emit two stations at the same point.
        float pathLen = 0f;
        for (int k = 0; k < segLimit; k++)
            pathLen += HorizontalDistance(points[k], points[(k + 1) % count]);
        if (pathLen <= 0f)
            pathLen = 1f;

        float traveled = 0f;
        var firstStation = -1;
        int prevStation = -1;

        for (int k = 0; k < count; k++)
        {
            var p = points[k];
            var hasPrev = closed || k > 0;
            var hasNext = closed || k < count - 1;
            var dirPrev = hasPrev ? HorizontalDirection(points[(k - 1 + count) % count], p) : HorizontalDirection(p, points[(k + 1) % count]);
            var dirNext = hasNext ? HorizontalDirection(p, points[(k + 1) % count]) : dirPrev;
            var perpPrev = new Vector3(-dirPrev.Z, 0, dirPrev.X);
            var perpNext = new Vector3(-dirNext.Z, 0, dirNext.X);

            float u = traveled / pathLen;
            var miterDir = perpPrev + perpNext;
            var miterLen = miterDir.Length();
            bool bevel;
            Vector3 offset = default;
            if (miterLen < 1e-4f)
            {
                bevel = true; // 180° turn - no miter direction exists
            }
            else
            {
                miterDir /= miterLen;
                var denom = Vector3.Dot(miterDir, perpPrev);
                bevel = denom < 1f / MiterLimit;
                if (!bevel)
                    offset = miterDir * (hw / denom);
            }

            if (bevel && hasPrev && hasNext)
            {
                // Two stations: one aligned to the incoming segment, one to the outgoing.
                int s0 = AddStation(verts, p, perpPrev * hw, n, u);
                if (prevStation >= 0) AddStrip(indices, b, prevStation, s0);
                int s1 = AddStation(verts, p, perpNext * hw, n, u);
                AddStrip(indices, b, s0, s1);
                prevStation = s1;
                if (firstStation < 0) firstStation = s0;
            }
            else
            {
                if (bevel)
                    offset = perpPrev * hw;
                int s = AddStation(verts, p, offset, n, u);
                if (prevStation >= 0) AddStrip(indices, b, prevStation, s);
                prevStation = s;
                if (firstStation < 0) firstStation = s;
            }

            if (k < count - 1)
                traveled += HorizontalDistance(p, points[k + 1]);
        }

        if (closed && prevStation >= 0 && firstStation >= 0)
            AddStrip(indices, b, prevStation, firstStation);

        static int AddStation(List<Vertex3D> verts, Vector3 p, Vector3 halfOffset, Vector3 n, float u)
        {
            int station = verts.Count;
            verts.Add(new Vertex3D(p - halfOffset, n, new Vector2(u, 0), White)); // left
            verts.Add(new Vertex3D(p + halfOffset, n, new Vector2(u, 1), White)); // right
            return station;
        }

        static void AddStrip(List<ushort> indices, int b, int s0, int s1)
        {
            // Station vertex 0 sits at p - perp·hw (the +X side on a +Z path), so clockwise-front
            // with normal +Y is (v0, r1, r0) / (v0, v0', r1).
            int l0 = s0 - b, r0 = s0 - b + 1, l1 = s1 - b, r1 = s1 - b + 1;
            AddTri(indices, b, l0, r1, r0);
            AddTri(indices, b, l0, l1, r1);
        }
    }

    private static Vector3 HorizontalDirection(Vector3 from, Vector3 to)
    {
        var d = to - from;
        d.Y = 0;
        var len = d.Length();
        return len > 1e-6f ? d / len : Vector3.UnitZ;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 c)
    {
        var d = c - a;
        d.Y = 0;
        return d.Length();
    }

    private static void AddTri(List<ushort> indices, int baseVertex, int a, int b, int c)
    {
        indices.Add(checked((ushort)(baseVertex + a)));
        indices.Add(checked((ushort)(baseVertex + b)));
        indices.Add(checked((ushort)(baseVertex + c)));
    }
}
