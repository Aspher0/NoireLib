namespace NoireLib.Helpers;

/// <summary>What a shop line asks for in exchange for the item.</summary>
public enum PurchaseKind
{
    /// <summary>The line charges nothing.</summary>
    Free,

    /// <summary>Gil is the whole price.</summary>
    GilShop,

    /// <summary>Every cost is a currency, gil among them.</summary>
    CurrencyExchange,

    /// <summary>An ordinary item is part of the price.</summary>
    ItemExchange,
}
