namespace NoireLib.Draw3D.Materials;

/// <summary>
/// How a material's pixels blend into the Draw3D layer.<br/>
/// Everything inside Draw3D is premultiplied-alpha end to end; blending is always (ONE, INV_SRC_ALPHA).
/// </summary>
public enum BlendMode
{
    /// <summary>No blending. Renders in the opaque bucket, writes the private depth buffer, and occlusion by the world is a hard pixel kill.</summary>
    Opaque = 0,

    /// <summary>Standard translucent "over" blending (premultiplied); the default for markers and translucent shapes.</summary>
    Premultiplied = 1,

    /// <summary>Additive blending for emissive/energy effects: adds light, contributes no occlusion to the layer's alpha.</summary>
    Additive = 2,
}
