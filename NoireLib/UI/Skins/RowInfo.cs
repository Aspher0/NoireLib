using System.Numerics;

namespace NoireLib.UI;

/// <summary>What one <see cref="IControlSkin.Row"/> shows.</summary>
/// <param name="Label">The main text.</param>
/// <param name="Detail">A second, muted text on the right.</param>
/// <param name="GameIcon">A game icon on the left, or 0.</param>
/// <param name="Selected">Whether it is selected.</param>
/// <param name="Marked">Whether it is already taken, as a picker's added item.</param>
/// <param name="Note">The note shown when marked.</param>
/// <param name="Tint">An accent for the hover and selection, or <see langword="null"/> for the theme's.</param>
public readonly record struct RowInfo(string Label, string? Detail = null, uint GameIcon = 0, bool Selected = false, bool Marked = false, string? Note = null, Vector4? Tint = null);
