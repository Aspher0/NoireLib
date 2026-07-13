using System.Numerics;

namespace NoireLib.Draw3D;

/// <summary>
/// Stylized lighting parameters consumed by <see cref="Materials.MaterialDomain.Lit"/> materials
/// (half-Lambert). Deliberately independent of the game's lighting — a clean stylized look beats an
/// uncanny mismatch we have no authority over.
/// </summary>
public sealed class Draw3DLighting
{
    /// <summary>Ambient light color.</summary>
    public Vector3 AmbientColor { get; set; } = new(1f, 1f, 1f);

    /// <summary>Ambient light intensity (0..1 typical).</summary>
    public float AmbientIntensity { get; set; } = 0.45f;

    /// <summary>Direction <b>toward</b> the light source (normalized at upload).</summary>
    public Vector3 LightDirection { get; set; } = new(0.35f, 0.8f, 0.25f);

    /// <summary>Directional light color.</summary>
    public Vector3 LightColor { get; set; } = new(1f, 1f, 1f);

    /// <summary>Directional light intensity (0..1 typical).</summary>
    public float LightIntensity { get; set; } = 0.75f;
}
