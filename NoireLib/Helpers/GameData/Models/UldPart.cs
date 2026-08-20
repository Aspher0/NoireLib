using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>Where one part of a ULD part list sits on its texture.</summary>
/// <param name="TexturePath">The game path of the texture the part is cut from.</param>
/// <param name="Position">The part's top left corner, in texture pixels.</param>
/// <param name="Size">The part's size, in texture pixels.</param>
public readonly record struct UldPart(string TexturePath, Vector2 Position, Vector2 Size);
