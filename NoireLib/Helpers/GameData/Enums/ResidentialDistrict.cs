namespace NoireLib.Helpers;

/// <summary>
/// The residential districts, as named constants over their TerritoryType row ids. Every housing lookup takes a raw
/// territory id and this only spares the caller from memorising one. A district added by a later patch is
/// resolved through the territory overloads without a code change and gains a member here once it ships.
/// <see cref="HousingHelper.Districts"/> stays the discovered list.
/// </summary>
public enum ResidentialDistrict : uint
{
    /// <summary>Mist, the Limsa Lominsa district.</summary>
    Mist = 339,

    /// <summary>The Lavender Beds, the Gridania district.</summary>
    LavenderBeds = 340,

    /// <summary>The Goblet, the Ul'dah district.</summary>
    Goblet = 341,

    /// <summary>Shirogane, the Kugane district.</summary>
    Shirogane = 641,

    /// <summary>Empyreum, the Ishgard district.</summary>
    Empyreum = 979,
}
