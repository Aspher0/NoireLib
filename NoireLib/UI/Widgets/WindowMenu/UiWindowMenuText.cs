using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A run of text a <see cref="NoireWindowMenu"/> draws or measures, handed to <see cref="WindowMenuStyle.CustomDrawText"/>.
/// </summary>
/// <param name="Text">The text.</param>
/// <param name="Role">Which piece of the menu it is.</param>
/// <param name="BoxMin">The top left of the box it sits in, in screen pixels.</param>
/// <param name="BoxMax">The bottom right of that box. A note wraps to its width.</param>
/// <param name="Align">Where it sits horizontally in the box. It is always centred vertically.</param>
/// <param name="Color">The colour, with the menu's fade applied.</param>
/// <param name="SizePx">The size at 100%, with <see cref="WindowMenuStyle.TextScale"/> applied.</param>
/// <param name="TrackingEm">Extra space per character, in ems.</param>
/// <param name="LineHeight">The line height, as a multiple of the size.</param>
public readonly record struct UiWindowMenuText(
    string Text,
    WindowMenuTextRole Role,
    Vector2 BoxMin,
    Vector2 BoxMax,
    UiAlign Align,
    Vector4 Color,
    float SizePx,
    float TrackingEm,
    float LineHeight);
