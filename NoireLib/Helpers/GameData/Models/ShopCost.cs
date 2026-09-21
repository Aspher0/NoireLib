namespace NoireLib.Helpers;

/// <summary>One thing an offer costs. Gil is represented as an item id.</summary>
/// <param name="ItemId">The Item row being spent, zero when the slot names no item this build knows.</param>
/// <param name="Amount">How many per purchase.</param>
/// <param name="CollectabilityRating">Required collectability, or zero.</param>
/// <param name="Kind">What the shop's cost column named.</param>
/// <param name="Slot">The Tomestones row or the scrip slot the column held, zero for an ordinary item.</param>
public readonly record struct ShopCost(
    uint ItemId,
    uint Amount,
    ushort CollectabilityRating = 0,
    ShopCostKind Kind = ShopCostKind.Item,
    uint Slot = 0)
{
    /// <summary>Whether this cost is paid in gil.</summary>
    public bool IsGil => Slot == 0 && ItemId == ShopHelper.GilItemId;

    /// <summary>Whether this cost is paid in a currency.</summary>
    public bool IsCurrency
        => Kind is ShopCostKind.Tomestone or ShopCostKind.Scrip or ShopCostKind.CompanyCredit || ShopHelper.IsCurrency(ItemId);

    /// <summary>Whether the line only takes the item high quality.</summary>
    public bool NeedsHighQuality => Kind == ShopCostKind.HighQualityItem;
}
