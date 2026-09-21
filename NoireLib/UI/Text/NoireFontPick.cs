namespace NoireLib.UI;

/// <summary>
/// The face a <see cref="NoireFontFamily"/> draws a weight with, and what it takes to stand in for that weight.
/// </summary>
/// <param name="Face">The face.</param>
/// <param name="Weight">The weight the family actually carries.</param>
/// <param name="SyntheticBoldPx">
/// How far to smear the glyphs to stand in for a bold the family has no face for, in logical pixels. 0 when the face
/// already carries the weight.
/// </param>
public readonly record struct NoireFontPick(NoireFont Face, int Weight, float SyntheticBoldPx);
