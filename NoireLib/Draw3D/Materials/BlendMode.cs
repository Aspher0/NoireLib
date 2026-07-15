namespace NoireLib.Draw3D.Materials;

/// <summary>
/// How a material's pixels blend into the Draw3D layer.<br/>
/// Everything inside Draw3D is premultiplied-alpha end to end (Law 4).
/// </summary>
public enum BlendMode
{
    /// <summary>No blending. Renders in the opaque bucket, writes the private depth buffer, and occlusion by the world is a hard pixel kill.</summary>
    Opaque = 0,

    /// <summary>Standard translucent "over" blending (premultiplied). The default for markers and translucent shapes.</summary>
    Premultiplied = 1,

    /// <summary>Additive blending - adds light, contributes no occlusion to the layer's alpha. For emissive/energy effects.</summary>
    Additive = 2,
}
