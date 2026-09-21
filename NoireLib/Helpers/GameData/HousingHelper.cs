using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Aetherytes;
using Dalamud.Game.Text.Evaluator;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace NoireLib.Helpers;

/// <summary>
/// Residential housing from the game's own data: interior territories and their kinds, plot and apartment entrances, interior names and doors, and the character's own addresses.<br/>
/// A missing sheet yields an empty result.
/// </summary>
public static unsafe partial class HousingHelper
{
    /// <summary>The marker index of the main-division apartment entrance, past the sixty plot markers.</summary>
    public const ushort MainApartmentMarker = 60;

    /// <summary>The marker index of the subdivision apartment entrance.</summary>
    public const ushort SubdivisionApartmentMarker = 61;

    /// <summary>
    /// The PlaceName sheet row an estate-hall aetheryte points at for a Free Company estate ("Estate Hall (Free
    /// Company)"). The row id never changes across client languages, only the text it resolves to.
    /// </summary>
    public const uint FreeCompanyEstatePlaceName = 1145;

    /// <summary>
    /// The PlaceName sheet row an estate-hall aetheryte points at for a private estate ("Estate Hall (Private)").
    /// A rented apartment shares this row and is told apart only by its apartment flag.
    /// </summary>
    public const uint PrivateEstatePlaceName = 1160;

    // The Addon rows the game formats a plot and an apartment address with.
    private const uint PlotAddressAddon = 6378;
    private const uint RoomAddressAddon = 6479;

    private static Dictionary<(uint Territory, ushort Subrow), Vector3>? markerPositions;
    private static HashSet<uint>? districts;
    private static IReadOnlyDictionary<uint, HousingInteriorKind>? interiorKinds;
    private static IReadOnlyDictionary<uint, string>? interiorDesigns;
    private static IReadOnlyDictionary<HousingInteriorKind, string>? kindNames;

    /// <summary>
    /// The residential districts, being the territories the housing map-marker sheet lays plots out for. Derived from
    /// the sheet, never listed by hand. A district added by a later patch is picked up without a code change, and a
    /// territory that merely looks residential is never mistaken for one.
    /// </summary>
    public static IReadOnlySet<uint> Districts
    {
        get
        {
            EnsureMarkersBuilt();
            return districts!;
        }
    }

    /// <summary>
    /// Drops the cached name tables so the next read resolves them again, in the client's current language. The
    /// marker positions and the sheet kinds are language-independent and are not dropped.
    /// </summary>
    public static void ResetNameCache()
    {
        interiorDesigns = null;
        kindNames = null;
    }

    /// <summary>
    /// Resolves a plot's map anchor in a residential territory, from the <c>HousingMapMarkerInfo</c> subrow sheet.
    /// The point is a real three-dimensional one, height included, but it sits inside the plot and above the ground,
    /// not at a door. It is measured six to twelve yalms up and fourteen to thirty out from the placard.
    /// Use <see cref="TryResolvePlot(uint, int, int, out HousingPlotLocation)"/> for the placard and the doors.
    /// <br/>
    /// Every ward of a district lays its markers out identically. One position per index serves any ward.
    /// </summary>
    /// <param name="territoryId">The residential territory.</param>
    /// <param name="plotIndex">The zero-based plot index (plot 1 is index 0, plot 31 is index 30 in the subdivision).</param>
    /// <param name="position">The plot's world position when found.</param>
    /// <returns>True when a position was found.</returns>
    public static bool TryGetPlotPosition(uint territoryId, ushort plotIndex, out Vector3 position)
    {
        EnsureMarkersBuilt();
        return markerPositions!.TryGetValue((territoryId, plotIndex), out position);
    }

    /// <summary>Resolves an apartment building's entrance position in a residential territory.</summary>
    /// <param name="territoryId">The residential territory.</param>
    /// <param name="subdivision">True for the subdivision apartment, false for the main-division one.</param>
    /// <param name="position">The apartment's world position when found.</param>
    /// <returns>True when a position was found.</returns>
    public static bool TryGetApartmentPosition(uint territoryId, bool subdivision, out Vector3 position)
    {
        EnsureMarkersBuilt();
        var subrow = subdivision ? SubdivisionApartmentMarker : MainApartmentMarker;
        return markerPositions!.TryGetValue((territoryId, subrow), out position);
    }

    /// <summary>
    /// Reads every housing interior territory and its kind from <c>HousingIndoorTerritory</c>: the only sheet that
    /// separates an apartment from the private chambers it shares a level file with, and the only one that says
    /// which of a district's three estate territories is small, medium, or large.
    /// </summary>
    /// <returns>The interior territories and their kinds.</returns>
    public static IReadOnlyList<HousingInteriorInfo> ReadInteriors()
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var rows = new List<HousingInteriorInfo>();
            var sheet = ExcelSheetHelper.GetSheet<HousingIndoorTerritory>();
            if (sheet == null)
                return (IReadOnlyList<HousingInteriorInfo>)rows;

            foreach (var row in sheet)
            {
                if (row.RowId != 0)
                    rows.Add(new HousingInteriorInfo(row.RowId, (HousingInteriorKind)row.Unknown0));
            }

            return rows;
        }, []) ?? [];
    }

    /// <summary>Reads each housing interior's design name from <c>HousingRenovation</c>, the only sheet naming the interiors <c>TerritoryType</c> leaves unnamed.</summary>
    /// <returns>Each interior's design name in the client language, keyed by territory.</returns>
    public static IReadOnlyDictionary<uint, string> ReadDesigns()
    {
        var empty = (IReadOnlyDictionary<uint, string>)new Dictionary<uint, string>();

        return SafeExecutor.ExecuteSafely(() =>
        {
            var designs = new Dictionary<uint, string>();
            var sheet = ExcelSheetHelper.GetSheet<HousingRenovation>();
            if (sheet == null)
                return empty;

            foreach (var row in sheet)
            {
                var territory = row.Territory.RowId;
                var name = row.Name.ExtractText();
                if (territory != 0 && !string.IsNullOrWhiteSpace(name))
                    designs[territory] = name;
            }

            return (IReadOnlyDictionary<uint, string>)designs;
        }, empty) ?? empty;
    }

    /// <summary>
    /// Reads which event handler every event object runs. Two housing doors doing the same job are separate EObj
    /// rows with separate names but run one handler: a district's apartment entrance and the lobby's door to the
    /// rooms share a handler, as do the lobby's way out and an apartment's. Matching handlers identifies two placed
    /// objects as the same door, pairing a district with its apartment building and telling an apartment's exit
    /// from the private chambers' exit when both sit in one level file.
    /// </summary>
    /// <returns>The event handler each EObj row runs, keyed by EObj row id.</returns>
    public static IReadOnlyDictionary<uint, uint> ReadEventObjectHandlers()
    {
        var empty = (IReadOnlyDictionary<uint, uint>)new Dictionary<uint, uint>();

        return SafeExecutor.ExecuteSafely(() =>
        {
            var handlers = new Dictionary<uint, uint>();
            var sheet = ExcelSheetHelper.GetSheet<EObj>();
            if (sheet == null)
                return empty;

            foreach (var row in sheet)
            {
                if (row.RowId != 0 && row.Data.RowId != 0)
                    handlers[row.RowId] = row.Data.RowId;
            }

            return (IReadOnlyDictionary<uint, uint>)handlers;
        }, empty) ?? empty;
    }

    /// <summary>
    /// Reads each district's plots from <c>HousingLandSet</c>, keeping the size every plot is built at and the
    /// level-file instance ids the row references. The sheet is keyed by an anonymous district index. The instance
    /// ids travel with the row and let the caller identify the district from its own level file.
    /// </summary>
    /// <returns>The land-set rows.</returns>
    public static IReadOnlyList<HousingLandSetInfo> ReadLandSets()
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var rows = new List<HousingLandSetInfo>();
            var sheet = ExcelSheetHelper.GetSheet<HousingLandSet>();
            if (sheet == null)
                return (IReadOnlyList<HousingLandSetInfo>)rows;

            foreach (var row in sheet)
            {
                var plots = new List<HousingPlot>();
                var instances = new List<uint>();
                var index = 0;
                foreach (var plot in row.LandSet)
                {
                    // A plot's marker index is its position in the land set.
                    var kind = HousingInteriorKinds.FromPlotSize(plot.PlotSize);
                    if (kind.HasValue)
                        plots.Add(new HousingPlot(index, kind.Value, plot.PlacardId));

                    if (plot.PlacardId != 0)
                        instances.Add(plot.PlacardId);

                    index++;
                }

                if (plots.Count > 0)
                    rows.Add(new HousingLandSetInfo(row.RowId, plots, instances));
            }

            return rows;
        }, []) ?? [];
    }

    /// <summary>
    /// Matches the land-set row whose referenced level-file instances are the ones actually placed in a district, and
    /// returns its plots. The sheet numbers its rows by an internal district index nothing else exposes. Instance ids
    /// tie a row to a place, and a row matching nothing yields no plots. It never guesses.
    /// </summary>
    /// <param name="districtTerritoryId">The residential district's TerritoryType row id.</param>
    /// <returns>The district's plots, or empty when no row matched.</returns>
    public static IReadOnlyList<HousingPlot> ReadPlots(uint districtTerritoryId)
        => MatchLandSet(ReadLandSets(), LevelFileHelper.ReadObjects(districtTerritoryId, LevelFileHelper.Files.PlanMap));

    /// <inheritdoc cref="ReadPlots"/>
    /// <param name="landSets">The land-set rows, from <see cref="ReadLandSets"/>.</param>
    /// <param name="districtObjects">The district's placed level objects.</param>
    /// <returns>The district's plots, or empty when no row matched.</returns>
    public static IReadOnlyList<HousingPlot> MatchLandSet(
        IReadOnlyList<HousingLandSetInfo> landSets,
        IReadOnlyList<LevelObject> districtObjects)
    {
        var placed = new HashSet<uint>();
        foreach (var levelObject in districtObjects)
            placed.Add(levelObject.InstanceId);

        foreach (var landSet in landSets)
        {
            var hits = 0;
            foreach (var instance in landSet.MarkerInstanceIds)
            {
                if (placed.Contains(instance))
                    hits++;
            }

            if (hits > 0 && hits == landSet.MarkerInstanceIds.Count)
                return landSet.Plots;
        }

        return [];
    }

    /// <summary>What kind of housing interior a territory is, or null when it is not one.</summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The kind, or null.</returns>
    public static HousingInteriorKind? KindOf(uint territoryId)
        => Kinds().TryGetValue(territoryId, out var kind) ? kind : null;

    /// <summary>The display name of a housing interior. An unnamed one is called by its kind and design, such as "Private House (Dark Minimalist Style)".</summary>
    /// <param name="territoryId">The interior's TerritoryType row id.</param>
    /// <returns>The name, or empty when the territory is not a housing interior.</returns>
    public static string InteriorName(uint territoryId)
    {
        if (!Kinds().TryGetValue(territoryId, out var kind))
            return string.Empty;

        var given = TerritoryHelper.SheetPlaceName(territoryId);
        if (given.Length > 0)
            return given;

        return ComposeName(KindName(kind), Designs().GetValueOrDefault(territoryId, string.Empty));
    }

    /// <summary>
    /// What a kind of housing interior is called with no district attached: the part every district's interior of that
    /// kind shares. Empty for the apartment kinds, whose buildings are each named outright and share nothing. Also
    /// empty in a language that names the district first.
    /// </summary>
    /// <param name="kind">The interior kind.</param>
    /// <returns>The shared name, or empty when there is none.</returns>
    public static string KindName(HousingInteriorKind kind)
        => KindNames().GetValueOrDefault(kind, string.Empty);

    /// <summary>
    /// The part every one of these names shares, for a set of interiors of one kind: the kind itself. The five
    /// districts' medium estates are all named "Private House - " plus the district. The shared part is "Private
    /// House". Trailing separators and spaces are dropped.<br/>
    /// Needs at least two names, and yields empty for a language that names the district first, where there is
    /// nothing to share. Pure, testable with hand-built names.
    /// </summary>
    /// <param name="names">The names to factor.</param>
    /// <returns>The shared leading part, or empty when there is none.</returns>
    public static string SharedName(IReadOnlyList<string> names)
    {
        if (names.Count < 2)
            return string.Empty;

        var shared = names[0];
        for (var i = 1; i < names.Count && shared.Length > 0; i++)
        {
            var other = names[i];
            var length = shared.Length < other.Length ? shared.Length : other.Length;

            var common = 0;
            while (common < length && shared[common] == other[common])
                common++;

            shared = shared[..common];
        }

        return TrimSeparators(shared);
    }

    /// <summary>Composes an unnamed interior's name from its kind and design. Either alone is used when the other is missing.</summary>
    /// <param name="kindName">The kind's shared name, or empty.</param>
    /// <param name="design">The interior design's name, or empty.</param>
    /// <returns>The composed name, or empty when neither part was known.</returns>
    public static string ComposeName(string kindName, string design)
    {
        if (kindName.Length == 0)
            return design;

        return design.Length == 0 ? kindName : $"{kindName} ({design})";
    }

    /// <summary>
    /// Picks an interior's two doors out of its placed event objects.<br/>
    /// The way out sits at the far positive-Z end of the room, the way further in at the far negative-Z end.
    /// </summary>
    /// <param name="territoryId">The interior territory the objects were read from.</param>
    /// <param name="objects">The interior's placed level objects.</param>
    /// <param name="restrictTo">The only event objects to consider. An apartment and the private chambers share one level file. Ignored when it would leave nothing.</param>
    /// <returns>The interior's doors. Both unfound when the level file held no event objects.</returns>
    public static HousingInteriorDoors FindInteriorDoors(
        uint territoryId,
        IReadOnlyList<LevelObject> objects,
        IReadOnlySet<uint>? restrictTo = null)
    {
        var restricted = restrictTo != null && HasAnyEventObject(objects, restrictTo);

        LevelObject? furthest = null;
        LevelObject? nearest = null;
        foreach (var levelObject in objects)
        {
            if (levelObject.Kind != LevelObjectKind.EventObject || (restricted && !restrictTo!.Contains(levelObject.BaseId)))
                continue;

            if (furthest is not { } far || levelObject.Position.Z > far.Position.Z)
                furthest = levelObject;

            if (nearest is not { } near || levelObject.Position.Z < near.Position.Z)
                nearest = levelObject;
        }

        if (furthest is not { } outward)
            return new HousingInteriorDoors(territoryId, default, default);

        // A single object is the way out. There is no way further in.
        var inward = nearest is { } candidate && candidate.InstanceId != outward.InstanceId
            ? new HousingDoor(candidate.Position, candidate.BaseId)
            : default;

        return new HousingInteriorDoors(territoryId, new HousingDoor(outward.Position, outward.BaseId), inward);
    }

    /// <summary>
    /// Classifies a teleport-list entry into an estate kind. The apartment and shared-house flags come straight off
    /// the entry. The private-versus-Free-Company split is not a flag. The entry's aetheryte is read for its
    /// PlaceName row instead, a language-independent anchor.
    /// </summary>
    /// <param name="entry">The teleport-list entry.</param>
    /// <returns>The estate kind.</returns>
    public static EstateKind ClassifyEstate(IAetheryteEntry entry)
        => ClassifyEstate(entry.IsApartment, entry.IsSharedHouse, AetheryteHelper.ReadEstateHall(entry.AetheryteId).PlaceNameId);

    /// <summary>
    /// The classification rule over the flags alone, for a caller holding them without the entry and for testing the
    /// rule with no game behind it. Prefer <see cref="ClassifyEstate(IAetheryteEntry)"/>.
    /// </summary>
    /// <param name="isApartment">The entry's apartment flag.</param>
    /// <param name="isSharedHouse">The entry's shared-house flag.</param>
    /// <param name="placeNameRowId">The estate-hall aetheryte's PlaceName row id.</param>
    /// <returns>The estate kind.</returns>
    public static EstateKind ClassifyEstate(bool isApartment, bool isSharedHouse, uint placeNameRowId)
    {
        if (isApartment)
            return EstateKind.Apartment;

        if (isSharedHouse)
            return EstateKind.SharedEstate;

        if (placeNameRowId == FreeCompanyEstatePlaceName)
            return EstateKind.FreeCompanyEstate;

        return EstateKind.PrivateEstate;
    }

    /// <summary>Whether a HouseId names an owned house. A not-owned slot comes back as an all-bits-set sentinel.</summary>
    /// <param name="house">The HouseId the game returned.</param>
    /// <returns>True when it names an owned house.</returns>
    public static bool IsOwnedHouse(HouseId house) => IsOwnedHouse(house.Id, house.TerritoryTypeId);

    /// <summary>
    /// The sentinel rule over the raw fields, for a caller holding them without the HouseId and for testing the rule
    /// with no game behind it. Prefer <see cref="IsOwnedHouse(HouseId)"/>.
    /// </summary>
    /// <param name="id">The HouseId's raw id.</param>
    /// <param name="territoryTypeId">The HouseId's territory, a second guard against the sentinel.</param>
    /// <returns>True when the fields name an owned house.</returns>
    public static bool IsOwnedHouse(ulong id, ushort territoryTypeId)
        => id != 0 && id != ulong.MaxValue && territoryTypeId != ushort.MaxValue;

    /// <summary>
    /// Reads the logged-in character's own address for a kind of estate, from the game's own housing data and not
    /// from a teleport entry (which carries none for an estate hall). It reads the same from anywhere in the world.
    /// The character need not be standing in a housing area. It is the same data the in-game Estate Profile shows.
    /// <br/>
    /// A shared estate has no single owned address to show. It reads as not owned.
    /// </summary>
    /// <param name="kind">The estate kind to read.</param>
    /// <returns>The address, or <see cref="HousingAddress.None"/> when the character owns nothing of that kind.</returns>
    public static HousingAddress ReadOwnedAddress(EstateKind kind)
    {
        // An apartment reads ApartmentRoom, which carries the room number the game shows.
        var estateType = kind switch
        {
            EstateKind.FreeCompanyEstate => EstateType.FreeCompanyEstate,
            EstateKind.PrivateEstate => EstateType.PersonalEstate,
            EstateKind.Apartment => EstateType.ApartmentRoom,
            _ => (EstateType)byte.MaxValue,
        };

        return estateType == (EstateType)byte.MaxValue ? HousingAddress.None : ReadOwnedEstate(estateType);
    }

    // GetOwnedHouseId is static and needs no live housing manager.
    private static HousingAddress ReadOwnedEstate(EstateType estateType)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var house = HousingManager.GetOwnedHouseId(estateType, 0);
            if (!IsOwnedHouse(house))
                return HousingAddress.None;

            return new HousingAddress(true, house.WardIndex, house.PlotIndex, house.RoomNumber,
                house.IsApartment, house.ApartmentDivision, house.TerritoryTypeId, house.IsWorkshop);
        }, HousingAddress.None);
    }

    /// <summary>
    /// The interior territories that belong to one residential district: its cottage, house and mansion interiors, its
    /// private chambers, its company workshop, and its apartment and lobby. Membership comes from the level-file region
    /// the district and its interiors share. No territory is listed by hand.
    /// </summary>
    /// <param name="districtTerritoryId">The residential district.</param>
    /// <returns>The district's interiors, empty when it is not a district or names no level files.</returns>
    public static IReadOnlyList<HousingInteriorInfo> InteriorsOf(uint districtTerritoryId)
    {
        EnsureInteriorsByDistrict();
        return interiorsByDistrict!.GetValueOrDefault(districtTerritoryId, []);
    }

    /// <summary>
    /// Resolves an interior onto the one a district actually holds. Six interiors belong to no district at all: they
    /// are the designs an estate can be renovated into, and a character standing in one is standing in that district's
    /// interior of the same size under different decor. Anything already belonging to the district, and anything that
    /// is not a housing interior, comes back unchanged.
    /// </summary>
    /// <param name="interiorTerritoryId">The interior the character is in.</param>
    /// <param name="districtTerritoryId">The district the interior belongs to, from the indoor house.</param>
    /// <returns>The district's own interior of that kind, or the input when it needs no resolving.</returns>
    public static uint ResolveInterior(uint interiorTerritoryId, uint districtTerritoryId)
    {
        if (interiorTerritoryId == 0 || districtTerritoryId == 0)
            return interiorTerritoryId;

        var interiors = InteriorsOf(districtTerritoryId);
        if (interiors.Count == 0 || KindOf(interiorTerritoryId) is not { } kind)
            return interiorTerritoryId;

        foreach (var interior in interiors)
        {
            if (interior.TerritoryId == interiorTerritoryId)
                return interiorTerritoryId;
        }

        foreach (var interior in interiors)
        {
            if (interior.Kind == kind)
                return interior.TerritoryId;
        }

        return interiorTerritoryId;
    }

    private static Dictionary<uint, List<HousingInteriorInfo>>? interiorsByDistrict;

    private static void EnsureInteriorsByDistrict()
    {
        if (interiorsByDistrict != null)
            return;

        var byDistrict = new Dictionary<uint, List<HousingInteriorInfo>>();
        SafeExecutor.ExecuteSafely(() =>
        {
            var regions = new Dictionary<string, List<uint>>();
            foreach (var district in Districts)
            {
                var region = LevelFileHelper.ResolveRegionRoot(district);
                if (region.Length == 0)
                    continue;

                if (!regions.TryGetValue(region, out var list))
                {
                    list = [];
                    regions[region] = list;
                }

                list.Add(district);
            }

            foreach (var interior in ReadInteriors())
            {
                var region = LevelFileHelper.ResolveRegionRoot(interior.TerritoryId);
                if (region.Length == 0 || !regions.TryGetValue(region, out var districtIds))
                    continue;

                foreach (var district in districtIds)
                {
                    if (!byDistrict.TryGetValue(district, out var list))
                    {
                        list = [];
                        byDistrict[district] = list;
                    }

                    list.Add(interior);
                }
            }
        });

        interiorsByDistrict = byDistrict;
    }

    /// <summary>The character's own private chambers, inside their Free Company's estate. Chambers are rented separately. Reads from anywhere.</summary>
    /// <returns>The address, or <see cref="HousingAddress.None"/> when the character has no chambers.</returns>
    public static HousingAddress ReadOwnedChambers() => ReadOwnedEstate(EstateType.PersonalChambers);

    /// <summary>
    /// The house the character is currently standing inside, from the game's own indoor state: which district, ward
    /// and plot it is, and for an apartment which division and room. This is the only thing that says <b>which</b> of
    /// the estates sharing an interior territory the character actually walked into, since every plot of a size opens
    /// into one and the same territory.
    /// </summary>
    /// <returns>
    /// The address, or <see cref="HousingAddress.None"/> when the character is not inside a house. A workshop reads
    /// as owned with <see cref="HousingAddress.IsWorkshop"/> set.
    /// </returns>
    public static HousingAddress ReadCurrentIndoorHouse()
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (!CharacterHelper.IsStateReady)
                return HousingAddress.None;

            var manager = HousingManager.Instance();
            if (manager == null || manager->IsOutside())
                return HousingAddress.None;

            var house = manager->GetCurrentIndoorHouseId();
            if (!IsOwnedHouse(house))
                return HousingAddress.None;

            return new HousingAddress(true, house.WardIndex, house.PlotIndex, house.RoomNumber,
                house.IsApartment, house.ApartmentDivision, house.TerritoryTypeId, house.IsWorkshop);
        }, HousingAddress.None);
    }

    /// <summary>
    /// Formats an address through the game's own Addon rows, in the client language's wording and part order.<br/>
    /// The plot row is "Plot &lt;lnum3&gt;, &lt;lnum2&gt; Ward, &lt;PlaceName lnum1&gt;".
    /// </summary>
    /// <param name="address">The address to format.</param>
    /// <param name="districtTerritoryId">The residential district. The addon takes its PlaceName row as a sheet reference.</param>
    /// <returns>The formatted address, or empty when it could not be formatted.</returns>
    public static string FormatAddress(HousingAddress address, uint districtTerritoryId)
    {
        if (!address.Owned || !NoireService.IsInitialized())
            return string.Empty;

        return SafeExecutor.ExecuteSafely(() =>
        {
            var district = TerritoryHelper.PlaceNameId(districtTerritoryId);
            var ward = (uint)(address.Ward + 1);
            var plot = (uint)(address.Plot + 1);

            if (address.IsApartment)
            {
                Span<SeStringParameter> room = [district, ward, plot, (uint)address.Room];
                return NoireService.SeStringEvaluator.EvaluateFromAddon(RoomAddressAddon, room).ExtractText().Trim();
            }

            // Both address rows end in a trailing space.
            Span<SeStringParameter> parameters = [district, ward, plot];
            return NoireService.SeStringEvaluator.EvaluateFromAddon(PlotAddressAddon, parameters).ExtractText().Trim();
        }, string.Empty) ?? string.Empty;
    }

    private static void EnsureMarkersBuilt()
    {
        if (markerPositions != null)
            return;

        var positions = new Dictionary<(uint, ushort), Vector3>();
        var found = new HashSet<uint>();

        SafeExecutor.ExecuteSafely(() =>
        {
            var sheet = ExcelSheetHelper.GetSubrowSheet<HousingMapMarkerInfo>();
            if (sheet == null)
                return;

            foreach (var collection in sheet)
            {
                foreach (var marker in collection)
                {
                    // The marker's Map names its residential territory.
                    var territory = marker.Map.ValueNullable?.TerritoryType.RowId ?? 0;
                    if (territory == 0)
                        continue;

                    positions[(territory, marker.SubrowId)] = new Vector3(marker.X, marker.Y, marker.Z);
                    found.Add(territory);
                }
            }
        });

        districts = found;
        markerPositions = positions;
    }

    private static IReadOnlyDictionary<uint, HousingInteriorKind> Kinds()
    {
        if (interiorKinds != null)
            return interiorKinds;

        var map = new Dictionary<uint, HousingInteriorKind>();
        foreach (var interior in ReadInteriors())
            map[interior.TerritoryId] = interior.Kind;

        return interiorKinds = map;
    }

    private static IReadOnlyDictionary<uint, string> Designs() => interiorDesigns ??= ReadDesigns();

    private static IReadOnlyDictionary<HousingInteriorKind, string> KindNames()
    {
        if (kindNames != null)
            return kindNames;

        // Only interiors the game named can say what their kind is called.
        var byKind = new Dictionary<HousingInteriorKind, List<string>>();
        foreach (var (territoryId, kind) in Kinds())
        {
            var name = TerritoryHelper.SheetPlaceName(territoryId);
            if (name.Length == 0)
                continue;

            if (!byKind.TryGetValue(kind, out var names))
            {
                names = [];
                byKind[kind] = names;
            }

            names.Add(name);
        }

        var result = new Dictionary<HousingInteriorKind, string>();
        foreach (var (kind, names) in byKind)
        {
            var shared = SharedName(names);
            if (shared.Length > 0)
                result[kind] = shared;
        }

        return kindNames = result;
    }

    private static bool HasAnyEventObject(IReadOnlyList<LevelObject> objects, IReadOnlySet<uint> wanted)
    {
        foreach (var levelObject in objects)
        {
            if (levelObject.Kind == LevelObjectKind.EventObject && wanted.Contains(levelObject.BaseId))
                return true;
        }

        return false;
    }

    // The hyphen, interpunct, colon or bracket the game puts between a kind and its district.
    private static string TrimSeparators(string text)
    {
        var end = text.Length;
        while (end > 0)
        {
            var c = text[end - 1];
            if (!char.IsWhiteSpace(c) && !char.IsPunctuation(c) && !char.IsSymbol(c))
                break;

            end--;
        }

        return end == text.Length ? text : text[..end];
    }
}
