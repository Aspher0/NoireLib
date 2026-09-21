using System;

namespace NoireLib.Helpers;

internal readonly record struct ContextMenuItemReading(
    string AddonName,
    bool HasCharacterTarget,
    uint ChatLinkItemId,
    uint RecipeNoteItemId,
    uint RecipeListItemId,
    ulong HoveredItemId);

internal static class ContextMenuItemResolver
{
    // Game state carries the quality as an offset on the row id. Event items start at 2 000 000 and are not Item rows.
    internal const ulong CollectableOffset = 500_000;
    internal const ulong HighQualityOffset = 1_000_000;
    internal const ulong EventItemStart = 2_000_000;

    private static readonly string[] RecipeListAddons = ["RecipeTree", "RecipeMaterialList", "RecipeProductList"];

    internal static (uint ItemId, bool IsHighQuality, ContextMenuItemSource Source) Resolve(ContextMenuItemReading reading)
    {
        if (reading.HasCharacterTarget)
            return (0, false, ContextMenuItemSource.None);

        var addonName = reading.AddonName ?? string.Empty;

        if (addonName.StartsWith("ChatLog", StringComparison.Ordinal))
            return Pick(reading.ChatLinkItemId, ContextMenuItemSource.ChatLink, reading.HoveredItemId);

        if (addonName == "RecipeNote")
            return Pick(reading.RecipeNoteItemId, ContextMenuItemSource.Recipe, reading.HoveredItemId);

        if (Array.IndexOf(RecipeListAddons, addonName) >= 0)
            return Pick(reading.RecipeListItemId, ContextMenuItemSource.Recipe, reading.HoveredItemId);

        return Pick(0, ContextMenuItemSource.None, reading.HoveredItemId);
    }

    internal static uint Decode(ulong rawItemId, out bool isHighQuality)
    {
        isHighQuality = false;

        if (rawItemId == 0 || rawItemId >= EventItemStart)
            return 0;

        if (rawItemId >= HighQualityOffset)
        {
            isHighQuality = true;
            return (uint)(rawItemId - HighQualityOffset);
        }

        return (uint)(rawItemId >= CollectableOffset ? rawItemId - CollectableOffset : rawItemId);
    }

    private static (uint ItemId, bool IsHighQuality, ContextMenuItemSource Source) Pick(ulong primary, ContextMenuItemSource source, ulong hovered)
    {
        var itemId = Decode(primary, out var isHighQuality);

        if (itemId != 0)
            return (itemId, isHighQuality, source);

        itemId = Decode(hovered, out isHighQuality);

        return itemId != 0
            ? (itemId, isHighQuality, ContextMenuItemSource.Hovered)
            : (0, false, ContextMenuItemSource.None);
    }
}
