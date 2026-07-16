using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Im;
using NoireLib.Draw3D.Materials;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// An opt-in wireframe of a decal's painted shape: the circle, ring, pie or rectangle the material's SDF actually paints,
/// traced as a closed world-space line lying on the decal's own plane. A placement / sizing aid - turn it on to position
/// and size a decal by eye, off to ship. The line is re-emitted every frame through the immediate layer (camera-facing,
/// so it stays crisp and is never distorted by the decal's non-uniform scale).
/// <br/>
/// It traces the shape rather than the projection volume deliberately. That volume is an oriented box whose footprint is
/// the SDF's <i>bounding square</i> and whose sweep runs well above and below the painted surface, so for anything but a
/// full-footprint circle it is far larger than the paint and centered where the paint is not - a pie's box is centered on
/// its apex and spans twice its radius - which reads as stray lines crossing the view rather than as the decal.
/// </summary>
public sealed partial class SceneNode
{
    /// <summary>Default shape-outline width, in world units.</summary>
    private const float DefaultDecalShapeWidth = 0.03f;

    /// <summary>The immediate-layer style for the outline: a world-depth-tested line, so it reads as a real marking on the surface.</summary>
    private static readonly ImShapeStyle DecalShapeEdgeStyle = new();

    /// <summary>Reusable point buffer for the outline loops. Render-thread only (see <see cref="DecalShapeService"/>), so one per thread costs nothing and keeps the per-frame trace allocation-free.</summary>
    [System.ThreadStatic]
    private static List<Vector3>? decalShapePath;

    /// <summary>The outline color (straight alpha); alpha 0 = the opt-in outline is off. Driven by <see cref="ShowDecalShape"/> / <see cref="HideDecalShape"/>.</summary>
    private Vector4 decalShapeColor;

    /// <summary>The outline width, in world units.</summary>
    private float decalShapeWidth = DefaultDecalShapeWidth;

    /// <summary>Whether the decal-shape outline is currently shown (its color's alpha &gt; 0).</summary>
    public bool HasDecalShape => decalShapeColor.W > 0f;

    /// <summary>
    /// Shows a wireframe outline tracing the shape this node's decal paints - the same SDF the
    /// <see cref="MaterialDomain.GroundDecal"/> shader evaluates, so the line lands exactly on the painted edge. Toggle it
    /// back off with <see cref="HideDecalShape"/>; calling it again updates the color / width. Fluent.<br/>
    /// It follows the material's <see cref="Material.Shape"/> and <see cref="Material.ShapeParams"/> live, and mirrors the
    /// decal's <see cref="DecalSurface"/> constraint, so it tracks the decal through any edit. No-op (logged) when the
    /// node carries no decal material.
    /// </summary>
    /// <param name="color">Outline color, straight alpha (alpha &gt; 0 to be visible). Null uses the decal's own color, made opaque.</param>
    /// <param name="edgeWidth">Outline thickness in world units (default 0.03).</param>
    public SceneNode ShowDecalShape(Vector4? color = null, float edgeWidth = DefaultDecalShapeWidth)
    {
        if (Renderer?.Material is not { Domain: MaterialDomain.GroundDecal } decalMat)
        {
            NoireLogger.LogWarning($"Draw3D: SceneNode '{Name ?? "(unnamed)"}'.ShowDecalShape on a node with no decal material - ignored. Give it a Material.Decal(...) first.", "Draw3D");
            return this;
        }

        decalShapeColor = color ?? OpaqueOf(decalMat.Color);
        if (decalShapeColor.W <= 0f)
            decalShapeColor.W = 1f;

        decalShapeWidth = edgeWidth > 0f ? edgeWidth : DefaultDecalShapeWidth;
        DecalShapeService.Register(this);
        return this;
    }

    /// <summary>Hides the decal-shape outline, if shown. Fluent.</summary>
    public SceneNode HideDecalShape()
    {
        decalShapeColor = default;
        DecalShapeService.Unregister(this);
        return this;
    }

    /// <summary>Stops the decal-shape outline and drops the node from the service (called on destroy).</summary>
    private void ReleaseDecalShape()
    {
        if (decalShapeColor.W <= 0f)
            return;

        decalShapeColor = default;
        DecalShapeService.Unregister(this);
    }

    /// <summary>
    /// Emits this node's decal-shape outline into the immediate layer for this frame. Render-thread only, driven off
    /// <see cref="NoireDraw3D.OnRenderOverlay"/> by <see cref="DecalShapeService"/> (the opt-in path) or by
    /// <see cref="Scene3D.TraceDecalShapes"/> (wireframe mode). Reads the shape and world matrix under the graph lock and
    /// skips a destroyed, hidden, or no-longer-decal node.
    /// </summary>
    /// <param name="im">The immediate layer to draw into.</param>
    /// <param name="force">
    /// Trace even when this node never opted in, using the decal's own color - what wireframe mode needs, since it must
    /// show every decal rather than only the ones an author flagged. An explicit <see cref="ShowDecalShape"/> color still wins.
    /// </param>
    internal void DrawDecalShapeEdges(ImDraw3D im, bool force = false)
    {
        Vector4 color;
        float width;
        DecalShape shape;
        Vector4 shapeParams;
        Matrix4x4 world;
        lock (Scene3D.GraphLock)
        {
            if (Destroyed || !IsEffectivelyVisibleNoLock())
                return;

            if (!force && decalShapeColor.W <= 0f)
                return;

            if (Renderer?.Material is not { Domain: MaterialDomain.GroundDecal } decalMat)
                return;

            color = decalShapeColor.W > 0f ? decalShapeColor : OpaqueOf(decalMat.Color);
            width = decalShapeWidth;
            shape = decalMat.Shape;
            shapeParams = decalMat.ShapeParams;
            world = ResolveWorld();
        }

        var path = decalShapePath ??= new List<Vector3>(DecalOutline.Segments * 2 + 8);
        var loops = DecalOutline.LoopCount(shape, shapeParams);
        for (var i = 0; i < loops; i++)
        {
            DecalOutline.BuildLoop(shape, shapeParams, in world, i, path);
            im.DrawPath(path, width, color, closed: true, DecalShapeEdgeStyle);
        }
    }

    /// <summary>A decal color at full alpha - the outline's default, so it reads as the decal it traces.</summary>
    private static Vector4 OpaqueOf(Vector4 color) => new(color.X, color.Y, color.Z, 1f);

    /// <summary>Effective visibility: this node and every ancestor is visible. Caller holds <see cref="Scene3D.GraphLock"/>.</summary>
    private bool IsEffectivelyVisibleNoLock()
    {
        for (var n = this; n != null; n = n.parent)
        {
            if (!n.Visible)
                return false;
        }

        return true;
    }
}
