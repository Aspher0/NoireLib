using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Im;
using NoireLib.Draw3D.Materials;
using System;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// An opt-in wireframe of a ground-decal's projection volume: the oriented box the decal projects through, drawn as its
/// twelve world-space edges so the exact footprint and sweep depth are visible in 3D. A debugging / authoring aid - turn
/// it on to place and size a decal, off to ship. The edges are re-emitted every frame through the immediate layer
/// (camera-facing, so they stay crisp and are never distorted by the box's own non-uniform scale) and depth-test against
/// the game world like any other 3D object.
/// </summary>
public sealed partial class SceneNode
{
    /// <summary>Default decal-box edge width, in world units.</summary>
    private const float DefaultDecalBoxWidth = 0.03f;

    /// <summary>The immediate-layer style for the box edges: a flat, world-depth-tested line, so the box reads as a real object standing in the world.</summary>
    private static readonly ImShapeStyle DecalBoxEdgeStyle = new() { Placement = ImShapePlacement.Flat };

    /// <summary>The twelve box edges as corner-index pairs (corners differ in exactly one sign bit): four along each local axis.</summary>
    private static readonly (int A, int B)[] BoxEdges =
    {
        (0, 1), (2, 3), (4, 5), (6, 7), // along local X
        (0, 2), (1, 3), (4, 6), (5, 7), // along local Y
        (0, 4), (1, 5), (2, 6), (3, 7), // along local Z
    };

    /// <summary>The box-edge color (straight alpha); alpha 0 = the box is off. Driven by <see cref="ShowDecalBox"/> / <see cref="HideDecalBox"/>.</summary>
    private Vector4 decalBoxColor;

    /// <summary>The box-edge width, in world units.</summary>
    private float decalBoxWidth = DefaultDecalBoxWidth;

    /// <summary>Whether the decal-box wireframe is currently shown (its color's alpha &gt; 0).</summary>
    public bool HasDecalBox => decalBoxColor.W > 0f;

    /// <summary>
    /// Shows a wireframe box tracing this node's decal projection volume - the oriented unit box the
    /// <see cref="MaterialDomain.GroundDecal"/> material projects through, in world space. Toggle it back off with
    /// <see cref="HideDecalBox"/>; calling it again updates the color / width. Fluent.<br/>
    /// The box mirrors the same <see cref="DecalSurface"/> constraint the decal renders with, so it lands exactly on the
    /// painted footprint. No-op (logged) when the node has no renderer.
    /// </summary>
    /// <param name="color">Edge color, straight alpha (alpha &gt; 0 to be visible). Null uses the decal's own color, made opaque.</param>
    /// <param name="edgeWidth">Edge thickness in world units (default 0.03).</param>
    public SceneNode ShowDecalBox(Vector4? color = null, float edgeWidth = DefaultDecalBoxWidth)
    {
        var renderer = Renderer;
        if (renderer == null)
        {
            NoireLogger.LogWarning($"Draw3D: SceneNode '{Name ?? "(unnamed)"}'.ShowDecalBox with no renderer - ignored. Attach a decal mesh first.", "Draw3D");
            return this;
        }

        var tint = renderer.Material.Color;
        var resolved = color ?? new Vector4(tint.X, tint.Y, tint.Z, 1f);
        if (resolved.W <= 0f)
            resolved.W = 1f;

        decalBoxColor = resolved;
        decalBoxWidth = edgeWidth > 0f ? edgeWidth : DefaultDecalBoxWidth;
        DecalBoxService.Register(this);
        return this;
    }

    /// <summary>Hides the decal-box wireframe, if shown. Fluent.</summary>
    public SceneNode HideDecalBox()
    {
        decalBoxColor = default;
        DecalBoxService.Unregister(this);
        return this;
    }

    /// <summary>Stops the decal-box wireframe and drops the node from the service (called on destroy).</summary>
    private void ReleaseDecalBox()
    {
        if (decalBoxColor.W <= 0f)
            return;

        decalBoxColor = default;
        DecalBoxService.Unregister(this);
    }

    /// <summary>
    /// Emits the twelve world-space edges of this node's decal box into the immediate layer for this frame. Render-thread
    /// only (driven by <see cref="DecalBoxService"/> off <see cref="NoireDraw3D.OnRenderOverlay"/>). Resolves the world
    /// matrix under the graph lock and skips a destroyed, hidden, or turned-off node.
    /// </summary>
    /// <param name="im">The immediate layer to draw into.</param>
    internal void DrawDecalBoxEdges(ImDraw3D im)
    {
        Vector4 color;
        float width;
        Matrix4x4 world;
        lock (Scene3D.GraphLock)
        {
            if (Destroyed || decalBoxColor.W <= 0f || !IsEffectivelyVisibleNoLock())
                return;

            color = decalBoxColor;
            width = decalBoxWidth;
            world = ResolveWorld();
        }

        // The eight corners of the unit projection box (local +/-0.5), transformed to world space.
        Span<Vector3> corners = stackalloc Vector3[8];
        for (var i = 0; i < 8; i++)
        {
            var local = new Vector3(
                (i & 1) == 0 ? -0.5f : 0.5f,
                (i & 2) == 0 ? -0.5f : 0.5f,
                (i & 4) == 0 ? -0.5f : 0.5f);
            corners[i] = Vector3.Transform(local, world);
        }

        foreach (var (a, b) in BoxEdges)
            im.DrawLine(corners[a], corners[b], width, color, DecalBoxEdgeStyle);
    }

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
