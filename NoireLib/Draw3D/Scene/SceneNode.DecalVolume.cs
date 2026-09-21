using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Im;
using NoireLib.Draw3D.Materials;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

public sealed partial class SceneNode
{
    private const float DefaultDecalVolumeWidth = 0.02f;

    private static readonly ImShapeStyle DecalVolumeEdgeStyle = new();

    // Per thread.
    [System.ThreadStatic]
    private static List<Vector3>? decalVolumePath;

    // Straight alpha. Alpha 0 = off.
    private Vector4 decalVolumeColor;

    private float decalVolumeWidth = DefaultDecalVolumeWidth;

    /// <summary>Whether the decal projection box is currently shown.</summary>
    public bool HasDecalVolume => decalVolumeColor.W > 0f;

    /// <summary>Shows the edges of the decal's projection box, or logs and does nothing without a decal material. Fluent.</summary>
    /// <param name="color">Edge color in straight alpha, or null for the decal's own color made opaque.</param>
    /// <param name="edgeWidth">Edge thickness in world units.</param>
    /// <returns>This node.</returns>
    public SceneNode ShowDecalVolume(Vector4? color = null, float edgeWidth = DefaultDecalVolumeWidth)
    {
        if (Renderer?.Material is not { Domain: MaterialDomain.GroundDecal } decalMat)
        {
            NoireLogger.LogWarning($"Draw3D: SceneNode '{Name ?? "(unnamed)"}'.ShowDecalVolume on a node with no decal material - ignored. Give it a Material.Decal(...) first.", "Draw3D");
            return this;
        }

        decalVolumeColor = color ?? OpaqueOf(decalMat.Color);
        if (decalVolumeColor.W <= 0f)
            decalVolumeColor.W = 1f;

        decalVolumeWidth = edgeWidth > 0f ? edgeWidth : DefaultDecalVolumeWidth;
        DecalOverlayService.Register(this);
        return this;
    }

    /// <summary>Hides the decal projection box, if shown. Fluent.</summary>
    public SceneNode HideDecalVolume()
    {
        decalVolumeColor = default;
        if (!HasDecalShape)
            DecalOverlayService.Unregister(this); // the shape outline may still need the slot
        return this;
    }

    private void ReleaseDecalVolume()
    {
        if (decalVolumeColor.W <= 0f)
            return;

        decalVolumeColor = default;
        if (!HasDecalShape)
            DecalOverlayService.Unregister(this);
    }

    // Render thread. Force draws a node that did not opt in.
    internal void DrawDecalVolumeEdges(ImDraw3D im, bool force = false)
    {
        Vector4 color;
        float width;
        Matrix4x4 world;
        lock (Scene3D.GraphLock)
        {
            if (Destroyed || !IsEffectivelyVisibleNoLock())
                return;

            if (!force && decalVolumeColor.W <= 0f)
                return;

            if (Renderer?.Material is not { Domain: MaterialDomain.GroundDecal } decalMat)
                return;

            color = decalVolumeColor.W > 0f ? decalVolumeColor : OpaqueOf(decalMat.Color);
            width = decalVolumeWidth;
            world = ResolveWorld();
        }

        Span<Vector3> corners = stackalloc Vector3[DecalOutline.VolumeCorners];
        DecalOutline.BuildVolumeCorners(in world, corners);

        var path = decalVolumePath ??= new List<Vector3>(4);

        for (var face = 0; face < 2; face++)
        {
            path.Clear();
            for (var i = 0; i < 4; i++)
                path.Add(corners[face * 4 + i]);
            im.DrawPath(path, width, color, closed: true, DecalVolumeEdgeStyle);
        }

        for (var i = 0; i < 4; i++)
            im.DrawLine(corners[i], corners[i + 4], width, color, DecalVolumeEdgeStyle);
    }
}
