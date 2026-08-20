using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>One chocobo taxi ride: a directed hop between two stands with a fixed fare and a fixed duration.</summary>
/// <param name="FromStandId">The departure stand's ChocoboTaxiStand row id.</param>
/// <param name="ToStandId">The arrival stand's ChocoboTaxiStand row id.</param>
/// <param name="Fare">The ride's fare in gil.</param>
/// <param name="TimeSeconds">The ride's fixed duration in seconds, converted from the sheet's minutes.</param>
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
/// Reads the chocobo porter network out of the game's sheets: the stands, each ride's fare and duration, and the
/// NPC running each stand. A stand's position comes from that porter's placement via
/// <see cref="EventNpcHelper.FindPlacements"/>. A missing sheet yields empty.
/// </summary>
public static class ChocoboTaxiHelper
{
    /// <summary>
    /// Reads the stands and the rides leaving each, skipping the unused slots of the fixed-width target array.
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
                    // Every real ride takes at least a minute, so a zero TimeRequired marks an unused slot.
                    if (targetRef.ValueNullable is not { } taxi || taxi.Location.RowId == 0 || taxi.TimeRequired == 0)
                        continue;

                    // TimeRequired is in minutes.
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
    /// Finds the NPC running each stand, being the event NPC whose <c>ENpcData</c> references it, taking the
    /// lowest-numbered when several do.
    /// </summary>
    /// <param name="stands">The stands from <see cref="ReadStands"/>; their destinations are included.</param>
    /// <param name="scan">A pre-built <see cref="EventNpcHandlerScan"/> to read from, or null to scan the sheet here.</param>
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
    /// Reads the stands the logged-in character has registered from the client's unlock bitmask. Framework thread,
    /// once the character's state is loaded.
    /// </summary>
    /// <returns>The registered stand ids, and whether the read produced a real answer; an empty set with
    /// <c>Known</c> true means a character that has registered no stand.</returns>
    public static unsafe (IReadOnlySet<uint> Unlocked, bool Known) ReadUnlockedStands()
    {
        var unlocked = new HashSet<uint>();

        // Logged out, nothing is registered and the empty set is known; mid-login the same set means nothing.
        if (!CharacterHelper.IsStateReady)
            return (unlocked, CharacterHelper.IsLoggedOut);

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
