using System.Numerics;

namespace NoireLib.UI;

/// <summary>Where a row's control goes.</summary>
/// <param name="Min">The top left corner, in screen pixels.</param>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="Disabled">Whether the control is disabled.</param>
public readonly record struct SettingRowArea(Vector2 Min, float Width, float Height, bool Disabled);
