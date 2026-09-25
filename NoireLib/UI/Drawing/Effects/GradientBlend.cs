namespace NoireLib.UI;

/// <summary>How a gradient's color combines with the color already drawn.</summary>
public enum GradientBlend
{
    /// <summary>The gradient's color replaces the drawn one, keeping the drawn alpha and its smooth edges.</summary>
    Replace,

    /// <summary>The two colors are multiplied, which tints the drawing.</summary>
    Multiply,

    /// <summary>The gradient's color is added, which makes the drawing glow.</summary>
    Add,

    /// <summary>Lightens by the gradient, never darker than the drawing.</summary>
    Screen,

    /// <summary>Darkens the dark parts and lightens the light ones, keeping the drawing's contrast.</summary>
    Overlay,
}
