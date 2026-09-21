using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A switch's hint, handed to <see cref="WindowMenuStyle.CustomShowHint"/>.
/// </summary>
/// <param name="Toggle">The switch under the pointer.</param>
/// <param name="Text">Its hint.</param>
/// <param name="Min">The top left of the switch, in screen pixels.</param>
/// <param name="Max">The bottom right of the switch.</param>
public readonly record struct UiWindowMenuHint(WindowMenuToggle Toggle, string Text, Vector2 Min, Vector2 Max);
