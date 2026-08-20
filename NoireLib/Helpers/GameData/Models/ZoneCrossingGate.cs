namespace NoireLib.Helpers;

/// <summary>
/// One quest condition standing on a zone crossing, in the form the <c>ZoneSharedGroup</c> sheet states it.
/// </summary>
/// <param name="QuestId">The quest row id.</param>
/// <param name="Step">The quest sequence step the crossing opens at; 255 means the quest must be complete.</param>
public readonly record struct ZoneCrossingGate(uint QuestId, byte Step);
