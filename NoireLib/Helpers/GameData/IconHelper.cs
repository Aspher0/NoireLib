using Dalamud.Game;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Lumina.Excel.Sheets;

namespace NoireLib.Helpers;

/// <summary>
/// Turns an icon id into its game path or its texture, resolved through the game's own texture lookup so the
/// hi-res and language variants fall back as the client's do.
/// </summary>
public static class IconHelper
{
    #region Resolving an icon id

    /// <summary>The game path an icon lives at.</summary>
    /// <param name="iconId">The icon id.</param>
    /// <param name="highQuality">Whether to resolve the high quality variant, which items have and nothing else does.</param>
    /// <param name="highResolution">Whether to resolve the high resolution variant.</param>
    /// <param name="language">The language to resolve for, or null for the client's own.</param>
    /// <returns>The path, or null when no such icon exists.</returns>
    public static string? Path(uint iconId, bool highQuality = false, bool highResolution = true, ClientLanguage? language = null)
    {
        if (iconId == 0 || !NoireService.IsInitialized())
            return null;

        return SafeExecutor.ExecuteSafely<string?>(
            () => NoireService.TextureProvider.TryGetIconPath(
                new GameIconLookup(iconId, highQuality, highResolution, language), out var path)
                ? path
                : null,
            null);
    }

    /// <summary>Whether an icon exists.</summary>
    /// <param name="iconId">The icon id.</param>
    /// <param name="highQuality">Whether to test the high quality variant.</param>
    /// <param name="highResolution">Whether to test the high resolution variant.</param>
    /// <returns>True when the icon exists.</returns>
    public static bool Exists(uint iconId, bool highQuality = false, bool highResolution = true)
        => Path(iconId, highQuality, highResolution) != null;

    /// <summary>
    /// The shared texture behind an icon. Hold this rather than a wrap.
    /// </summary>
    /// <param name="iconId">The icon id.</param>
    /// <param name="highQuality">Whether to load the high quality variant.</param>
    /// <param name="highResolution">Whether to load the high resolution variant.</param>
    /// <param name="language">The language to load for, or null for the client's own.</param>
    /// <returns>The shared texture, or null when no such icon exists.</returns>
    public static ISharedImmediateTexture? Get(
        uint iconId,
        bool highQuality = false,
        bool highResolution = true,
        ClientLanguage? language = null)
    {
        if (iconId == 0 || !NoireService.IsInitialized())
            return null;

        return SafeExecutor.ExecuteSafely<ISharedImmediateTexture?>(
            () => NoireService.TextureProvider.TryGetFromGameIcon(
                new GameIconLookup(iconId, highQuality, highResolution, language), out var texture)
                ? texture
                : null,
            null);
    }

    /// <summary>
    /// An icon as a wrap ready to hand to a draw call. The wrap belongs to the shared texture and must not be disposed
    /// by the caller.
    /// </summary>
    /// <param name="iconId">The icon id.</param>
    /// <param name="highQuality">Whether to load the high quality variant.</param>
    /// <param name="highResolution">Whether to load the high resolution variant.</param>
    /// <param name="language">The language to load for, or null for the client's own.</param>
    /// <returns>The wrap, or null when the icon does not exist or has not finished loading.</returns>
    public static IDalamudTextureWrap? Wrap(
        uint iconId,
        bool highQuality = false,
        bool highResolution = true,
        ClientLanguage? language = null)
        => Get(iconId, highQuality, highResolution, language)?.GetWrapOrDefault();

    #endregion

    #region Icons named by a sheet row

    /// <summary>An item's icon.</summary>
    /// <param name="itemId">The Item row id.</param>
    /// <returns>The icon id, or zero when the id names no item.</returns>
    public static uint ForItem(uint itemId) => Column<Item>(itemId, static row => row.Icon);

    /// <summary>An action's icon.</summary>
    /// <param name="actionId">The Action row id.</param>
    /// <returns>The icon id, or zero when the id names no action.</returns>
    public static uint ForAction(uint actionId) => Column<Action>(actionId, static row => row.Icon);

    /// <summary>A status effect's icon.</summary>
    /// <param name="statusId">The Status row id.</param>
    /// <returns>The icon id, or zero when the id names no status.</returns>
    public static uint ForStatus(uint statusId) => Column<Status>(statusId, static row => row.Icon);

    /// <summary>A duty's icon, as the duty finder shows it.</summary>
    /// <param name="conditionId">The ContentFinderCondition row id.</param>
    /// <returns>The icon id, or zero when the id names no duty.</returns>
    public static uint ForDuty(uint conditionId)
        => Column<ContentFinderCondition>(conditionId, static row => row.Icon);

    /// <summary>An emote's icon.</summary>
    /// <param name="emoteId">The Emote row id.</param>
    /// <returns>The icon id, or zero when the id names no emote.</returns>
    public static uint ForEmote(uint emoteId) => Column<Emote>(emoteId, static row => row.Icon);

    /// <summary>A map symbol's icon.</summary>
    /// <param name="markerId">The MapSymbol row id.</param>
    /// <returns>The icon id, or zero when the id names no symbol.</returns>
    public static uint ForMapSymbol(uint markerId)
        => Column<MapSymbol>(markerId, static row => (uint)row.Icon);

    #endregion

    private static uint Column<T>(uint rowId, System.Func<T, uint> read) where T : struct, Lumina.Excel.IExcelRow<T>
    {
        if (rowId == 0)
            return 0;

        return SafeExecutor.ExecuteSafely(
            () => ExcelSheetHelper.TryGetRow<T>(rowId, out var row) && row.HasValue ? read(row.Value) : 0u);
    }
}
