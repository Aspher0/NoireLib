namespace NoireLib.Helpers;

/// <summary>One thing an offer costs; gil is represented as an item id.</summary>
/// <param name="ItemId">The Item row being spent.</param>
/// <param name="Amount">How many per purchase.</param>
/// <param name="CollectabilityRating">Required collectability, or zero.</param>
public readonly record struct ShopCost(uint ItemId, uint Amount, ushort CollectabilityRating = 0)
{
    /// <summary>Whether this cost is paid in gil.</summary>
    public bool IsGil => ItemId == ShopHelper.GilItemId;
}
