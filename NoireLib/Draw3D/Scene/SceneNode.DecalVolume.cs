using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Im;
using NoireLib.Draw3D.Materials;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// An opt-in wireframe of a decal's <b>projection box</b>: the oriented volume the shader evaluates its SDF inside,
/// traced as the box's twelve world-space edges. Where <see cref="ShowDecalShape"/> answers "what does this decal
/// paint", this answers "how far does its projection reach" - the box sweeps above and below the surface, and only what
/// falls inside it can be painted at all, so it is the aid for sizing the sweep and seeing why a decal stops short of a
/// wall or a step.
/// <br/>
/// The two are independent and compose: turn both on to see the painted shape sitting inside the volume that produced
/// it. Like the shape outline, this is re-emitted every frame through the immediate layer (camera-facing, so the edges
/// stay crisp and are never distorted by the decal's non-uniform scale).
/// </summary>
public sealed partial class SceneNode
{
    /// <summary>Default volume-edge width, in world units. Thinner than the shape outline so the box reads as scaffolding behind it.</summary>
    private const float DefaultDecalVolumeWidth = 0.02f;

    /// <summary>The immediate-layer style for the box edges: a world-depth-tested line, so the volume reads as a real frame in the scene.</summary>
    private static readonly ImShapeStyle DecalVolumeEdgeStyle = new();

    /// <summary>Reusable face-loop buffer. Render-thread only (see <see cref="DecalOverlayService"/>), so one per thread keeps the per-frame trace allocation-free.</summary>
    [System.ThreadStatic]
    private static List<Vector3>? decalVolumePath;

    /// <summary>The volume-edge color (straight alpha); alpha 0 = the opt-in box is off. Driven by <see cref="ShowDecalVolume"/> / <see cref="HideDecalVolume"/>.</summary>
    private Vector4 decalVolumeColor;

    /// <summary>The volume-edge width, in world units.</summary>
    private float decalVolumeWidth = DefaultDecalVolumeWidth;

    /// <summary>Whether the decal-volume box is currently shown (its color's alpha &gt; 0).</summary>
    public bool HasDecalVolume => decalVolumeColor.W > 0f;

    /// <summary>
    /// Shows the decal's projection box as a wireframe - the twelve edges of the volume the shader tests against, so you
    /// can see exactly how far the projection reaches above and below the surface. Toggle it back off with
    /// <see cref="HideDecalVolume"/>; calling it again updates the color / width. Fluent.<br/>
    /// It follows the node's transform and the decal's <see cref="DecalSurface"/> constraint live, so it tracks the decal
    /// through any edit. No-op (logged) when the node carries no decal material.
    /// </summary>
    /// <param name="color">Edge color, straight alpha (alpha &gt; 0 to be visible). Null uses the decal's own color, made opaque.</param>
    /// <param name="edgeWidth">Edge thickness in world units (default 0.02).</param>
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

    /// <summary>Hides the decal-volume box, if shown. Fluent.</summary>
    public SceneNode HideDecalVolume()
    {
        decalVolumeColor = default;
        if (!HasDecalShape)
            DecalOverlayService.Unregister(this); // the shape outline may still need the per-frame slot
        return this;
    }

    /// <summary>Stops the decal-volume box and drops the node from the service when nothing else needs it (called on destroy).</summary>
    private void ReleaseDecalVolume()
    {
        if (decalVolumeColor.W <= 0f)
            return;

        decalVolumeColor = default;
        if (!HasDecalShape)
            DecalOverlayService.Unregister(this);
    }

    /// <summary>
    /// Emits this node's decal-volume box into the immediate layer for this frame. Render-thread only, driven off
    /// <see cref="NoireDraw3D.OnRenderOverlay"/> by <see cref="DecalOverlayService"/> (the opt-in path) or by
    /// <see cref="Scene3D.TraceDecalVolumes"/> (the master toggle). Reads the world matrix under the graph lock and skips
    /// a destroyed, hidden, or no-longer-decal node.
    /// </summary>
    /// <param name="im">The immediate layer to draw into.</param>
    /// <param name="force">
    /// Trace even when this node never opted in, using the decal's own color - what the master toggle needs, since it must
    /// show every decal rather than only the ones an author flagged. An explicit <see cref="ShowDecalVolume"/> color still wins.
    /// </param>
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

        // The two face loops, then the four verticals joining them - the box's twelve edges.
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
