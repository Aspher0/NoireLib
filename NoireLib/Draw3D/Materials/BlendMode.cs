namespace NoireLib.Draw3D.Materials;

/// <summary>How a material's pixels blend into the premultiplied-alpha Draw3D layer.</summary>
public enum BlendMode
{
    /// <summary>No blending. Renders in the opaque bucket and writes the layer's depth buffer.</summary>
    Opaque = 0,

    /// <summary>Standard translucent premultiplied blending (the default).</summary>
    Premultiplied = 1,

    /// <summary>Additive blending that adds light without contributing to the layer's alpha.</summary>
    Additive = 2,
}
