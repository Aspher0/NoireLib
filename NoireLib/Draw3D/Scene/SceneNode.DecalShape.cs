using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Im;
using NoireLib.Draw3D.Materials;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

public sealed partial class SceneNode
{
    private const float DefaultDecalShapeWidth = 0.03f;

    private static readonly ImShapeStyle DecalShapeEdgeStyle = new();

    // Per thread.
    [System.ThreadStatic]
    private static List<Vector3>? decalShapePath;

    // Straight alpha. Alpha 0 = off.
    private Vector4 decalShapeColor;

    private float decalShapeWidth = DefaultDecalShapeWidth;

    /// <summary>Whether the decal-shape outline is currently shown.</summary>
    public bool HasDecalShape => decalShapeColor.W > 0f;

    /// <summary>Shows a world-space outline tracing the shape this node's decal paints, or logs and does nothing without a decal material. Fluent.</summary>
    /// <param name="color">Outline color in straight alpha, or null for the decal's own color made opaque.</param>
    /// <param name="edgeWidth">Outline thickness in world units.</param>
    /// <returns>This node.</returns>
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
        DecalOverlayService.Register(this);
        return this;
    }

    /// <summary>Hides the decal-shape outline, if shown. Fluent.</summary>
    public SceneNode HideDecalShape()
    {
        decalShapeColor = default;
        if (!HasDecalVolume)
            DecalOverlayService.Unregister(this); // the volume box may still need the slot
        return this;
    }

    private void ReleaseDecalShape()
    {
        if (decalShapeColor.W <= 0f)
            return;

        decalShapeColor = default;
        if (!HasDecalVolume)
            DecalOverlayService.Unregister(this);
    }

    // Render thread. Force draws a node that did not opt in.
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

    private static Vector4 OpaqueOf(Vector4 color) => new(color.X, color.Y, color.Z, 1f);

    // Caller holds GraphLock.
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
