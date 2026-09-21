using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// Resolves the identifiers a game material uses for its samplers and constants. Each identifier is a CRC32 of
/// the name. An identifier missing from <see cref="Known"/> still parses and only lacks a label.
/// </summary>
public static class GameShaderNames
{
    /// <summary>Sampler and constant names known to this library.</summary>
    public static readonly IReadOnlyList<string> Known =
    [
        "g_SamplerNormal", "g_SamplerIndex", "g_SamplerDiffuse", "g_SamplerSpecular",
        "g_SamplerMask", "g_SamplerTable", "g_SamplerColorMap0", "g_SamplerColorMap1",
        "g_SamplerNormalMap0", "g_SamplerNormalMap1", "g_SamplerSpecularMap0", "g_SamplerSpecularMap1",
        "g_SamplerEnvMap", "g_SamplerReflection", "g_SamplerWaveMap", "g_SamplerWhitecapMap",
        "g_SamplerFlow", "g_SamplerLightDiffuse", "g_SamplerLightSpecular", "g_SamplerGBuffer",
        "g_MaterialParameter", "g_DiffuseColor", "g_SpecularColor", "g_EmissiveColor",
        "g_NormalScale", "g_AlphaThreshold", "g_Shininess", "g_TileScale", "g_TileIndex",
    ];

    private static readonly Dictionary<uint, string> ByIdMap = BuildIdMap();

    /// <summary>Identifier of a shader name.</summary>
    /// <param name="name">The name, for example <c>g_SamplerDiffuse</c>.</param>
    public static uint IdOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Span<byte> ascii = name.Length <= 256 ? stackalloc byte[name.Length] : new byte[name.Length];
        for (var i = 0; i < name.Length; i++)
            ascii[i] = (byte)name[i];

        // Zero initial value and no final inversion.
        return Crc32Helper.Compute(ascii, seed: 0u, finalXor: 0u);
    }

    /// <summary>The name behind an identifier, or null when it is not one of the known names.</summary>
    /// <param name="id">The identifier taken from a material.</param>
    public static string? NameOf(uint id) => ByIdMap.GetValueOrDefault(id);

    private static Dictionary<uint, string> BuildIdMap()
    {
        var map = new Dictionary<uint, string>(Known.Count);
        foreach (var name in Known)
            map[IdOf(name)] = name;

        return map;
    }
}
