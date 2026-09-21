using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Everything a <see cref="WindowMenuStyle.CustomDrawSlider"/> hook needs to paint one of the menu's sliders itself.
/// </summary>
/// <param name="TextStep">Whether it is the text size slider.</param>
/// <param name="Min">The top left of the slider's own box, between its label and its value, in screen pixels.</param>
/// <param name="Max">The bottom right of that box.</param>
/// <param name="Fraction">How far along the value is, from 0 to 1.</param>
/// <param name="Hovered">Whether the pointer is over it.</param>
/// <param name="Held">Whether it is being dragged.</param>
/// <param name="Fade">The menu's opening fade, from 0 to 1.</param>
/// <param name="Style">The resolved style.</param>
public readonly record struct UiWindowMenuSliderDraw(
    bool TextStep,
    Vector2 Min,
    Vector2 Max,
    float Fraction,
    bool Hovered,
    bool Held,
    float Fade,
    WindowMenuStyle Style);
