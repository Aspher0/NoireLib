using Lumina.Data;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D.Assets;

/// <summary>A texture slot of a game material.</summary>
/// <param name="Path">Archive path of the texture.</param>
/// <param name="Flags">Slot flags; bit 0x8000 marks the DirectX 11 variant of the file.</param>
public readonly record struct GameMaterialTexture(string Path, ushort Flags)
{
    /// <summary>Whether this slot refers to the DirectX 11 variant of the texture.</summary>
    public bool IsDx11 => (Flags & 0x8000) != 0;
}

/// <summary>Binds one of the material's textures to a named shader sampler.</summary>
/// <param name="SamplerId">Identifier of the sampler, resolvable through <see cref="GameShaderNames"/>.</param>
/// <param name="Flags">Address modes, level-of-detail bias and minimum level, packed.</param>
/// <param name="TextureIndex">Index into the material's texture list.</param>
public readonly record struct GameMaterialSampler(uint SamplerId, uint Flags, byte TextureIndex);

/// <summary>A shader constant's location inside the material's packed value blob.</summary>
/// <param name="Id">Identifier of the constant, resolvable through <see cref="GameShaderNames"/>.</param>
/// <param name="ByteOffset">Offset into the value blob.</param>
/// <param name="ByteSize">Size of the value in bytes.</param>
public readonly record struct GameMaterialConstant(uint Id, ushort ByteOffset, ushort ByteSize);

/// <summary>
/// Resolves the identifiers a game material uses for its samplers and constants.<br/>
/// The identifiers are not opaque: each is a CRC of the name, so the library ships a list of names
/// and derives the numbers. A name that is not in the list costs nothing, since an unresolved
/// identifier still parses and renders and only loses its label.
/// </summary>
public static class GameShaderNames
{
    /// <summary>Sampler and constant names known to this library; adding one is enough to resolve its identifier.</summary>
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

        // The names are ASCII, so each character is its own byte; truncating here keeps the hash allocation-free.
        Span<byte> ascii = name.Length <= 256 ? stackalloc byte[name.Length] : new byte[name.Length];
        for (var i = 0; i < name.Length; i++)
            ascii[i] = (byte)name[i];

        // The identifiers use a zero initial value and no final inversion, which is not the common configuration:
        // the usual all-ones initial value matches none of them.
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

/// <summary>
/// A parsed FFXIV material: which textures it uses, which shader samplers they feed, its shader
/// constants, and its color table when it has one.<br/>
/// Parsed here rather than by the shipped reader, whose color table is a fixed size that cannot hold
/// the larger layout current files use. For the one-call path use <see cref="GameMaterialLoader"/>.
/// </summary>
public sealed class GameMaterialFile : FileResource
{
    /// <summary>Set in the table flags when the material carries a color table.</summary>
    private const uint HasTableFlag = 0x4;

    /// <summary>Set in the table flags when a dye table follows the color table.</summary>
    private const uint HasDyeTableFlag = 0x8;

    /// <summary>File format version.</summary>
    public uint Version { get; private set; }

    /// <summary>Shader package this material is drawn with, such as <c>bgcolorchange.shpk</c> or <c>characterlegacy.shpk</c>.</summary>
    public string ShaderPackage { get; private set; } = string.Empty;

    /// <summary>Textures referenced by the material, addressed by <see cref="GameMaterialSampler.TextureIndex"/>.</summary>
    public GameMaterialTexture[] Textures { get; private set; } = [];

    /// <summary>Named UV set attributes.</summary>
    public string[] UvSets { get; private set; } = [];

    /// <summary>Named color set attributes.</summary>
    public string[] ColorSets { get; private set; } = [];

    /// <summary>Which texture feeds which shader sampler.</summary>
    public GameMaterialSampler[] Samplers { get; private set; } = [];

    /// <summary>Shader constants and where their values sit in <see cref="ShaderValues"/>.</summary>
    public GameMaterialConstant[] Constants { get; private set; } = [];

    /// <summary>Shader key and value pairs selecting the shader variant.</summary>
    public (uint Key, uint Value)[] ShaderKeys { get; private set; } = [];

    /// <summary>Packed blob the constants index into.</summary>
    public byte[] ShaderValues { get; private set; } = [];

    /// <summary>Raw color table bytes (rows are half-precision floats), empty when the material has none.</summary>
    public byte[] ColorTable { get; private set; } = [];

    /// <summary>Raw dye table bytes, empty when the material has none.</summary>
    public byte[] DyeTable { get; private set; } = [];

    /// <summary>Whether the material carries a color table.</summary>
    public bool HasColorTable => ColorTable.Length > 0;

    /// <summary>
    /// Reads a named shader constant's value as floats (a color constant is three, a scalar one), or null
    /// when the material does not set it.
    /// </summary>
    /// <param name="constantName">Constant name, for example <c>g_DiffuseColor</c>.</param>
    public float[]? ConstantValue(string constantName)
    {
        var id = GameShaderNames.IdOf(constantName);
        foreach (var constant in Constants)
        {
            if (constant.Id != id)
                continue;

            var count = constant.ByteSize / sizeof(float);
            if (count == 0 || constant.ByteOffset + constant.ByteSize > ShaderValues.Length)
                return null;

            var values = new float[count];
            for (var i = 0; i < count; i++)
                values[i] = BitConverter.ToSingle(ShaderValues, constant.ByteOffset + (i * sizeof(float)));

            return values;
        }

        return null;
    }

    /// <summary>Finds the texture bound to a named sampler, or null when the material has no such sampler.</summary>
    /// <param name="samplerName">Sampler name, for example <c>g_SamplerDiffuse</c>.</param>
    public GameMaterialTexture? TextureFor(string samplerName)
    {
        var id = GameShaderNames.IdOf(samplerName);
        foreach (var sampler in Samplers)
        {
            if (sampler.SamplerId == id && sampler.TextureIndex < Textures.Length)
                return Textures[sampler.TextureIndex];
        }

        return null;
    }

    /// <inheritdoc/>
    public override void LoadFile()
    {
        var data = Data;
        var cursor = new ByteCursor(data);

        Version = cursor.U32();
        var fileSize = cursor.U16();
        var dataSetSize = cursor.U16();
        var stringTableSize = cursor.U16();
        var shaderPackageOffset = cursor.U16();
        var textureCount = cursor.U8();
        var uvSetCount = cursor.U8();
        var colorSetCount = cursor.U8();
        var additionalDataSize = cursor.U8();

        var textureEntries = new (ushort Offset, ushort Flags)[textureCount];
        for (var i = 0; i < textureCount; i++)
            textureEntries[i] = (cursor.U16(), cursor.U16());

        var uvOffsets = new ushort[uvSetCount];
        for (var i = 0; i < uvSetCount; i++)
        {
            uvOffsets[i] = cursor.U16();
            cursor.Skip(sizeof(ushort));
        }

        var colorOffsets = new ushort[colorSetCount];
        for (var i = 0; i < colorSetCount; i++)
        {
            colorOffsets[i] = cursor.U16();
            cursor.Skip(sizeof(ushort));
        }

        var stringBase = cursor.Position;
        cursor.Skip(stringTableSize);

        ShaderPackage = StringAt(data, stringBase, shaderPackageOffset);

        Textures = new GameMaterialTexture[textureCount];
        for (var i = 0; i < textureCount; i++)
            Textures[i] = new GameMaterialTexture(StringAt(data, stringBase, textureEntries[i].Offset), textureEntries[i].Flags);

        UvSets = new string[uvSetCount];
        for (var i = 0; i < uvSetCount; i++)
            UvSets[i] = StringAt(data, stringBase, uvOffsets[i]);

        ColorSets = new string[colorSetCount];
        for (var i = 0; i < colorSetCount; i++)
            ColorSets[i] = StringAt(data, stringBase, colorOffsets[i]);

        var tableFlags = additionalDataSize >= sizeof(uint) ? BitConverter.ToUInt32(data, cursor.Position) : 0u;
        cursor.Skip(additionalDataSize);

        ReadTables(data, cursor.Position, tableFlags);
        cursor.Position += dataSetSize;

        var shaderValueSize = cursor.U16();
        var shaderKeyCount = cursor.U16();
        var constantCount = cursor.U16();
        var samplerCount = cursor.U16();
        cursor.Skip(sizeof(uint));                          // material flags

        ShaderKeys = new (uint, uint)[shaderKeyCount];
        for (var i = 0; i < shaderKeyCount; i++)
            ShaderKeys[i] = (cursor.U32(), cursor.U32());

        Constants = new GameMaterialConstant[constantCount];
        for (var i = 0; i < constantCount; i++)
            Constants[i] = new GameMaterialConstant(cursor.U32(), cursor.U16(), cursor.U16());

        Samplers = new GameMaterialSampler[samplerCount];
        for (var i = 0; i < samplerCount; i++)
        {
            Samplers[i] = new GameMaterialSampler(cursor.U32(), cursor.U32(), cursor.U8());
            cursor.Skip(3);                                 // padding to a twelve-byte record
        }

        ShaderValues = new byte[shaderValueSize];
        Array.Copy(data, cursor.Position, ShaderValues, 0, Math.Min(shaderValueSize, data.Length - cursor.Position));
        cursor.Skip(shaderValueSize);

        // The header declares the file's own length, so a walk that ends anywhere else has mis-sized a
        // block and every offset taken from it would address the wrong bytes.
        if (cursor.Position != fileSize)
        {
            throw new InvalidOperationException(
                $"Material layout walk ended at {cursor.Position} in a declared {fileSize}-byte file '{FilePath}'.");
        }
    }

    /// <summary>
    /// Extracts the color table, and the dye table behind it, from the material's data set; row count and
    /// width come from the flags rather than being fixed (the current layout vs. the older half-sized one).
    /// </summary>
    private void ReadTables(byte[] data, int dataSetStart, uint tableFlags)
    {
        if ((tableFlags & HasTableFlag) == 0)
            return;

        var dimensions = (tableFlags >> 4) & 0xFF;
        var (tableSize, dyeSize) = dimensions switch
        {
            0x53 => (2048, 128),
            0x42 or 0 => (512, 32),
            _ => (0, 0),
        };

        if (tableSize == 0 || dataSetStart + tableSize > data.Length)
            return;

        ColorTable = new byte[tableSize];
        Array.Copy(data, dataSetStart, ColorTable, 0, tableSize);

        if ((tableFlags & HasDyeTableFlag) == 0 || dataSetStart + tableSize + dyeSize > data.Length)
            return;

        DyeTable = new byte[dyeSize];
        Array.Copy(data, dataSetStart + tableSize, DyeTable, 0, dyeSize);
    }

    private static string StringAt(byte[] data, int stringBase, int offset)
        => BufferHelper.ReadNullTerminatedString(data, stringBase + offset);

}
