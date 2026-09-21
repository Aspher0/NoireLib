using Lumina.Data;
using System;

namespace NoireLib.Helpers;

/// <summary>
/// A parsed FFXIV material: its textures, the samplers they feed, its shader constants and its color table.
/// Lumina's reader is not used because its fixed-size color table cannot hold the current layout.
/// </summary>
public sealed class GameMaterialFile : FileResource
{
    private const uint HasTableFlag = 0x4;

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

    /// <summary>Reads a named shader constant's value as floats, or null when the material does not set it.</summary>
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
        cursor.Skip(sizeof(uint));

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

        // A walk not ending at the declared file size has mis-sized a block.
        if (cursor.Position != fileSize)
        {
            throw new InvalidOperationException(
                $"Material layout walk ended at {cursor.Position} in a declared {fileSize}-byte file '{FilePath}'.");
        }
    }

    // 0x53 is the current layout, 0x42 or 0 the older half-sized one.
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
