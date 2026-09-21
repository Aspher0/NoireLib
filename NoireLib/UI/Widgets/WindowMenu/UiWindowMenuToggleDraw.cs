using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Everything a <see cref="WindowMenuStyle.CustomDrawToggle"/> hook needs to paint a switch of the menu itself.
/// </summary>
/// <param name="Toggle">Which switch it is.</param>
/// <param name="Label">Its label.</param>
/// <param name="Min">The top left of the cell, in screen pixels.</param>
/// <param name="Max">The bottom right of the cell.</param>
/// <param name="On">Whether the switch is on.</param>
/// <param name="Hovered">Whether the pointer is over it.</param>
/// <param name="Held">Whether it is being pressed.</param>
/// <param name="OnProgress">How far its "on" look has eased in, from 0 to 1.</param>
/// <param name="HoverProgress">How far its hover look has eased in, from 0 to 1.</param>
/// <param name="Fade">The menu's opening fade, from 0 to 1.</param>
/// <param name="Style">The resolved style.</param>
public readonly record struct UiWindowMenuToggleDraw(
    WindowMenuToggle Toggle,
    string Label,
    Vector2 Min,
    Vector2 Max,
    bool On,
    bool Hovered,
    bool Held,
    float OnProgress,
    float HoverProgress,
    float Fade,
    WindowMenuStyle Style);
