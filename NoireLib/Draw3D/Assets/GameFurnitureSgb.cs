using System;

namespace NoireLib.Draw3D.Assets;

/// <summary>
/// Reads the default stain out of a furniture's scene definition (<c>.sgb</c>).<br/>
/// A placed item whose stain slot is empty renders with this stain; when the file states none, the game
/// falls back to stain 1 (<see cref="GameMaterial.UndyedStain"/>). The block is optional and most furniture
/// omits it. See docs/Draw3D Game Assets Status.md for the measurements behind the layout.
/// </summary>
public static class GameFurnitureSgb
{
    // File-absolute layout: "SGB1" magic, an SCN1 chunk at 0xC, a u32 table count at 0x74, the table at
    // 0x9C, and behind it a pair of small u32s followed by the SCN1-relative offset of the settings block
    // whose first u16 is the stain id. Files whose walk does not match these shapes return unreadable
    // rather than a misread value.
    private const int MagicOffset = 0;
    private const int TableCountOffset = 0x74;
    private const int TableOffset = 0x9C;
    private const int SceneChunkOffset = 0xC;
    private const uint Magic = 0x31424753; // "SGB1"

    /// <summary>
    /// Reads the default stain id from a furniture sgb's bytes.
    /// </summary>
    /// <param name="data">The file's bytes.</param>
    /// <param name="stainId">The stated default stain id; 0 when the item states none.</param>
    /// <returns>False when the file is not a furniture sgb this layout can read.</returns>
    public static bool TryReadDefaultStain(byte[] data, out ushort stainId)
    {
        stainId = 0;

        if (data is null || data.Length < TableOffset + 16 || BitConverter.ToUInt32(data, MagicOffset) != Magic)
            return false;

        var count = BitConverter.ToUInt32(data, TableCountOffset);
        if (count is 0 or > 64)
            return false;

        var post = TableOffset + ((int)count * 4);
        if (post + 12 > data.Length)
            return false;

        // The two values ahead of the block offset are small counts in every readable file; anything else
        // means the walk landed somewhere structurally different.
        if (BitConverter.ToUInt32(data, post) > 16 || BitConverter.ToUInt32(data, post + 4) > 16)
            return false;

        var blockOffset = BitConverter.ToUInt32(data, post + 8);
        if (blockOffset == 0)
            return true; // readable, no block: the item states no default

        var at = SceneChunkOffset + (long)blockOffset;
        if (at + 2 > data.Length)
            return false;

        var value = BitConverter.ToUInt16(data, (int)at);
        if (value > 1000)
            return false;

        stainId = value;
        return true;
    }

    /// <summary>
    /// The default stain of the furniture a background model belongs to, resolved from the model's path.
    /// </summary>
    /// <param name="modelGamePath">The model's archive path, under <c>bgcommon/</c>.</param>
    /// <returns>The stain id, 0 for none stated, or null when no readable sgb sits beside the model.</returns>
    public static ushort? DefaultStainFor(string modelGamePath)
    {
        if (string.IsNullOrWhiteSpace(modelGamePath) || !modelGamePath.EndsWith(".mdl", StringComparison.Ordinal))
            return null;

        var sgbPath = modelGamePath.Replace("/bgparts/", "/asset/", StringComparison.Ordinal)[..^4] + ".sgb";
        if (sgbPath.Length == modelGamePath.Length && sgbPath == modelGamePath)
            return null;

        var file = NoireService.DataManager.GetFile(sgbPath);
        return file is not null && TryReadDefaultStain(file.Data, out var stain) ? stain : null;
    }
}
