using Dalamud.Interface.Textures.TextureWraps;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>A ULD part resolved to its loaded texture and the UVs that cut it out.</summary>
/// <param name="Texture">The shared texture the part lives on, which must not be disposed.</param>
/// <param name="Uv0">The part's top left corner, normalised.</param>
/// <param name="Uv1">The part's bottom right corner, normalised.</param>
/// <param name="Size">The part's size, in texture pixels.</param>
public readonly record struct UldPartTexture(IDalamudTextureWrap Texture, Vector2 Uv0, Vector2 Uv1, Vector2 Size);
