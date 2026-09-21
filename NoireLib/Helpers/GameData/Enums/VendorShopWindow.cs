namespace NoireLib.Helpers;

/// <summary>The window a vendor's shop opens in.</summary>
public enum VendorShopWindow
{
    /// <summary>The gil shop window, <c>Shop</c>.</summary>
    Shop,

    /// <summary>The special shop windows, <c>ShopExchangeItem</c> or <c>ShopExchangeCurrency</c>.</summary>
    ShopExchange,

    /// <summary>The categorised special shop window, <c>InclusionShop</c>.</summary>
    InclusionShop,

    /// <summary>The grand company quartermaster's window, <c>GrandCompanyExchange</c>.</summary>
    GrandCompanyExchange,

    /// <summary>The free company credit window, <c>FreeCompanyExchange</c>.</summary>
    FreeCompanyExchange,
}
