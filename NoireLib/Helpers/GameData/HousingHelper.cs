using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Aetherytes;
using Dalamud.Game.Text.Evaluator;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace NoireLib.Helpers;

/// <summary>
/// Everything the game's own data says about residential housing: which territories are interiors and what kind each
/// one is, where every plot and apartment entrance stands, what an interior is called when the sheets leave it
/// unnamed, which placed objects are an interior's doors, and what the logged-in character's own address is.
/// <br/>
/// Nothing here is listed by hand, so a district, an interior, or an interior design added by a later patch is picked
/// up on its own. Every sheet read is guarded; a missing sheet yields an empty result.
/// </summary>
public static unsafe class HousingHelper
{
    /// <summary>The marker index of the main-division apartment entrance, past the sixty plot markers.</summary>
    public const ushort MainApartmentMarker = 60;

    /// <summary>The marker index of the subdivision apartment entrance.</summary>
    public const ushort SubdivisionApartmentMarker = 61;

    /// <summary>
    /// The PlaceName sheet row an estate-hall aetheryte points at for a Free Company estate. It is a fixed data row
    /// ("Estate Hall (Free Company)"), so matching on the row id tells a Free Company estate from a private one in
    /// every client language: the row id never changes, only the text it resolves to does.
    /// </summary>
    public const uint FreeCompanyEstatePlaceName = 1145;

    /// <summary>
    /// The PlaceName sheet row an estate-hall aetheryte points at for a private estate ("Estate Hall (Private)").
    /// A rented apartment shares this row and is told apart only by its apartment flag.
    /// </summary>
    public const uint PrivateEstatePlaceName = 1160;

    // The Addon sheet rows the game itself formats a housing address with: one for a plot and one for an apartment
    // room. Evaluating them, rather than composing the words here, makes the address read as the game writes it in
    // any client language, where both the wording and the order of the parts differ.
    private const uint PlotAddressAddon = 6378;
    private const uint RoomAddressAddon = 6479;

    private static Dictionary<(uint Territory, ushort Subrow), Vector3>? markerPositions;
    private static HashSet<uint>? districts;
    private static IReadOnlyDictionary<uint, HousingInteriorKind>? interiorKinds;
    private static IReadOnlyDictionary<uint, string>? interiorDesigns;
    private static IReadOnlyDictionary<HousingInteriorKind, string>? kindNames;

    /// <summary>
    /// The residential districts, being the territories the housing map-marker sheet lays plots out for. Derived from
    /// the sheet rather than listed, so a district added by a later patch is picked up without a code change and a
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
    /// Resolves a plot's entrance position in a residential territory. The <c>HousingMapMarkerInfo</c> subrow sheet
    /// carries a real three-dimensional point per marker, height included, and every ward of a district lays its
    /// markers out identically, so one position per index serves any ward.
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

    /// <summary>
    /// Reads the interior design every housing interior is decorated in from <c>HousingRenovation</c>, which names one
    /// per interior territory: a district's own three estate interiors carry that district's regional design and the
    /// district-agnostic ones carry the designs an estate can be renovated into. It is the only sheet that names the
    /// interiors the <c>TerritoryType</c> sheet leaves without a place name at all.
    /// </summary>
    /// <returns>Each interior's design name in the client's own language, keyed by its territory.</returns>
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
    /// level-file instance ids the row references. The sheet is keyed by an anonymous district index, so the instance
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
                    // A plot's marker index is its position in the land set, which is the same index the housing
                    // map-marker sheet positions it by, so the two line up without any further matching.
                    var kind = HousingInteriorKinds.FromPlotSize(plot.PlotSize);
                    if (kind.HasValue)
                        plots.Add(new HousingPlot(index, kind.Value));

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
    /// returns its plots. The sheet numbers its rows by an internal district index nothing else exposes; instance ids
    /// tie a row to a place, and a row matching nothing yields no plots rather than a guess.
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

    /// <summary>
    /// The display name of a housing interior, or empty when the territory is not one. An interior the game named
    /// keeps that name; one it left unnamed is called by the kind of place it is and the design it is decorated in,
    /// so "Territory 1375" reads as "Private House (Dark Minimalist Style)".<br/>
    /// Six interiors carry no place name at all: they are the district-agnostic designs an estate can be renovated
    /// into, so the game names them in <c>HousingRenovation</c> and leaves <c>TerritoryType</c> blank.
    /// </summary>
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
    /// kind shares. Empty for the apartment kinds, whose buildings are each named outright and share nothing, and
    /// empty in a language that names the district first.
    /// </summary>
    /// <param name="kind">The interior kind.</param>
    /// <returns>The shared name, or empty when there is none.</returns>
    public static string KindName(HousingInteriorKind kind)
        => KindNames().GetValueOrDefault(kind, string.Empty);

    /// <summary>
    /// The part every one of these names shares: for a set of interiors of one kind, the kind itself. The five
    /// districts' medium estates are all named "Private House - " plus the district, so the shared part is "Private
    /// House"; trailing separators and spaces are dropped.<br/>
    /// Needs at least two names, and yields empty for a language that names the district first, since there is
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

    /// <summary>
    /// Composes the name of an interior the game left unnamed, from the kind of place it is and the interior design it
    /// is decorated in. Either alone is used when the other is missing, which keeps a name coming out even when a
    /// patch adds a design before the sheets describe it fully. Pure.
    /// </summary>
    /// <param name="kindName">The kind's shared name, or empty when it was not derivable.</param>
    /// <param name="design">The interior design's name, or empty when the sheet carries none.</param>
    /// <returns>The composed name, or empty when neither part was known.</returns>
    public static string ComposeName(string kindName, string design)
    {
        if (kindName.Length == 0)
            return design;

        return design.Length == 0 ? kindName : $"{kindName} ({design})";
    }

    /// <summary>
    /// Picks an interior's two doors out of its placed event objects. Every housing interior is one room whose
    /// doorway out sits at the far positive-Z end and whose doorway further in, when it has one, sits at the far
    /// negative-Z end; matching on layout rather than name or object id keeps this working across languages and
    /// patches.<br/>
    /// The choice is only ever between objects in the same small room, so a wrong pick lands a few metres off
    /// inside the right interior, never in the wrong territory.
    /// </summary>
    /// <param name="territoryId">The interior territory the objects were read from.</param>
    /// <param name="objects">The interior's placed level objects.</param>
    /// <param name="restrictTo">
    /// When given, only these event objects are considered. An apartment and the private chambers are built from one
    /// level file, so both their doors sit at the same spot in both territories; this restricts to the one
    /// belonging to the territory being read. Ignored when it would leave nothing to choose from.
    /// </param>
    /// <returns>The interior's doors; both unfound when the level file held no event objects.</returns>
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

        // A single object is the whole room's doorway, so it serves as the way out and there is no way further in.
        var inward = nearest is { } candidate && candidate.InstanceId != outward.InstanceId
            ? new HousingDoor(candidate.Position, candidate.BaseId)
            : default;

        return new HousingInteriorDoors(territoryId, new HousingDoor(outward.Position, outward.BaseId), inward);
    }

    /// <summary>
    /// Classifies a teleport-list entry into an estate kind. The apartment and shared-house flags come straight off
    /// the entry; the private-versus-Free-Company split is not a flag, so the entry's aetheryte is read for its
    /// PlaceName row, which is a language-independent anchor.
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

    /// <summary>
    /// Whether a HouseId names a house the character actually owns. The game returns a not-owned slot as an
    /// all-bits-set sentinel (id <c>0xFFFF_FFFF_FFFF_FFFF</c> with every field maxed) rather than a zero, so both the
    /// sentinel and a zero id are treated as not owned.
    /// </summary>
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
    /// Reads the logged-in character's own address for a kind of estate, from the game's own housing data rather than
    /// from a teleport entry (which carries none for an estate hall). It reads the same from anywhere in the world, so
    /// the character need not be standing in a housing area, and it is the same data the in-game Estate Profile shows.
    /// <br/>
    /// A shared estate has no single owned address to show, so it reads as not owned.
    /// </summary>
    /// <param name="kind">The estate kind to read.</param>
    /// <returns>The address, or <see cref="HousingAddress.None"/> when the character owns nothing of that kind.</returns>
    public static HousingAddress ReadOwnedAddress(EstateKind kind)
    {
        // An apartment reads from ApartmentRoom, which carries the room number the game shows; the estate kinds read
        // from their matching EstateType.
        var estateType = kind switch
        {
            EstateKind.FreeCompanyEstate => EstateType.FreeCompanyEstate,
            EstateKind.PrivateEstate => EstateType.PersonalEstate,
            EstateKind.Apartment => EstateType.ApartmentRoom,
            _ => (EstateType)byte.MaxValue,
        };

        if (estateType == (EstateType)byte.MaxValue)
            return HousingAddress.None;

        // GetOwnedHouseId is static and reads the character's own housing data, so it does not need the housing
        // manager to be live (which it is not when the character stands outside a housing area).
        return SafeExecutor.ExecuteSafely(() =>
        {
            var house = HousingManager.GetOwnedHouseId(estateType, 0);
            if (!IsOwnedHouse(house))
                return HousingAddress.None;

            return new HousingAddress(true, house.WardIndex, house.PlotIndex, house.RoomNumber,
                house.IsApartment, house.ApartmentDivision);
        }, HousingAddress.None);
    }

    /// <summary>
    /// Formats an address through the game's own Addon rows, so the parts are worded and ordered the way the client's
    /// language does it. The parameters are positional and the same in every language, and their order is the row's
    /// rather than the reading order of the result: the plot address is "Plot &lt;lnum3&gt;, &lt;lnum2&gt; Ward,
    /// &lt;PlaceName lnum1&gt;", so the district comes first and the plot last.
    /// </summary>
    /// <param name="address">The address to format.</param>
    /// <param name="districtTerritoryId">
    /// The residential district the address is in. The addon takes its PlaceName row as a sheet reference rather
    /// than text, so the district reads in the client's own language with the game's own declension.
    /// </param>
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

            // Both address rows end in a trailing space of their own, which reads as a gap before a closing bracket.
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
                    // The marker's Map names the residential territory it belongs to, so a marker is keyed by the
                    // territory the character actually stands in rather than by the raw land-set row.
                    var territory = marker.Map.ValueNullable?.TerritoryType.RowId ?? 0;
                    if (territory == 0)
                        continue;

                    positions[(territory, marker.SubrowId)] = new Vector3(marker.X, marker.Y, marker.Z);
                    found.Add(territory);
                }
            }
        });

        // A sheet read failure leaves the cache empty, so every lookup simply misses and the caller falls back.
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

        // Only interiors the game named can say what their kind is called; the unnamed ones are excluded from
        // deriving it.
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
            // The districts are read in sheet order, which is fixed, so the shared part is the same every run.
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

    // Drops the separator the shared part ends on: the hyphen, interpunct, colon or bracket the game puts between a
    // kind and the district that follows it, together with the spaces around it.
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
