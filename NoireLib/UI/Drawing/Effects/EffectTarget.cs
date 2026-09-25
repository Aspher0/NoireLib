namespace NoireLib.UI;

/// <summary>Which part of the drawing an effect applies to.</summary>
public enum EffectTarget
{
    /// <summary>Everything drawn inside the scope.</summary>
    All,

    /// <summary>Text and images, the parts drawn from a texture.</summary>
    Text,

    /// <summary>Fills and lines, the parts drawn without a texture.</summary>
    Shapes,
}
