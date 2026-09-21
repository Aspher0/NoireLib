namespace NoireLib.Helpers;

/// <summary>Binds one of the material's textures to a named shader sampler.</summary>
/// <param name="SamplerId">Identifier of the sampler, resolvable through <see cref="GameShaderNames"/>.</param>
/// <param name="Flags">Address modes, level-of-detail bias and minimum level, packed.</param>
/// <param name="TextureIndex">Index into the material's texture list.</param>
public readonly record struct GameMaterialSampler(uint SamplerId, uint Flags, byte TextureIndex);
