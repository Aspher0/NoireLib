using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.World;
using System;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

/// <summary>Shortcuts turning the game's live collision into scene geometry through <see cref="WorldGeometry"/>. Framework thread only.</summary>
public static class Draw3DWorld
{
    /// <summary>Spawns the collision geometry within <paramref name="radius"/> of <paramref name="center"/> as a flat-shaded node.</summary>
    /// <param name="scene">The target scene.</param>
    /// <param name="center">World-space query centre.</param>
    /// <param name="radius">Half-size of the cubic query volume.</param>
    /// <param name="material">Material to draw the collision with.</param>
    /// <param name="includeAnalytic">Whether to include box, cylinder, sphere and plane colliders.</param>
    /// <param name="name">Optional node name.</param>
    /// <param name="keepCpuData">Retain CPU geometry for picking.</param>
    /// <returns>The node, or null when no collision is found or the call is off the framework thread.</returns>
    public static SceneNode? SpawnWorldGeometry(this Scene3D scene, Vector3 center, float radius, Material material, bool includeAnalytic = true, string? name = null, bool keepCpuData = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(material);

        var collected = WorldGeometry.Collect(center, radius, includeAnalytic: includeAnalytic);
        if (collected is not { } geo)
            return null;

        return scene.Spawn(geo.Vertices, geo.Indices, material, geo.Center, name, keepCpuData);
    }
    /// <summary>Projects a decal footprint onto the world collision near <paramref name="center"/> and spawns it as a conforming mesh node the scene owns.</summary>
    /// <param name="scene">The target scene.</param>
    /// <param name="center">World-space centre of the footprint (roughly on the surface).</param>
    /// <param name="normal">Surface-outward direction the decal faces (e.g. <c>Vector3.UnitY</c> for a floor).</param>
    /// <param name="width">Footprint size along the decal U axis.</param>
    /// <param name="height">Footprint size along the decal V axis.</param>
    /// <param name="material">Translucent textured or coloured material, drawn with <see cref="CullMode.None"/>.</param>
    /// <param name="depth">Thickness of the projection volume along <paramref name="normal"/>.</param>
    /// <param name="includeAnalytic">Whether to also project onto analytic colliders.</param>
    /// <param name="name">Optional node name.</param>
    /// <returns>The node, or null when nothing is under the footprint.</returns>
    public static SceneNode? SpawnWorldDecal(this Scene3D scene, Vector3 center, Vector3 normal, float width, float height, Material material, float depth = 2f, bool includeAnalytic = false, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(material);

        var mesh = WorldGeometry.ProjectDecal(center, normal, width, height, depth, includeAnalytic: includeAnalytic);
        if (mesh is not { } data)
            return null;

        return scene.Spawn(data, material with { Cull = CullMode.None }, center, name);
    }
}
