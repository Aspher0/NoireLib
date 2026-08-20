using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Lumina.Data.Files;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>Where one part of a ULD part list sits on its texture.</summary>
/// <param name="TexturePath">The game path of the texture the part is cut from.</param>
/// <param name="Position">The part's top left corner, in texture pixels.</param>
/// <param name="Size">The part's size, in texture pixels.</param>
public readonly record struct UldPart(string TexturePath, Vector2 Position, Vector2 Size);

/// <summary>A ULD part resolved to its loaded texture and the UVs that cut it out.</summary>
/// <param name="Texture">The shared texture the part lives on, which must not be disposed.</param>
/// <param name="Uv0">The part's top left corner, normalised.</param>
/// <param name="Uv1">The part's bottom right corner, normalised.</param>
/// <param name="Size">The part's size, in texture pixels.</param>
public readonly record struct UldPartTexture(IDalamudTextureWrap Texture, Vector2 Uv0, Vector2 Uv1, Vector2 Size);

/// <summary>
/// Reads part lists out of the game's ULD files and resolves their parts to textures and UVs.
/// </summary>
public static class UldHelper
{
    private static readonly ConcurrentDictionary<string, UldFile?> Files = new();

    /// <summary>Every part of a part list, in the order the ULD declares them.</summary>
    /// <param name="uldPath">The game path of the ULD, for instance <c>ui/uld/emote.uld</c>.</param>
    /// <param name="partListId">The part list's id inside that ULD.</param>
    /// <returns>The parts, or an empty list when the ULD or the part list does not exist.</returns>
    public static IReadOnlyList<UldPart> Parts(string uldPath, uint partListId)
    {
        if (File(uldPath) is not { } uld)
            return [];

        foreach (var partList in uld.Parts)
        {
            if (partList.Id != partListId)
                continue;

            var parts = new List<UldPart>(partList.Parts.Length);

            foreach (var part in partList.Parts)
            {
                var texturePath = TexturePathOf(uld, part.TextureId);

                if (texturePath == null)
                    continue;

                parts.Add(new UldPart(texturePath, new Vector2(part.U, part.V), new Vector2(part.W, part.H)));
            }

            return parts;
        }

        return [];
    }

    /// <summary>One part of a part list.</summary>
    /// <param name="uldPath">The game path of the ULD, for instance <c>ui/uld/emote.uld</c>.</param>
    /// <param name="partListId">The part list's id inside that ULD.</param>
    /// <param name="partIndex">The part's index in that list.</param>
    /// <returns>The part, or null when it does not exist.</returns>
    public static UldPart? Part(string uldPath, uint partListId, int partIndex)
    {
        var parts = Parts(uldPath, partListId);
        return partIndex >= 0 && partIndex < parts.Count ? parts[partIndex] : null;
    }

    /// <summary>One part of a part list, resolved to a texture and the UVs that cut it out.</summary>
    /// <param name="uldPath">The game path of the ULD, for instance <c>ui/uld/emote.uld</c>.</param>
    /// <param name="partListId">The part list's id inside that ULD.</param>
    /// <param name="partIndex">The part's index in that list.</param>
    /// <returns>The part, or null when it does not exist or its texture has not finished loading.</returns>
    public static UldPartTexture? PartTexture(string uldPath, uint partListId, int partIndex)
        => Part(uldPath, partListId, partIndex) is { } part ? Resolve(part) : null;

    /// <summary>Resolves a part to a texture and the UVs that cut it out.</summary>
    /// <param name="part">The part to resolve.</param>
    /// <returns>The resolved part, or null when its texture has not finished loading.</returns>
    public static UldPartTexture? Resolve(UldPart part)
    {
        if (!NoireService.IsInitialized())
            return null;

        var texture = SafeExecutor.ExecuteSafely<IDalamudTextureWrap?>(
            () => NoireService.TextureProvider.GetFromGame(part.TexturePath).GetWrapOrDefault(), null);

        if (texture is not { Width: > 0, Height: > 0 })
            return null;

        var sheet = new Vector2(texture.Width, texture.Height);

        return new UldPartTexture(texture, part.Position / sheet, (part.Position + part.Size) / sheet, part.Size);
    }

    private static string? TexturePathOf(UldFile uld, uint textureId)
    {
        foreach (var asset in uld.AssetData)
        {
            if (asset.Id != textureId)
                continue;

            var path = new string(asset.Path).TrimEnd('\0');
            return string.IsNullOrEmpty(path) ? null : path;
        }

        return null;
    }

    private static UldFile? File(string uldPath)
    {
        if (!NoireService.IsInitialized())
            return null;

        return Files.GetOrAdd(uldPath, static path =>
            SafeExecutor.ExecuteSafely<UldFile?>(() => NoireService.DataManager.GetFile<UldFile>(path), null));
    }
}
