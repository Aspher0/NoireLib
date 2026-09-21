using System.Numerics;

namespace NoireLib.Draw3D;

/// <summary>
/// The stylized half-Lambert lighting parameters of <see cref="Materials.MaterialDomain.Lit"/> materials, independent
/// of the game's lighting.
/// </summary>
public sealed class Draw3DLighting
{
    /// <summary>Gets or sets the ambient light color.</summary>
    public Vector3 AmbientColor { get; set; } = new(1f, 1f, 1f);

    /// <summary>Gets or sets the ambient light intensity, typically 0 to 1.</summary>
    public float AmbientIntensity { get; set; } = 0.45f;

    /// <summary>Gets or sets the direction <b>toward</b> the light source, normalized at upload.</summary>
    public Vector3 LightDirection { get; set; } = new(0.35f, 0.8f, 0.25f);

    /// <summary>Gets or sets the directional light color.</summary>
    public Vector3 LightColor { get; set; } = new(1f, 1f, 1f);

    /// <summary>Gets or sets the directional light intensity, typically 0 to 1.</summary>
    public float LightIntensity { get; set; } = 0.75f;
}
