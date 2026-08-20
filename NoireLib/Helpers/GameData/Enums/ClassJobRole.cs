namespace NoireLib.Helpers;

/// <summary>
/// What a class or job does in a party, as the <c>ClassJob</c> sheet's own <c>Role</c> column numbers it.
/// <br/>
/// A typed view of that column. ClientStructs has no role enum with these semantics.
/// </summary>
public enum ClassJobRole : byte
{
    /// <summary>No combat role.</summary>
    None = 0,

    /// <summary>Tank.</summary>
    Tank = 1,

    /// <summary>Melee damage.</summary>
    MeleeDps = 2,

    /// <summary>Ranged damage, physical or magical.</summary>
    RangedDps = 3,

    /// <summary>Healer.</summary>
    Healer = 4,
}
