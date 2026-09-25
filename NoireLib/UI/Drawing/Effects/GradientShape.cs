namespace NoireLib.UI;

/// <summary>How a point of the drawing is placed along a gradient.</summary>
public enum GradientShape
{
    /// <summary>Along a straight line at the gradient's angle, the first color on one side and the last on the other.</summary>
    Linear,

    /// <summary>Like <see cref="Linear"/>, mirrored around the centre: the first color in the middle, the last on both sides.</summary>
    Reflected,

    /// <summary>By distance from the centre: the first color there, the last at the edge of an ellipse filling the area.</summary>
    Radial,

    /// <summary>By angle around the centre, one full turn from the first color back to the last, like a color wheel.</summary>
    Conic,

    /// <summary>By distance from the centre measured along both axes added, which draws diamonds.</summary>
    Diamond,

    /// <summary>By distance from the centre measured along the longer axis, which draws nested rectangles.</summary>
    Square,

    /// <summary>By a smooth random pattern over the area, like marble or clouds.</summary>
    Noise,

    /// <summary>By the rank of each character in the text rather than by its position.</summary>
    PerGlyph,

    /// <summary>By the line each piece of text sits on, one color per line.</summary>
    PerLine,

    /// <summary>By a function of the point's position within the area.</summary>
    Custom,
}
