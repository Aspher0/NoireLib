using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>One chocobo taxi ride: a directed hop between two stands with a fixed fare and a fixed duration.</summary>
/// <param name="FromStandId">The departure stand's ChocoboTaxiStand row id.</param>
/// <param name="ToStandId">The arrival stand's ChocoboTaxiStand row id.</param>
/// <param name="Fare">The ride's fare in gil.</param>
/// <param name="TimeSeconds">The ride's fixed duration in seconds. The sheet states minutes; this is already converted.</param>
/// <param name="DestinationName">The arrival stand's name in the client's own language, or empty when it did not resolve.</param>
public readonly record struct ChocoboTaxiRide(
    uint FromStandId,
    uint ToStandId,
    uint Fare,
    int TimeSeconds,
    string DestinationName);

/// <summary>One chocobo taxi stand and the rides that leave it.</summary>
/// <param name="StandId">The ChocoboTaxiStand row id.</param>
/// <param name="Name">The stand's name in the client's own language.</param>
/// <param name="Rides">The rides leaving this stand.</param>
public readonly record struct ChocoboTaxiStandInfo(uint StandId, string Name, IReadOnlyList<ChocoboTaxiRide> Rides);

/// <summary>
/// Reads the chocobo porter network out of the game's sheets: which stands exist, what each ride costs and how long
/// it takes, and which NPC runs each stand. A stand's position comes from that porter's own placement, resolved
/// through <see cref="EventNpcHelper.FindPlacements"/>. Every read is guarded; a missing sheet yields empty.
/// </summary>
public static class ChocoboTaxiHelper
{
    /// <summary>
    /// Reads the stands and the rides leaving each, keeping only rides the sheet actually describes.<br/>
    /// A stand's target list is a fixed-width slot array; unused slots are placeholder rides naming no destination
    /// or one reachable in zero time. Every real ride takes at least a minute, so a zero duration marks an unused slot.
    /// </summary>
    /// <returns>The stands that offer at least one ride.</returns>
    public static IReadOnlyList<ChocoboTaxiStandInfo> ReadStands()
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var stands = new List<ChocoboTaxiStandInfo>();
            var sheet = ExcelSheetHelper.GetSheet<ChocoboTaxiStand>();
            if (sheet == null)
                return (IReadOnlyList<ChocoboTaxiStandInfo>)stands;

            var names = new Dictionary<uint, string>();
            foreach (var stand in sheet)
            {
                if (stand.RowId != 0)
                    names[stand.RowId] = stand.PlaceName.ExtractText() ?? string.Empty;
            }

            foreach (var stand in sheet)
            {
                if (stand.RowId == 0)
                    continue;

                var rides = new List<ChocoboTaxiRide>();
                foreach (var targetRef in stand.TargetLocations)
                {
                    if (targetRef.ValueNullable is not { } taxi || taxi.Location.RowId == 0 || taxi.TimeRequired == 0)
                        continue;

                    // The sheet's TimeRequired is in minutes; a duration is far more useful in seconds.
                    rides.Add(new ChocoboTaxiRide(stand.RowId, taxi.Location.RowId, taxi.Fare,
                        taxi.TimeRequired * 60, names.GetValueOrDefault(taxi.Location.RowId, string.Empty)));
                }

                if (rides.Count > 0)
                    stands.Add(new ChocoboTaxiStandInfo(stand.RowId, names.GetValueOrDefault(stand.RowId, string.Empty), rides));
            }

            return stands;
        }, []) ?? [];
    }

    /// <summary>
    /// Every stand id the network touches: departure stands and every stand they can reach, including destinations
    /// nothing departs from.
    /// </summary>
    /// <param name="stands">The stands from <see cref="ReadStands"/>.</param>
    /// <returns>The stand ids.</returns>
    public static IReadOnlySet<uint> CollectStandIds(IReadOnlyList<ChocoboTaxiStandInfo> stands)
    {
        var ids = new HashSet<uint>();
        foreach (var stand in stands)
        {
            ids.Add(stand.StandId);
            foreach (var ride in stand.Rides)
                ids.Add(ride.ToStandId);
        }

        return ids;
    }

    /// <summary>
    /// Flattens every ride in the network into one list.
    /// </summary>
    /// <param name="stands">The stands from <see cref="ReadStands"/>.</param>
    /// <returns>Every ride, in stand order.</returns>
    public static IReadOnlyList<ChocoboTaxiRide> CollectRides(IReadOnlyList<ChocoboTaxiStandInfo> stands)
    {
        var rides = new List<ChocoboTaxiRide>();
        foreach (var stand in stands)
            rides.AddRange(stand.Rides);

        return rides;
    }

    /// <summary>
    /// Finds the NPC that runs each stand, being the event NPC whose <c>ENpcData</c> references the stand. A stand
    /// served by more than one porter resolves to the lowest-numbered of them, so the answer never depends on the
    /// order the sheet was walked in.
    /// </summary>
    /// <param name="stands">The stands from <see cref="ReadStands"/>; their destinations are included.</param>
    /// <param name="scan">
    /// A pre-built <see cref="EventNpcHandlerScan"/> to read from, so one sheet pass can serve several consumers.
    /// Null scans the sheet here, filtered to the given stands.
    /// </param>
    /// <returns>The ENpcBase row id running each stand.</returns>
    public static IReadOnlyDictionary<uint, uint> ScanPorters(
        IReadOnlyList<ChocoboTaxiStandInfo> stands,
        EventNpcHandlerScan? scan = null)
        => ScanPorters(CollectStandIds(stands), scan);

    /// <inheritdoc cref="ScanPorters(IReadOnlyList{ChocoboTaxiStandInfo}, EventNpcHandlerScan)"/>
    /// <param name="standIds">The stand ids to find porters for, from <see cref="CollectStandIds"/>.</param>
    /// <param name="scan">The pre-built scan, or null to scan here.</param>
    /// <returns>The ENpcBase row id running each stand.</returns>
    public static IReadOnlyDictionary<uint, uint> ScanPorters(
        IReadOnlySet<uint> standIds,
        EventNpcHandlerScan? scan = null)
    {
        var porters = new Dictionary<uint, uint>();
        if (standIds.Count == 0)
            return porters;

        var byHandler = (scan ?? EventNpcHelper.ScanHandlers(standIds)).NpcsByHandler;
        foreach (var standId in standIds)
        {
            if (!byHandler.TryGetValue(standId, out var npcs))
                continue;

            foreach (var npcId in npcs)
            {
                if (!porters.TryGetValue(standId, out var existing) || npcId < existing)
                    porters[standId] = npcId;
            }
        }

        return porters;
    }

    /// <summary>
    /// The chocobo taxi stands the logged-in character has registered, read from the client's own unlock bitmask,
    /// together with whether that bitmask could be read at all.<br/>
    /// Unlike the teleport list, this is client state that exists in full for any loaded character, so an empty set
    /// from a successful read is a real answer: a character who has registered no stand yet. Only a read that could
    /// not happen reports <c>Known</c> false. Framework thread, and only once the character's state is loaded.
    /// </summary>
    /// <returns>The registered stand ids, and whether the read produced a real answer.</returns>
    public static unsafe (IReadOnlySet<uint> Unlocked, bool Known) ReadUnlockedStands()
    {
        var unlocked = new HashSet<uint>();
        if (!CharacterHelper.IsStateReady)
            return (unlocked, false);

        var known = SafeExecutor.ExecuteSafely(() =>
        {
            var state = UIState.Instance();
            var sheet = ExcelSheetHelper.GetSheet<ChocoboTaxiStand>();
            if (state == null || sheet == null)
                return false;

            foreach (var stand in sheet)
            {
                if (stand.RowId != 0 && state->IsChocoboTaxiStandUnlocked(stand.RowId))
                    unlocked.Add(stand.RowId);
            }

            return true;
        }, false);

        return (unlocked, known);
    }

    /// <summary>Resolves a stand's name in the client's own language, or empty when it does not resolve.</summary>
    /// <param name="standId">The ChocoboTaxiStand row id.</param>
    /// <returns>The stand's name, or empty.</returns>
    public static string StandName(uint standId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (standId != 0 && ExcelSheetHelper.TryGetRow<ChocoboTaxiStand>(standId, out var row) && row is { } stand)
                return stand.PlaceName.ExtractText() ?? string.Empty;

            return string.Empty;
        }, string.Empty) ?? string.Empty;
    }
}
