using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A lightweight description of an image usable by the NoireLib UI helpers (overlay buttons, custom tooltips...).<br/>
/// Sources backed by the <see cref="Dalamud.Plugin.Services.ITextureProvider"/> (files, game icons, game textures) are resolved
/// through Dalamud's shared texture cache and are cheap to resolve every frame.
/// </summary>
public sealed class UiImageSource
{
    private enum SourceKind
    {
        File,
        GameIcon,
        GameTexture,
        Wrap,
    }

    private readonly SourceKind kind;
    private readonly string? path;
    private readonly uint gameIconId;
    private readonly bool gameIconHiRes;
    private readonly IDalamudTextureWrap? wrap;

    private UiImageSource(SourceKind kind, string? path = null, uint gameIconId = 0, bool gameIconHiRes = true, IDalamudTextureWrap? wrap = null)
    {
        this.kind = kind;
        this.path = path;
        this.gameIconId = gameIconId;
        this.gameIconHiRes = gameIconHiRes;
        this.wrap = wrap;
    }

    /// <summary>
    /// Creates an image source from an image file on disk (png, jpg, etc.).
    /// </summary>
    /// <param name="filePath">The path of the image file.</param>
    /// <returns>The created <see cref="UiImageSource"/>.</returns>
    public static UiImageSource FromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath), "File path cannot be null or blank.");

        return new UiImageSource(SourceKind.File, path: filePath);
    }

    /// <summary>
    /// Creates an image source from a game icon id (the same ids used by <c>/xldata</c> icon browser).
    /// </summary>
    /// <param name="iconId">The id of the game icon.</param>
    /// <param name="hiRes">Whether to load the high resolution version of the icon.</param>
    /// <returns>The created <see cref="UiImageSource"/>.</returns>
    public static UiImageSource FromGameIcon(uint iconId, bool hiRes = true)
        => new(SourceKind.GameIcon, gameIconId: iconId, gameIconHiRes: hiRes);

    /// <summary>
    /// Creates an image source from a game texture path (e.g. <c>ui/uld/image.tex</c>).
    /// </summary>
    /// <param name="texPath">The internal game path of the texture.</param>
    /// <returns>The created <see cref="UiImageSource"/>.</returns>
    public static UiImageSource FromGameTexture(string texPath)
    {
        if (string.IsNullOrWhiteSpace(texPath))
            throw new ArgumentNullException(nameof(texPath), "Texture path cannot be null or blank.");

        return new UiImageSource(SourceKind.GameTexture, path: texPath);
    }

    /// <summary>
    /// Creates an image source from an existing texture wrap.<br/>
    /// The wrap stays owned by the caller: it will not be disposed by NoireLib and must outlive this source.
    /// </summary>
    /// <param name="textureWrap">The texture wrap to use.</param>
    /// <returns>The created <see cref="UiImageSource"/>.</returns>
    public static UiImageSource FromWrap(IDalamudTextureWrap textureWrap)
    {
        if (textureWrap == null)
            throw new ArgumentNullException(nameof(textureWrap), "Texture wrap cannot be null.");

        return new UiImageSource(SourceKind.Wrap, wrap: textureWrap);
    }

    /// <summary>
    /// Resolves this source to a texture wrap usable with ImGui for the current frame.<br/>
    /// Shared sources may return an empty placeholder texture while still loading.
    /// </summary>
    /// <returns>The resolved texture wrap, or <see langword="null"/> if it could not be resolved.</returns>
    public IDalamudTextureWrap? GetWrap()
    {
        try
        {
            return kind switch
            {
                SourceKind.Wrap => wrap,
                SourceKind.File => NoireService.TextureProvider.GetFromFile(path!).GetWrapOrEmpty(),
                SourceKind.GameTexture => NoireService.TextureProvider.GetFromGame(path!).GetWrapOrEmpty(),
                SourceKind.GameIcon => NoireService.TextureProvider.GetFromGameIcon(new GameIconLookup(gameIconId, hiRes: gameIconHiRes)).GetWrapOrEmpty(),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            NoireLogger.LogError<UiImageSource>(ex, $"Failed to resolve image source ({kind}, '{path ?? gameIconId.ToString()}').");
            return null;
        }
    }

    /// <summary>
    /// Gets the native pixel size of the underlying texture, or <see langword="null"/> if it is not available yet.
    /// </summary>
    /// <returns>The size of the texture in pixels, or <see langword="null"/>.</returns>
    public Vector2? GetNativeSize()
    {
        var resolved = GetWrap();
        if (resolved == null)
            return null;

        var size = resolved.Size;
        return size.X <= 4 && size.Y <= 4 ? null : size; // Shared textures return a 4x4 placeholder while loading.
    }
}
