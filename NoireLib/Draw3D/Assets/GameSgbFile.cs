using Lumina.Data;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace NoireLib.Draw3D.Assets;

/// <summary>
/// One placed asset inside a scene definition: the file it references and where it stands, local to the scene.
/// </summary>
/// <param name="Path">Archive path of the referenced file: a model for <see cref="GameSgbFile.Models"/>, another scene for <see cref="GameSgbFile.Attachments"/>.</param>
/// <param name="CollisionPath">Archive path of the collision file, empty when the entry references none.</param>
/// <param name="Translation">Local translation, in world units.</param>
/// <param name="Rotation">Local rotation as Euler angles in radians, applied X then Y then Z.</param>
/// <param name="Scale">Local scale.</param>
public readonly record struct GameSgbPlacement(
    string Path,
    string CollisionPath,
    Vector3 Translation,
    Vector3 Rotation,
    Vector3 Scale);

/// <summary>
/// A parsed scene definition (<c>.sgb</c>): the models it places, the scenes it nests, and its default stain.<br/>
/// Furniture is stored this way - the item the game spawns is the scene, and the model files under
/// <c>bgparts/</c> are what the scene places. A placed item whose stain slot is empty renders
/// <see cref="DefaultStain"/>; an item that states none falls back to
/// <see cref="GameMaterial.UndyedStain"/> on dyeable surfaces.
/// </summary>
public sealed class GameSgbFile : FileResource
{
    // Layout, validated against every furniture sgb in the archives (1916 files, indoor and outdoor):
    //   0x00 "SGB1", 0x0C "SCN1"; header offsets are relative to 0x14.
    //   0x14 u32: offset of the shared entry group. Group: name u32 at +0x04 (group-relative),
    //             entry count at +0x20, entry offset table at +0x48, each entry at table + stored u32.
    //   0x40 u32: offset of the default stain u16, 0 when the scene states none.
    //   Entry: type u32, id u32, name offset u32 (entry-relative), translation/rotation/scale at
    //          0x0C/0x18/0x24 as float3 each, referenced file at 0x30, collision file at 0x34.
    //   Type 1 places a model; type 6 nests another scene with the same layout.
    private const uint MagicSgb = 0x31424753;
    private const uint MagicScn = 0x314E4353;
    private const int BaseOffset = 0x14;
    private const int StainPointerOffset = 0x40;
    private const uint ModelEntry = 1;
    private const uint SceneEntry = 6;

    private readonly List<GameSgbPlacement> models = [];
    private readonly List<GameSgbPlacement> attachments = [];

    /// <summary>The models this scene places, with their local transforms.</summary>
    public IReadOnlyList<GameSgbPlacement> Models => models;

    /// <summary>The scenes this scene nests, with their local transforms. Their own placements are local to them.</summary>
    public IReadOnlyList<GameSgbPlacement> Attachments => attachments;

    /// <summary>The stain an empty stain slot renders, 0 when the scene states none. Dyeable furniture states one explicitly.</summary>
    public ushort DefaultStain { get; private set; }

    /// <inheritdoc/>
    public override void LoadFile()
    {
        var data = Data;
        if (data.Length < 0x60 || BitConverter.ToUInt32(data, 0) != MagicSgb || BitConverter.ToUInt32(data, 0xC) != MagicScn)
            throw new InvalidOperationException("Not a scene definition: the SGB1/SCN1 header is missing.");

        DefaultStain = ReadDefaultStain(data);

        var group = BaseOffset + BitConverter.ToInt32(data, BaseOffset);
        if (group < 0 || group + 0x50 > data.Length)
            throw new InvalidOperationException($"Scene group offset 0x{group:X} is outside the file.");

        var count = BitConverter.ToInt32(data, group + 0x20);
        if (count is < 0 or > 4096)
            throw new InvalidOperationException($"Scene group declares {count} entries.");

        var table = group + 0x48;
        for (var i = 0; i < count; i++)
        {
            if (table + (i * 4) + 4 > data.Length)
                throw new InvalidOperationException("Scene entry table runs past the end of the file.");

            var entry = table + BitConverter.ToInt32(data, table + (i * 4));
            if (entry < 0 || entry + 0x38 > data.Length)
                throw new InvalidOperationException($"Scene entry {i} sits outside the file.");

            switch (BitConverter.ToUInt32(data, entry))
            {
                case ModelEntry:
                    models.Add(ReadPlacement(data, entry, ".mdl"));
                    break;
                case SceneEntry:
                    attachments.Add(ReadPlacement(data, entry, ".sgb"));
                    break;
                // Other types are lights, sounds, vfx and timeline data, none of which places geometry.
            }
        }
    }

    /// <summary>
    /// The scene definition placed beside a background model, resolved from the model's path.
    /// Furniture pairs <c>.../bgparts/x.mdl</c> with <c>.../asset/x.sgb</c>.
    /// </summary>
    /// <param name="modelGamePath">The model's archive path, under <c>bgcommon/</c>.</param>
    /// <returns>The sibling scene path, or null when the path does not follow the pairing.</returns>
    public static string? PathBesideModel(string modelGamePath)
    {
        if (string.IsNullOrWhiteSpace(modelGamePath)
            || !modelGamePath.EndsWith(".mdl", StringComparison.Ordinal)
            || !modelGamePath.Contains("/bgparts/", StringComparison.Ordinal))
            return null;

        return modelGamePath.Replace("/bgparts/", "/asset/", StringComparison.Ordinal)[..^4] + ".sgb";
    }

    private static ushort ReadDefaultStain(byte[] data)
    {
        var pointer = BitConverter.ToUInt32(data, StainPointerOffset);
        if (pointer == 0)
            return 0;

        var at = BaseOffset + (long)pointer;
        if (at + 2 > data.Length)
            return 0;

        // A value beyond the stain table means the pointer did not mean this here; render it as unstated
        // rather than as an arbitrary color.
        var value = BitConverter.ToUInt16(data, (int)at);
        return value > 1000 ? (ushort)0 : value;
    }

    private static GameSgbPlacement ReadPlacement(byte[] data, int entry, string extension)
    {
        var path = ReadString(data, entry, BitConverter.ToInt32(data, entry + 0x30));
        if (!path.EndsWith(extension, StringComparison.Ordinal))
            throw new InvalidOperationException($"Scene entry at 0x{entry:X} references '{path}' where a {extension} was expected.");

        return new GameSgbPlacement(
            path,
            ReadString(data, entry, BitConverter.ToInt32(data, entry + 0x34)),
            new Vector3(BitConverter.ToSingle(data, entry + 0x0C), BitConverter.ToSingle(data, entry + 0x10), BitConverter.ToSingle(data, entry + 0x14)),
            new Vector3(BitConverter.ToSingle(data, entry + 0x18), BitConverter.ToSingle(data, entry + 0x1C), BitConverter.ToSingle(data, entry + 0x20)),
            new Vector3(BitConverter.ToSingle(data, entry + 0x24), BitConverter.ToSingle(data, entry + 0x28), BitConverter.ToSingle(data, entry + 0x2C)));
    }

    private static string ReadString(byte[] data, int entry, int offset)
    {
        var at = entry + offset;
        if (at <= 0 || at >= data.Length)
            throw new InvalidOperationException($"Scene entry at 0x{entry:X} points a string outside the file.");

        var end = at;
        while (end < data.Length && data[end] != 0)
            end++;

        return Encoding.ASCII.GetString(data, at, end - at);
    }
}
