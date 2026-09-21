namespace NoireLib.Helpers;

/// <summary>One special shop an InclusionShop holds, with the job restriction its category carries.</summary>
/// <param name="SpecialShopId">The SpecialShop row id.</param>
/// <param name="CategoryId">The InclusionShopCategory row id holding it.</param>
/// <param name="ClassJobCategoryId">The ClassJobCategory row the category restricts it to, or zero.</param>
public readonly record struct InclusionShopEntry(uint SpecialShopId, uint CategoryId, uint ClassJobCategoryId);
