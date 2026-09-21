namespace NoireLib.Helpers;

/// <summary>What the cost column of a shop line names.</summary>
public enum ShopCostKind
{
    /// <summary>An Item row, gil among them.</summary>
    Item,

    /// <summary>An Item row the line only takes high quality.</summary>
    HighQualityItem,

    /// <summary>A Tomestones row. The item it charges changes with the patch.</summary>
    Tomestone,

    /// <summary>A scrip slot. The item it charges changes with the expansion.</summary>
    Scrip,

    /// <summary>Free company credits, which the Item sheet holds no row for.</summary>
    CompanyCredit,
}
