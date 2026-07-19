using System;

namespace NoireLib.UI;

/// <summary>
/// Which corners of a rectangle a <see cref="CornerShape"/> applies to. The ones left out stay square.
/// </summary>
/// <remarks>
/// Cutting two corners rather than four is most of what separates a deliberate shape from a rounded box, so the
/// diagonal pairs are named rather than left to be spelled out.
/// </remarks>
[Flags]
public enum RectCorners
{
    /// <summary>No corner is cut.</summary>
    None = 0,

    /// <summary>The top left corner.</summary>
    TopLeft = 1,

    /// <summary>The top right corner.</summary>
    TopRight = 2,

    /// <summary>The bottom right corner.</summary>
    BottomRight = 4,

    /// <summary>The bottom left corner.</summary>
    BottomLeft = 8,

    /// <summary>Both top corners.</summary>
    Top = TopLeft | TopRight,

    /// <summary>Both bottom corners.</summary>
    Bottom = BottomLeft | BottomRight,

    /// <summary>Both left corners.</summary>
    Left = TopLeft | BottomLeft,

    /// <summary>Both right corners.</summary>
    Right = TopRight | BottomRight,

    /// <summary>The top left and bottom right corners.</summary>
    Diagonal = TopLeft | BottomRight,

    /// <summary>The top right and bottom left corners.</summary>
    Antidiagonal = TopRight | BottomLeft,

    /// <summary>Every corner.</summary>
    All = TopLeft | TopRight | BottomRight | BottomLeft,
}
