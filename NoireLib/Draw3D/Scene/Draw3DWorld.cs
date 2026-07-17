using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.World;
using System;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// World-collision shortcuts: turn the game's real collision (terrain, background models, furniture, dynamic objects)
/// into scene geometry in one call. Thin sugar over <see cref="WorldGeometry"/> + <see cref="Scene3D.Spawn(Geometry.Vertex3D[], uint[], Material, Vector3, string?, bool)"/>.<br/>
/// <b>Framework thread only</b> - these read the live collision scene (see <see cref="WorldGeometry"/>).
/// </summary>
public static class Draw3DWorld
{
    /// <summary>
    /// Spawns the collision geometry within <paramref name="radius"/> of <paramref name="center"/> as a node the scene
    /// owns (flat-shaded). Returns the node, or null when no collision is under the query (or off the framework thread).
    /// A debugging / world-preview aid - the same source <see cref="SpawnWorldDecal"/> projects onto.
    /// </summary>
    /// <param name="scene">The target scene.</param>
    /// <param name="center">World-space query centre.</param>
    /// <param name="radius">Half-size of the cubic query volume.</param>
    /// <param name="material">Material to draw the collision with (e.g. a wireframe-friendly translucent <see cref="Material.Lit(Vector4, float)"/>).</param>
    /// <param name="includeAnalytic">Include box/cylinder/sphere/plane colliders (invisible walls / triggers), not just mesh models.</param>
    /// <param name="name">Optional node name.</param>
    /// <param name="keepCpuData">Retain CPU geometry for picking.</param>
    public static SceneNode? SpawnWorldGeometry(this Scene3D scene, Vector3 center, float radius, Material material, bool includeAnalytic = true, string? name = null, bool keepCpuData = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(material);

        var collected = WorldGeometry.Collect(center, radius, includeAnalytic: includeAnalytic);
        if (collected is not { } geo)
            return null;

        return scene.Spawn(geo.Vertices, geo.Indices, material, geo.Center, name, keepCpuData);
    }

    /// <summary>
    /// Projects a decal footprint onto the real world surface near <paramref name="center"/> and spawns it as a node the
    /// scene owns - it conforms to terrain, walls and furniture (unlike the screen-space <see cref="Material.Decal"/>).
    /// Give it a translucent <b>textured</b> material (the texture's alpha is the footprint) or a flat translucent colour.
    /// Returns the node, or null when nothing is under the footprint.
    /// </summary>
    /// <param name="scene">The target scene.</param>
    /// <param name="center">World-space centre of the footprint (roughly on the surface).</param>
    /// <param name="normal">Surface-outward direction the decal faces (e.g. <c>Vector3.UnitY</c> for a floor).</param>
    /// <param name="width">Footprint size along the decal U axis.</param>
    /// <param name="height">Footprint size along the decal V axis.</param>
    /// <param name="material">Translucent textured/coloured material. <c>Cull</c> is forced to <see cref="CullMode.None"/>.</param>
    /// <param name="depth">Thickness of the projection volume along <paramref name="normal"/>.</param>
    /// <param name="includeAnalytic">Also project onto analytic colliders (default false: real surfaces only).</param>
    /// <param name="name">Optional node name.</param>
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
