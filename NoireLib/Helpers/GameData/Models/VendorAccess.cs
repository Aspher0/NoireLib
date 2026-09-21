using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// What one character has done, read once and handed to the gates. A null field means the client could not answer,
/// and the gate reading it counts as met.
/// </summary>
/// <param name="CompletedQuests">The quests the character has finished, among those the vendors ask about.</param>
/// <param name="ClearedDuties">The duties the character has cleared, among those the vendors ask about.</param>
/// <param name="EarnedAchievements">The achievements the character holds, null until the client loads the list.</param>
/// <param name="ActiveFestivals">The festivals running, with the phase each has reached.</param>
/// <param name="ClassJobCategories">The ClassJobCategory rows the character's job belongs to.</param>
/// <param name="ClassJobId">The ClassJob row the character is on, or zero.</param>
/// <param name="GrandCompanyId">The grand company the character joined, or zero.</param>
/// <param name="GrandCompanyRank">The rank the character holds in it.</param>
/// <param name="FreeCompanyRank">The rank the character holds in their free company.</param>
public sealed record VendorAccess(
    IReadOnlySet<uint>? CompletedQuests = null,
    IReadOnlySet<uint>? ClearedDuties = null,
    IReadOnlySet<uint>? EarnedAchievements = null,
    IReadOnlyDictionary<uint, uint>? ActiveFestivals = null,
    IReadOnlySet<uint>? ClassJobCategories = null,
    uint ClassJobId = 0,
    uint GrandCompanyId = 0,
    uint? GrandCompanyRank = null,
    uint? FreeCompanyRank = null)
{
    /// <summary>A character nothing is known about. Every gate reads as met.</summary>
    public static VendorAccess Unknown { get; } = new();
}
