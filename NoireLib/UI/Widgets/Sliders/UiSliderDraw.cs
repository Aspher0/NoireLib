using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Everything a <see cref="SliderStyle.CustomDraw"/> hook needs to paint a slider itself: where it is, how far along it
/// is, and what state it is in.
/// </summary>
/// <param name="Min">The top-left of the whole control, in screen pixels.</param>
/// <param name="Max">The bottom-right of the whole control, in screen pixels.</param>
/// <param name="TrackMin">The left end of the track, in screen pixels.</param>
/// <param name="TrackMax">The right end of the track, in screen pixels.</param>
/// <param name="GrabCenter">Where the handle sits, in screen pixels.</param>
/// <param name="Fraction">How far along the track the value is, from 0 to 1.</param>
/// <param name="Value">The value itself.</param>
/// <param name="Hovered">Whether the pointer is over the control.</param>
/// <param name="Held">Whether the handle is being dragged right now.</param>
/// <param name="Style">The resolved style, so a hook can honour the parts it does not want to replace.</param>
public readonly record struct UiSliderDraw(
    Vector2 Min,
    Vector2 Max,
    Vector2 TrackMin,
    Vector2 TrackMax,
    Vector2 GrabCenter,
    float Fraction,
    float Value,
    bool Hovered,
    bool Held,
    SliderStyle Style);
