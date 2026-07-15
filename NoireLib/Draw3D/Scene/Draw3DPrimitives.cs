using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Materials;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// Primitive-shape shortcuts over <see cref="Scene3D.Spawn(MeshData, Material, Vector3, string, bool)"/> - one call
/// per <see cref="MeshBuilder"/> primitive. Extension methods so the scene core stays lean; each is two lines over
/// <c>Spawn(MeshBuilder.X(...), ...)</c> and never locks you in - pass any <see cref="MeshData"/> / <see cref="Mesh"/>
/// to <c>Spawn</c> for anything not in this list.
/// </summary>
public static class Draw3DPrimitives
{
    /// <summary>Spawns a unit box (owned mesh). Fluent-chainable via the returned node's transform setters.</summary>
    public static SceneNode AddBox(this Scene3D scene, Material material, Vector3 position = default, string? name = null, bool keepCpuData = false)
        => scene.Spawn(MeshBuilder.Box(), material, position, name, keepCpuData);

    /// <summary>Spawns a box with explicit extents (owned mesh).</summary>
    public static SceneNode AddBox(this Scene3D scene, Vector3 size, Material material, Vector3 position = default, string? name = null, bool keepCpuData = false)
        => scene.Spawn(MeshBuilder.Box(size), material, position, name, keepCpuData);

    /// <summary>Spawns a UV sphere (owned mesh).</summary>
    public static SceneNode AddSphere(this Scene3D scene, float radius, Material material, Vector3 position = default, string? name = null, bool keepCpuData = false)
        => scene.Spawn(MeshBuilder.Sphere(radius), material, position, name, keepCpuData);

    /// <summary>Spawns a flat quad on the XZ plane (owned mesh).</summary>
    public static SceneNode AddQuad(this Scene3D scene, float width, float depth, Material material, Vector3 position = default, string? name = null, bool keepCpuData = false)
        => scene.Spawn(MeshBuilder.Quad(width, depth), material, position, name, keepCpuData);

    /// <summary>Spawns a flat disc on the XZ plane (owned mesh) - the natural base for a circular ground decal.</summary>
    public static SceneNode AddDisc(this Scene3D scene, float radius, Material material, Vector3 position = default, string? name = null, bool keepCpuData = false)
        => scene.Spawn(MeshBuilder.Disc(radius), material, position, name, keepCpuData);

    /// <summary>Spawns a flat ring (donut) on the XZ plane (owned mesh).</summary>
    public static SceneNode AddRing(this Scene3D scene, float innerRadius, float outerRadius, Material material, Vector3 position = default, string? name = null, bool keepCpuData = false)
        => scene.Spawn(MeshBuilder.Ring(innerRadius, outerRadius), material, position, name, keepCpuData);

    /// <summary>Spawns a cylinder along Y (owned mesh).</summary>
    public static SceneNode AddCylinder(this Scene3D scene, float radius, float height, Material material, Vector3 position = default, string? name = null, bool keepCpuData = false)
        => scene.Spawn(MeshBuilder.Cylinder(radius, height), material, position, name, keepCpuData);

    /// <summary>Spawns a cone along Y (owned mesh).</summary>
    public static SceneNode AddCone(this Scene3D scene, float radius, float height, Material material, Vector3 position = default, string? name = null, bool keepCpuData = false)
        => scene.Spawn(MeshBuilder.Cone(radius, height), material, position, name, keepCpuData);

    /// <summary>Spawns a 3D torus around the Y axis (owned mesh).</summary>
    public static SceneNode AddTorus(this Scene3D scene, float majorRadius, float minorRadius, Material material, Vector3 position = default, string? name = null, bool keepCpuData = false)
        => scene.Spawn(MeshBuilder.Torus(majorRadius, minorRadius), material, position, name, keepCpuData);

    /// <summary>Spawns an arrow along +Y, base at the origin (owned mesh).</summary>
    public static SceneNode AddArrow(this Scene3D scene, float length, Material material, Vector3 position = default, string? name = null, bool keepCpuData = false)
        => scene.Spawn(MeshBuilder.Arrow(length), material, position, name, keepCpuData);
}
