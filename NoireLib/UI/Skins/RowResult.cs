using System.Numerics;

namespace NoireLib.UI;

/// <summary>What the mouse did to a list row this frame, and where it was drawn.</summary>
/// <param name="Clicked">Clicked with the left button. Never true on a marked row.</param>
/// <param name="RightClicked">Clicked with the right button.</param>
/// <param name="Hovered">Under the mouse.</param>
/// <param name="Pressed">Pressed this frame, for a drag source.</param>
/// <param name="Min">The row's top left corner, in screen pixels.</param>
/// <param name="Max">The row's bottom right corner.</param>
public readonly record struct RowResult(bool Clicked, bool RightClicked, bool Hovered, bool Pressed, Vector2 Min, Vector2 Max);
