# Helper Documentation : Game Data Helpers

You are reading the documentation for the `Helpers/GameData` helpers.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [The Helpers](#the-helpers)
- [Shared Models](#shared-models)
- [Archive Paths](#archive-paths)
- [Level Files](#level-files)
- [Coordinates](#coordinates)
- [The Eorzean Clock](#the-eorzean-clock)
- [Weather](#weather)
- [Territories](#territories)
- [Aetherytes and Aethernet Shards](#aetherytes-and-aethernet-shards)
- [Warps](#warps)
- [Chocobo Taxis](#chocobo-taxis)
- [Housing](#housing)
- [Shops](#shops)
- [Duties](#duties)
- [Classes and Jobs](#classes-and-jobs)
- [Icons](#icons)
- [Text Commands](#text-commands)
- [Worlds and Travel](#worlds-and-travel)
- [Live Character and World State](#live-character-and-world-state)
- [Rules That Hold Everywhere](#rules-that-hold-everywhere)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The game data helpers are static helpers in the `NoireLib.Helpers` namespace that answer everything the game's own
**files and sheets** say about the world, one helper per subject. They cover:

- **Level (`.lgb`) file reading** into a flat, Lumina-free object model
- **Coordinate conversion** between world positions, map-marker pixels and in-game map coordinates
- **Territory facts**: names, level paths, the real/duty/mountable sets, zone-crossing quest gates
- **The aetheryte network**: crystal identity and position, residential shards, attunement, fares
- **Warps and chocobo taxis**: what teleports the character, what it costs, what unlocks it
- **Residential housing**: districts, interiors, plots, apartments, doors, naming, the character's own address
- **Shops**: what every vendor sells, what it charges, and which NPC stands behind it
- **Duties**: what the duty finder lists, what it requires, and what the character has unlocked or cleared
- **Classes and jobs**: names, roles, category membership, and the character's level in each
- **Icons and text commands**: an icon id resolved to a drawable texture, a command resolved into the client's language
- **Worlds and live state**: worlds and data centres, whether a placement is actually standing there, quest progress,
  active festivals

The folder is organisation only: everything is `public` and lives in `NoireLib.Helpers`, so one `using` reaches all
of it. Every sheet and file read is wrapped in `SafeExecutor`, so a missing sheet or an unreadable file yields an
empty result rather than throwing.

> **A level file states what *could* stand in a territory, never what does.** Its layers belong to layer sets the
> game switches on and off for quest progress, instance, phase and season, and nothing in the files says which are
> on for a given character. `LayoutHelper` is the only thing that can answer that, and only for the territory the
> client currently has loaded.

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

There is nothing to construct and nothing to register. Import the namespace and call:

```csharp
using NoireLib.Helpers;

// Where does this territory's aetheryte stand?
var aetherytes = AetheryteHelper.ApplyLevelPositions(AetheryteHelper.ReadAll());

// What is this place called, in whatever language the client runs in?
var name = TerritoryHelper.Name(territoryId);

// Where is plot 17 of a residential ward?
if (HousingHelper.TryGetPlotPosition(territoryId, 16, out var plot))
    NoireLogger.LogInfo($"Plot 17 stands at {plot}.");
```

---

## The Helpers

| Helper | Answers |
|---|---|
| `LevelFileHelper` | Reads a territory's placed objects out of its `.lgb` level files. |
| `MapCoordinateHelper` | Converts between world positions, map-marker pixels and in-game map coordinates; reads map rows and markers. |
| `EorzeaTimeHelper` | The Eorzean clock, day and night, and the eight-hour windows the weather is decided in. |
| `WeatherHelper` | What the weather is and what it will be, forecast from the moment alone. |
| `TerritoryHelper` | Territory names, level paths, the real/duty/mountable sets, zone-crossing quest gates, and the canonical-territory rule. |
| `AetheryteHelper` | Aetheryte and shard identity, their world positions, residential aethernet crystals, and what the character has attuned. |
| `EventNpcHelper` | Which NPCs run which event handlers, and where each of them stands. |
| `WarpHelper` | The interactables that teleport the character, what they cost, and what unlocks them. |
| `ChocoboTaxiHelper` | The porter network: stands, rides, fares, durations, and which NPC runs each stand. |
| `HousingHelper` | Residential districts, interiors and their kinds, plot and apartment positions, interior doors, interior naming, and the character's own address. |
| `ShopHelper` | What every shop sells and charges, indexed both ways, plus the menus that hide shops from a handler scan. |
| `DutyHelper` | What the duty finder says about a duty, and what the character has unlocked or cleared. |
| `ClassJobHelper` | Class and job identity, roles, `ClassJobCategory` membership, and the character's level in each. |
| `IconHelper` | An icon id resolved to its game path or its texture, and the icon a sheet row names. |
| `TextCommandHelper` | The client's own text commands, and the rewrite that makes one work in any language. |
| `LayerSetHelper` | Which territory a level layer belongs to, when several territories share one level directory. |
| `LayoutHelper` | Whether a placement is actually standing in the world right now. |
| `QuestHelper` | Whether quests are complete or accepted, and how far each accepted one has got. |
| `WorldHelper` | Worlds, data centres, where the character is against where they live, and which seasonal events are running. |

---

## Shared Models

`LevelObject` (with `LevelObjectKind` and `LevelObjectFilter`) is the flattened placed object every file read
produces and most of the other helpers consume. `MapProjection` / `MapMarkerEntry` / `ProjectedMapMarker`,
`TerritoryEntry`, `AetheryteEntry`, `ResidentialShard`, `WarpDefinition`, `ChocoboTaxiStandInfo` /
`ChocoboTaxiRide`, `EventNpcHandlerScan`, `QuestProgress`, `ShopCost` / `ShopOffer` / `ShopInfo` / `ShopCatalog`,
`DutyInfo`, `ClassJobInfo`, `TextCommandInfo`, `WorldInfo` and the `Housing*` records are each documented on the
helper that produces them.

**None of them references Lumina**, so anything built on top of these records is testable with hand-built fixtures
and no game behind it.

---

## Archive Paths

`GamePathHelper` answers "where would that file be" as pure string rules. Nothing here opens an archive, so a caller
can resolve one file's reference to another before deciding whether to load it.

```csharp
// A model's material. Background models store it outright; character models store it relative,
// beginning with a slash, and it resolves BESIDE the model's folder rather than under it.
string? mtrl = GamePathHelper.ResolveMaterialPath(
    "chara/equipment/e0001/model/c0101e0001_top.mdl", "/mt_c0101e0001_top_a.mtrl", variant: 1);
// chara/equipment/e0001/material/v0001/mt_c0101e0001_top_a.mtrl

// A character material's file name encodes its owner, so an equipment model naming its wearer's
// skin resolves into the human directory. Several candidates come back; take the first that loads.
IReadOnlyList<string> candidates = GamePathHelper.ResolveMaterialByOwnerName("/mt_c0201b0001_a.mtrl");

// The DirectX 11 texture sits beside the named one with a doubled dash on the file name.
string dx11 = GamePathHelper.Dx11TexturePath("chara/.../texture/v01_c0101e0001_top_d.tex");

// Furniture pairs .../bgparts/x.mdl with .../asset/x.sgb.
string? scene = GamePathHelper.SceneBesideModel("bgcommon/hou/indoor/general/0681/bgparts/fun_b0_m0681.mdl");
```

---

## Level Files

```csharp
// Everything interactable a territory places, across planmap, planevent, planner and planlive.
var objects = LevelFileHelper.ReadPlacements(territoryId);

// Or one file, keeping only what you asked for.
var crystals = LevelFileHelper.ReadObjects(territoryId, LevelFileHelper.Files.PlanMap, new LevelObjectFilter
{
    Kinds = new HashSet<LevelObjectKind> { LevelObjectKind.Aetheryte },
});

// Group what came back.
var exits = LevelFileHelper.OfKind(objects, LevelObjectKind.ExitRange);
```

`LevelFileHelper.Files` names the five files by role (`PlanMap`, `PlanEvent`, `Planner`, `PlanLive`, `Background`)
and `Files.Interactable` is the four `ReadPlacements` merges. `PlanLive` is the layout of the places the game
switches on and off, such as a Grand Company barracks or a story tower's floors; it holds arrival volumes that
appear in no other file, so a warp landing in one of those places resolves to no position without it.
`ResolveLevelDirectory` and `ResolveRegionRoot` are the pure path rules, usable without reading anything.

An `ExitRange` carries `ExitKind` alongside its destination. A `LevelExitKind.ZoneLine` names the territory it
leads to; a `LevelExitKind.IntraZoneTeleport` names none, because it moves the character within the territory it
already stands in, so its `DestInstanceId` resolves against that same territory. Reading the destination alone
makes the second kind look like a broken zone line.

**Filter during the read, not after.** A level file holds thousands of NPCs and far more scenery, and a
whole-world pass that keeps everything exhausts the heap. `LevelObjectFilter` drops an object before it is ever
added to the list:

- `Kinds` restricts what is kept at all.
- `EventNpcBaseIds` / `EventObjectBaseIds` keep only the interactables you already know you want.
- `IncludeUnmappedKinds` (or `LevelObjectFilter.Everything`) keeps the rest, which is most of a file.

The default drops `LevelObjectKind.Other`.

**Reads are sequential on purpose.** Lumina serializes file access internally, so parallelising a whole-world pass
barely helps and saturates every core. The helper yields briefly between territories instead, so the game's own
file reads on the framework thread can interleave and the client does not freeze.

**Arrival points are indexed once.** A warp lands the character in a `PopRange` volume identified by instance id,
so `BuildPopRangeIndex` turns a whole-world read into the `(territory, instance) -> position` lookup every arrival
resolves through:

```csharp
var arrivals = LevelFileHelper.BuildPopRangeIndex(objectsByTerritory);
if (arrivals.TryGetValue((destinationTerritory, warp.ArrivalInstanceId), out var landing))
    NoireLogger.LogInfo($"That warp lands at {landing}.");
```

### Which territory a placement belongs to

One level directory is shared by several `TerritoryType` rows, and reading it attributes every placement to all of
them. That is how a story-scene NPC reads as a permanent fixture: the shuttle pilot offering "Enter the tiring
room?" is in the Prima Vista Tiring Room's own level file, but belongs to the *second* row of that place, and is
not standing in the one the character walks into.

The files answer it. Each layer lists the **layer sets** it belongs to, and the level-base (`.lvb`) file beside the
level files maps every layer set to a `TerritoryType` row:

```csharp
foreach (var set in LayerSetHelper.ReadLayerSets(territoryId))
    NoireLogger.LogInfo($"Layer set {set.LayerSetId} belongs to territory {set.TerritoryId}.");
```

Every object read carries the answer, so nothing has to be looked up twice:

```csharp
foreach (var placed in LevelFileHelper.ReadPlacements(territoryId))
{
    if (!placed.BelongsTo(territoryId))
        continue;   // it stands in another row of this place, not in this one
}
```

`LevelObject.LayerTerritories` is empty for an unconditional layer, which is most of them, and for a layer set the
level's own table does not describe. Both mean "nothing rules it out", so `BelongsTo` is true: a wrong exclusion
removes a real placement with nothing to show it was ever there.

Both structures are read from the file bytes. Lumina's `Layer.LayerSetReferences` resolves its list against the
layer rather than against the list, so it reads into the instance-object table and returns numbers that are not
layer sets at all.

---

## Coordinates

A spot on a map has three names, and all three are the same point through one map's size factor and offset:

- a **world** position, which is what a game object carries;
- a **marker** pixel in the 0-2048 space a map image is authored in, which is what the `MapMarker` sheet stores;
- a **map coordinate**, which is the "X: 12.3, Y: 9.8" a flag or a chat link is written with.

**None of them carries a height.** A real altitude only ever comes from a placed object's own transform.

```csharp
// A territory can span several maps with different offsets, so project through each map's own projection.
var markers = MapCoordinateHelper.ProjectMarkers(territoryId, MapMarkerDataType.AethernetShard);

if (MapCoordinateHelper.TryFindNearestMarker(markers, crystalPosition, out var nearest))
    var ward = TerritoryHelper.PlaceName(nearest.Marker.DataKey);

// The flag coordinate for a world position, and back again.
foreach (var map in MapCoordinateHelper.ReadMaps(territoryId))
{
    var (x, y) = MapCoordinateHelper.WorldToMapCoordinate(worldPosition, map);
    var back = MapCoordinateHelper.MapCoordinateToWorld(x, y, map);
}
```

`MapMarkerDataType` names the marker kinds worth filtering on (`Map`, `InstanceEntrance`, `Aetheryte`,
`AethernetShard`). Each conversion also has a loose-float overload for a caller that already holds the size factor
and offsets, but the projection-taking form is the one to reach for.

---

## The Eorzean Clock

Eorzea time is **not read from the game**. It is a pure function of real time, running at 1440/70 the speed of it,
so every method works for any moment, past or future.

```csharp
EorzeaTimeHelper.Hour;                       // 0 to 23, right now
EorzeaTimeHelper.IsNight;                    // night runs 18:00 to 05:59
EorzeaTimeHelper.HourAt(someMoment);

// Weather is decided in eight-hour windows, so they are first-class here.
var start = EorzeaTimeHelper.WeatherWindowStart(DateTimeOffset.UtcNow);
var next = EorzeaTimeHelper.NextWeatherWindow(DateTimeOffset.UtcNow);
var upcoming = EorzeaTimeHelper.WeatherWindows(DateTimeOffset.UtcNow, 12);
```

A window always starts at Eorzean 00:00, 08:00 or 16:00, and lasts 1400 real seconds (just over 23 minutes).

`ToEorzea` returns a `DateTimeOffset` whose **time of day** is the Eorzean one. Its date part is not a date in
Eorzea and means nothing: it is where the Eorzean second count lands on a calendar counting twenty times too fast.
Read the hour off it, never the day.

`FixedTimeOfDay(territoryId)` reports the zones that freeze the clock instead of running it, which many instanced
and cutscene territories do.

---

## Weather

**Weather is not stored anywhere; it is computed.** The game rolls a number from the moment alone, the same number
on every client and every world, and looks it up in the territory's own rate table. Nothing has to have happened
yet for the answer to be known, so a window weeks away reads as easily as the current one.

```csharp
var now = WeatherHelper.Current(territoryId);                 // computed for this moment
var actual = WeatherHelper.Active();                          // what the client is really showing

foreach (var window in WeatherHelper.Forecast(territoryId, windows: 12))
    NoireLogger.LogInfo($"{window.Start:t}: {WeatherHelper.Name(window.WeatherId)}");
```

`Active()` is the one thing here that needs a running game, and it is the ground truth: it can differ from
`Current` in a territory whose weather is set by something other than the clock (a duty, a cutscene, a quest
phase).

### Waiting for weather

```csharp
// The next window with one of these weathers.
var fog = WeatherHelper.FindNext(territoryId, new HashSet<uint> { 5, 6 });

// A transition, which is how most timed conditions are actually stated.
var afterFog = WeatherHelper.FindNextTransition(territoryId,
    (previous, current) => previous == 6 && current == 1);
```

**A predicate over the current window alone cannot express a transition**, and a great many timed nodes and fish
are stated as one ("clear skies, after fog"); `FindNextTransition` takes both.

### Running without a game

Only the rate table comes from the sheets. `Forecast` and `FindNextTransition` each have an overload taking the
table directly, so a forecast can be computed against a table read earlier, or a hypothetical one:

```csharp
var rates = WeatherHelper.ReadRates(territoryId);             // [(weatherId, rate), ...]
var forecast = WeatherHelper.Forecast(rates, territoryId, windows: 100);
```

`Resolve(rates, chance)` and `ChanceAt(moment)` are the two pure rules underneath, both usable on their own.

---

## Territories

```csharp
TerritoryHelper.Name(territoryId);          // display name, falling back to a housing interior's name
TerritoryHelper.SheetPlaceName(territoryId);// the sheet's own PlaceName only, with no fallback
TerritoryHelper.PlaceNameId(territoryId);   // the row id, which is what you store
TerritoryHelper.Bg(territoryId);            // the level path, e.g. ffxiv/fst_f1/fld/f1f1/level/f1f1

TerritoryHelper.ReadReal();                 // territories that are a real place, not a placeholder row
TerritoryHelper.ReadQueueableDuties();      // territories reachable through the Duty Finder
TerritoryHelper.ReadMountable();            // territories that allow a mount (cached)
TerritoryHelper.ReadAetherCurrentZones();   // (territory, CompFlgSet) pairs for flight unlocks (cached)
TerritoryHelper.ReadZoneCrossingGates();    // the quest gates that close a zone boundary
```

### The canonical territory

Many `TerritoryType` rows share one level file: the open-world zone plus its duty, quest-battle and PvP versions
(Central Shroud alone has nineteen).

```csharp
var aliases = TerritoryHelper.BuildAliases(preferred: TerritoryHelper.ReadReal());
var canonical = TerritoryHelper.ResolveAlias(aliases, territoryId);
```

Sharing a level file is **not** on its own enough to call two rows the same place: the game reuses one file for
genuinely different destinations, such as a residential district's apartment and its private chambers. A row is a
variant only when it also carries the same **PlaceName row id**, compared as an id and never as text.

---

## Aetherytes and Aethernet Shards

Identity comes from the sheet and position comes from the crystals placed in the level files, so the two are read
in two steps:

```csharp
var aetherytes = AetheryteHelper.ApplyLevelPositions(AetheryteHelper.ReadAll());

AetheryteHelper.Name(aetheryteId);          // the crystal's name in the client's language
AetheryteHelper.ReadUnlocked();             // what the logged-in character has attuned to
AetheryteHelper.ReadTeleportFares();        // the gil fare per destination, from the teleport list
```

An `AetheryteEntry` marked `ArrivalOnly` (an airship landing, say) has no crystal to be positioned from at all.

### Estate halls

An estate hall is an Aetheryte row that is not a crystal and belongs to no aethernet group. The row-taking form is
the one to call:

```csharp
if (ExcelSheetHelper.TryGetRow<Aetheryte>(aetheryteId, out var row) && row is { } aetheryte
    && AetheryteHelper.IsEstateHall(aetheryte))
{
    var (_, placeNameId, placeName) = AetheryteHelper.ReadEstateHall(aetheryteId);
}
```

### Residential shards

A residential aethernet shard has no Aetheryte row of its own: it exists only as a crystal placed in the
district's level file, matched by its shared-group asset path
(`AetheryteHelper.ResidentialCrystalAssetPrefix`) and labelled from the map marker nearest to it.

```csharp
var shards = AetheryteHelper.ReadResidentialShards(districtTerritoryId);
foreach (var shard in shards)
    NoireLogger.LogInfo($"{TerritoryHelper.PlaceName(shard.PlaceNameId)} at {shard.Position}");
```

Pass the district's already-read `LevelObject` list as the optional second argument when you have one, so the
district is not read twice.

---

## Warps

A warp is an interactable that teleports the character to a set landing spot. They are found by the **event
handler** they run, so a scan picks up every one of them in every client language and takes nothing that merely
looks like one.

```csharp
var warpIds = WarpHelper.ReadWarpIds();

var byNpc = WarpHelper.ScanEventNpcWarps();      // ENpcBase -> the warps it offers
var byObject = WarpHelper.ScanEventObjectWarps();// EObj -> the warps it offers, direct and array-handler wired

foreach (var (baseId, definitions) in byNpc)
{
    foreach (var warp in definitions)
        NoireLogger.LogInfo($"{WarpHelper.Label(warp)} -> {TerritoryHelper.Name(warp.DestTerritoryId)} for {warp.GilCost} gil");
}
```

A `WarpDefinition` carries what it costs (`GilCost`), what it needs (`ClassLevel`, `RequiredQuests` with a
`QuestThreshold`) and where it lands (`DestTerritoryId` plus the `ArrivalInstanceId` that resolves through
`LevelFileHelper.BuildPopRangeIndex`).

**`QuestThreshold` equal to the quest count is an "all of"; below it, it is a genuine M-of-N**: this expresses an
unlock reachable by several storylines.

`LogicId` is the warp's `WarpLogic` family, named by `WarpHelper.LogicName`. It is an internal name, never shown
to a player and never localised, so it is safe to use as a classifier:

```csharp
if (WarpHelper.LogicName(warp.LogicId).StartsWith("WarpInn"))
    NoireLogger.LogInfo("That one is an inn warp.");
```

Nearly every warp in the game belongs to one generic family whose name is empty, so an empty result means "an
ordinary warp" rather than a failed lookup. The families that are named are the inn warps, the two housing doors,
the rental-chocobo desks, the wedding desk, and the story portal networks.

`ScanArrayHandlerWarps` exposes the indirection on its own for a caller that needs it: some event objects do not
name a warp directly but run an array handler that does.

---

## Chocobo Taxis

```csharp
var stands = ChocoboTaxiHelper.ReadStands();
var rides = ChocoboTaxiHelper.CollectRides(stands);
var porters = ChocoboTaxiHelper.ScanPorters(stands);

foreach (var ride in rides)
    NoireLogger.LogInfo($"{ChocoboTaxiHelper.StandName(ride.FromStandId)} -> {ride.DestinationName}: {ride.Fare} gil, {ride.TimeSeconds}s");
```

A stand's target list is a fixed-width set of slots, and the unused ones sit in the sheet as placeholder rides.
Every ride a porter really offers takes at least a minute, so a zero duration is the sheet's own mark of an unused
slot and `ReadStands` drops it. `TimeSeconds` is already converted; the sheet states minutes.

A stand has no position of its own; it is wherever its porter stands, resolved via `ScanPorters` plus
`EventNpcHelper.FindPlacements`. A stand served by more than one porter resolves to the lowest-numbered of them,
so the answer never depends on the order the sheet was walked in.

---

## Housing

Nothing about housing is listed by hand, so a district, an interior, or an interior design added by a later patch
is picked up on its own.

```csharp
var interiors = HousingHelper.ReadInteriors();     // every interior territory and what kind it is
var plots = HousingHelper.ReadPlots(districtTerritoryId);
var doors = HousingHelper.FindInteriorDoors(interiorTerritoryId, placedObjects);

HousingHelper.TryGetPlotPosition(districtTerritoryId, plotIndex, out var plotPosition);
HousingHelper.TryGetApartmentPosition(districtTerritoryId, subdivision: false, out var apartmentPosition);

var address = HousingHelper.ReadOwnedAddress(EstateKind.PrivateEstate);
if (address.Owned)
    NoireLogger.LogInfo(HousingHelper.FormatAddress(address, districtTerritoryId));
```

**Which house is this?** Every plot of a size opens into one and the same interior territory, so the territory can
never say which estate the character is standing in. The game's indoor state can, and it is the only thing that can:

```csharp
var inside = HousingHelper.ReadCurrentIndoorHouse();
if (inside.Owned)
    NoireLogger.LogInfo($"Plot {inside.Plot + 1}, ward {inside.Ward + 1} of district {inside.District}.");
```

It names the district, ward and plot, and for an apartment the division and room, so the way back out of a shared
interior can be narrowed to the one door that is really this house's.

**What the character owns** reads the same from anywhere in the world, with no district loaded:

```csharp
var company  = HousingHelper.ReadOwnedAddress(EstateKind.FreeCompanyEstate);
var chambers = HousingHelper.ReadOwnedChambers();   // rented separately: a company estate does not imply chambers
var room     = HousingHelper.ReadOwnedAddress(EstateKind.Apartment);
```

There is **no list of open apartment rooms or Free Company chambers anywhere in the client**, from inside the ward
or out; the game states the character's own and nothing else. Anything offering a room to walk into is limited to
those, and to the lobby, which anyone may enter.

- **What an interior is** comes from `HousingIndoorTerritory`. It is the only thing that separates an apartment
  from the private chambers it shares a level file with, and the only thing that says which of a district's three
  estate territories is the small, medium, or large one.
- **Which district an interior belongs to** comes from the level-file region they share
  (`LevelFileHelper.ResolveRegionRoot`), which pairs them without naming either.
- **Where a plot or an apartment entrance stands** comes from `HousingMapMarkerInfo`, which carries a real
  three-dimensional point per marker, height included. Every ward of a district lays its markers out identically,
  so one position per index serves any ward.
- **Which two doors an interior has** comes from its layout: every housing interior is one room whose doorway out
  sits at the far positive-Z end and whose doorway further in, when it has one, sits at the far negative-Z end.
- **Two placed objects are the same door** when they run the same event handler: this pairs a district with its
  apartment building, and tells an apartment's exit from the private chambers' exit in a shared level file.
- **An interior the sheets leave unnamed** is named from the kind of place it is plus the interior design it is
  decorated in, both read from the game's own strings, so "Territory 1375" reads as
  "Private House (Dark Minimalist Style)".
- **An interior design belongs to no district**, so `InteriorsOf` lists what a district really holds and
  `ResolveInterior` maps a design onto the district's interior of the same size. A character standing in a design is
  standing in that room under different decor, and the district comes from the indoor house above.

`ClassifyEstate` takes a Dalamud teleport-list entry (`IAetheryteEntry`) and `IsOwnedHouse` takes the game's own
`HouseId`; both have a loose-field overload behind them for testing the rule without a game.

---

## Shops

`ShopHelper` answers what a vendor sells and what it charges. Every price, gil included, is expressed the same way:
as a quantity of an item.

```csharp
var shop = ShopHelper.ReadShop(262100);        // kind, name, and every line it sells

foreach (var offer in shop!.Offers)
    NoireLogger.LogInfo($"{offer.ItemId} x{offer.Quantity} for {offer.Costs[0].Amount} of {offer.Costs[0].ItemId}");
```

`ShopCost.IsGil` and `ShopOffer.IsGilPurchase` test against gil's item id (**1**). An offer is a gil purchase only
when gil is the *whole* price: a special shop charging gil alongside a token is not something a gil purchase can
pay for, and reporting it as one would send a caller to a vendor it cannot buy from.

### Who sells this

`ScanCatalog` walks every gil and special shop once and indexes them both ways. It is cached, since the sheets cannot
change while the client runs:

```csharp
var catalog = ShopHelper.ScanCatalog();

catalog.ShopsSelling(itemId);        // every shop with it on the shelf
catalog.OffersFor(itemId);           // every line selling it, across every shop
catalog.CheapestGilPrice(itemId);    // the lowest gil price, and who charges it
```

### From a shop to the NPC standing behind it

**A gil shop's and a special shop's row id is also the event handler id an NPC runs.** "Route me to the nearest
vendor selling X" becomes a plain composition, with no NPC name matched:

```csharp
var shops = new HashSet<uint>(ShopHelper.FindShopsSelling(itemId));
var scan = EventNpcHelper.ScanHandlers(shops);

foreach (var (shopId, npcIds) in scan.NpcsByHandler)
    NoireLogger.LogInfo($"Shop {shopId} is run by {npcIds.Count} NPC(s).");

// ... and then where each of them stands
var positions = EventNpcHelper.FindPositions(levelObjects, new HashSet<uint>(scan.NpcsByHandler[shopId]));
```

### Shops behind a menu

Not every vendor runs its shop handler directly. An NPC that opens a menu first runs the **menu's** handler, so its
shops are invisible to a handler scan until the menu is unfolded:

```csharp
ShopHelper.ReadTopicSelectShops();   // TopicSelect row -> the shops behind it
ShopHelper.ReadInclusionShops();     // InclusionShop row -> the special shops behind it (the scrip exchanges)
```

Grand company quartermasters are the one shop kind not addressed by a shop row at all: their stock is assembled from
every category belonging to the company rather than held as one row, so they are read by company instead, and priced
in that company's own seals.

```csharp
ShopHelper.ReadGrandCompanyOffers(GrandCompany.Maelstrom);   // the client's own GrandCompany enum
ShopHelper.SealItemId(GrandCompany.Maelstrom);               // the seal item those prices are in
```

---

## Duties

`DutyHelper` reads the duty finder's own description of a duty. A duty is addressed everywhere by its
**`ContentFinderCondition` row id**, so that is the id every method takes:

```csharp
var duty = DutyHelper.Read(dutyId);

duty!.Name;                  // "Sastasha"
duty.TerritoryId;            // where it takes place
duty.LevelRequired;          // and what it takes to get in
duty.ItemLevelSync;          // 0 when it does not sync
duty.PartySize;              // how many it queues for
duty.RouletteIds;            // the roulettes that can draw it
```

A roulette is a **`ContentRoulette` row id**, not a name written down in the library. `ContentFinderCondition` opens
with one boolean column per roulette in that sheet's row order, so membership is read positionally and a roulette
added in a later patch appears on its own:

```csharp
duty.IsInRoulette(1);                    // ContentRoulette row 1 is Leveling
DutyHelper.ReadRoulettes();              // every roulette the game defines, named in the client language
DutyHelper.RouletteName(9);
DutyHelper.InRoulette(9);                // every duty Mentor can draw
DutyHelper.InTerritory(territoryId);     // and the reverse lookup
```

### What the character has done

```csharp
DutyHelper.Current();                    // the duty the character is inside, or 0
DutyHelper.IsInDuty();
DutyHelper.IsUnlocked(dutyId);
DutyHelper.IsCompleted(dutyId);
DutyHelper.ReadProgress(dutyIds);        // both sets at once, for a known set
```

Unlock and completion are recorded for **instanced content only**. A duty whose content is defined in another sheet
answers false rather than reading an unrelated row id as though it were an instance. Check
`DutyInfo.IsInstanceContent` first to tell "not cleared" from "not knowable".

---

## Classes and Jobs

`ClassJobHelper` covers identity, roles and levels:

```csharp
var job = ClassJobHelper.Read(19);

job!.Abbreviation;     // "PLD" - localised, so this is a label, never a key
job.NameEnglish;       // "Paladin" - the same on every client, so this is what to match on
job.Role;              // ClassJobRole.Tank
job.ParentId;          // the class it advances from, or its own id when it advances from nothing
```

The game keeps **three separate numberings** and a row sits in at most one, so each has its own test and none of
them is "is this a job":

```csharp
job.IsBattleJob;       // has a place in the battle job numbering (paladin, machinist, ...)
job.IsBattleClass;     // has a place in the class numbering and none in the job one (gladiator, ...)
job.IsHandOrLand;      // has an index among the crafters and gatherers
```

A crafter's `JobIndex` is zero because it is outside the *battle job* numbering, not because it is not a job.

Disciplines come from the sheet, so they are picked from a list rather than by a row id you have to go and find:

```csharp
ClassJobHelper.ReadDisciplines();          // (categoryId, "Disciple of the Land"), discovered from the sheet
ClassJobHelper.InDiscipline(categoryId);   // every class and job in it

ClassJobHelper.BattleJobs();
ClassJobHelper.BattleClasses();
ClassJobHelper.HandAndLand();
ClassJobHelper.Find("pld");                     // by abbreviation or by either name
ClassJobHelper.InRole(ClassJobRole.Healer);     // every healer
```

Levels come from the loaded character and are stored **per class**, so asking for a job answers the level of the
class it grew out of - which is the same number the game itself shows:

```csharp
ClassJobHelper.CurrentId();          // what the character is playing
ClassJobHelper.Level(19);            // their level in it
ClassJobHelper.Level(19, synced: true);
ClassJobHelper.AllLevels(minimumLevel: 50);   // every job at 50 or above
ClassJobHelper.HighestLevel();
```

### Category membership

Every "this job may equip it" and "this job may queue for it" restriction in the game is a `ClassJobCategory`:

```csharp
ClassJobHelper.CategoryIncludes(categoryId, classJobId);
ClassJobHelper.CategoryMembers(categoryId);      // every job the category holds
ClassJobHelper.CategoryName(categoryId);         // the text shown on an item's restriction
```

The category sheet holds one boolean column per class and job **in `ClassJob` row order**, read **positionally**
rather than by name: a job added later arrives as a new sheet row and column together, picked up with no code
change. Reading by name would need the names written down here, and would break twice over, since a column is
named after an abbreviation and an abbreviation is localised.

---

## Icons

`IconHelper` turns an icon id into something drawable. The path is resolved by the game's own texture lookup rather
than assembled here, so the high resolution and language variants fall back exactly the way the client's do:

```csharp
IconHelper.Path(iconId);                    // the game path, or null when there is no such icon
IconHelper.Exists(iconId);
IconHelper.Get(iconId);                     // the shared texture - hold this, not a wrap
IconHelper.Wrap(iconId);                    // ready to hand to a draw call; do not dispose it
```

An icon id is almost never what a caller holds. They hold an item, an action or a duty, and the icon is a column on
it:

```csharp
IconHelper.ForItem(itemId);
IconHelper.ForAction(actionId);
IconHelper.ForStatus(statusId);
IconHelper.ForDuty(dutyId);
IconHelper.ForEmote(emoteId);
IconHelper.ForMapSymbol(symbolId);
```

---

## Text Commands

`TextCommandHelper` reads the client's own command list. **A command typed as `/dance` is only `/dance` on an
English client**: the same row reads `/danse` in French and `/tanz` in German, so writing the English string by
hand works for exactly one audience.

```csharp
// Name the command in English, send back what the client actually accepts.
var command = TextCommandHelper.Localize("/dance");
```

`Find` matches every spelling the client accepts - full, abbreviated, and both aliases - and takes a whole line as
typed, so the leading slash and any arguments are optional:

```csharp
TextCommandHelper.Find("/dance motion");     // the same row as "dance"
TextCommandHelper.Normalize("  /DANCE x ");  // "dance" - the pure rule, testable without a game
TextCommandHelper.ReadAll();                 // every command the client knows
```

---

## Worlds and Travel

`WorldHelper` reads the world tree and where the character sits in it:

```csharp
WorldHelper.ReadAll();                       // the public worlds; pass false for the internal ones too
WorldHelper.Read(worldId);
WorldHelper.Find("Ragnarok");                // by display or internal name
WorldHelper.WorldsOn(dataCenterId);
WorldHelper.DataCenterName(dataCenterId);

WorldHelper.CurrentId();                     // where the character is standing
WorldHelper.HomeId();                        // where they belong
WorldHelper.IsVisiting();                    // standing somewhere else
WorldHelper.IsTravelling();                  // and on another data centre, which is a different journey
```

`ShareDataCenter` is the test that separates the two: a world visit and a data centre travel are reached by
different routes and cost different things.

---

## Live Character and World State

Everything above reads static content. These read the client:

```csharp
LayoutHelper.LoadedTerritory();              // the territory the client has loaded, or 0
LayoutHelper.IsInstancePlaced(levelObject);  // true / false / null when it cannot be answered

QuestHelper.IsComplete(questId);
QuestHelper.IsAccepted(questId);
QuestHelper.Sequence(questId);               // how far an accepted quest has got
QuestHelper.ReadProgress(questIds);          // all three at once, for a set of quests

WorldHelper.ReadDataCenters();               // world -> data centre (cached)
WorldHelper.ReadActiveFestivals();           // the seasonal events running right now

DutyHelper.Current();                        // the duty the character is inside, or 0
DutyHelper.ReadProgress(dutyIds);            // what they have unlocked and cleared

ClassJobHelper.CurrentId();                  // what they are playing
ClassJobHelper.AllLevels();                  // and their level in everything
```

`LayoutHelper.IsInstancePlaced` returns **null** rather than false when the answer is unknown - a different
territory is loaded, or the layout is not ready - so "not there" is never confused with "cannot say".

### Reading character data safely

Anything that touches the logged-in character - the teleport list, the housing address, the quest journal, the
loaded layout - is gated on `CharacterHelper.IsStateReady`, and so should any code you write beside it.

**Being logged in is not that point.** The login event fires while the client is still assembling the character,
and reading through it goes through pointers the game has not filled in yet: that is an access violation, not an
exception, and no `try` will catch it.

---

## Rules That Hold Everywhere

- **A level file says what could stand in a territory, never what does.** `LayoutHelper.IsInstancePlaced` is the
  only answer, and only for the loaded territory.
- **Scan by function, never by name.** A warp NPC, a porter, a door and a vendor are found by the event handler they
  run, which picks exactly the ones that do the thing and works in every client language. A gil or special shop's
  row id *is* that handler id, so a shop and the NPC standing behind it are the same lookup.
- **Read it, never restate it.** If the client or a sheet holds a value, read it from there rather than writing it
  down: the clock's current reading comes from `Framework.ClientTime`, a shop's kind from the handler content in its
  own row id, a roulette's name from `ContentRoulette`, a job's discipline from the category the sheet points at. The
  same goes for types: where the game defines an enum, that enum is used (`EventHandlerContent`, `GrandCompany`,
  `ContentType`, `LayerEntryType`) rather than a parallel one. What is left is only what has no source at all, and it
  is marked as such where it is declared: the Eorzean clock's **rate** (the client holds the reading, never the
  speed) and gil's item id.
- **Prefer a positional read over a written-down list.** Where a sheet lays out one column per row of another sheet,
  walk the columns by position. `ClassJobCategory` and the roulette block of `ContentFinderCondition` both work this
  way, so a job or a roulette added in a patch is picked up with no code change at all.
- **Never key on localised text.** A job abbreviation, a place name and a text command all change with the client's
  language, so a dictionary keyed on one works on exactly one client. Key on the row id, or on the column the game
  keeps unlocalised for the purpose (`ClassJobInfo.NameEnglish`, `DutyInfo.ShortCode`). Where user-typed text has to
  be matched, match it through a helper that considers every spelling (`ClassJobHelper.Find`,
  `TextCommandHelper.Find`) rather than by comparing one string.
- **Share one scan.** The `ENpcBase` sheet is large, so one pass can serve several consumers:

  ```csharp
  var stands = ChocoboTaxiHelper.ReadStands();
  var handlerIds = new HashSet<uint>(ChocoboTaxiHelper.CollectStandIds(stands));
  handlerIds.UnionWith(WarpHelper.ReadWarpIds());

  var scan = EventNpcHelper.ScanHandlers(handlerIds);   // one pass
  var warps = WarpHelper.ScanEventNpcWarps(scan);
  var porters = ChocoboTaxiHelper.ScanPorters(stands, scan);
  ```

  Passing the handler ids keeps the scan small; passing the scan keeps it to one.
- **Store row ids, resolve text at display time.** Text is resolved from ids at the point it is shown, never
  frozen when data is read, so one dataset extracted on an English client reads correctly on a French one. Where a
  label has to be stored, store the **row id** (a `PlaceName` row, a `Warp` row) and resolve it later.
- **Every read is guarded.** A missing sheet or an unreadable file is an empty result, not a throw.
- **A row-shaped method takes the row.** `IsEstateHall(Aetheryte)`, `ClassifyEstate(IAetheryteEntry)`,
  `MarkerToWorld(marker, map)`, `IsOwnedHouse(HouseId)`. The loose-parameter overloads exist only as the pure
  rule, for a caller that already holds the fields and for testing without a game.

---

## Troubleshooting

### A read comes back empty

- Check the client is logged in and the character is loaded: anything reading character state answers empty rather
  than reading through a half-built character (`CharacterHelper.IsStateReady`).
- Check the territory has a level path at all. `TerritoryHelper.Bg` is empty for placeholder rows, and
  `LevelFileHelper.ResolveLevelDirectory` returns null for them.
- Check the filter. `LevelObjectFilter` defaults to dropping `LevelObjectKind.Other`, which is most of a file; use
  `LevelObjectFilter.Everything` when you really want all of it.
- Check `/xllog`: every guarded read logs what it caught rather than failing silently.

### A whole-world read is slow or exhausts memory

- Filter during the read rather than after it, with `Kinds` and the base-id sets.
- Read one territory at a time, and canonicalize first with `TerritoryHelper.BuildAliases` so nineteen variants of
  one zone are read once rather than nineteen times.

### A position is wrong or has no height

- A marker or map coordinate carries no height at all. Take altitude from a placed object's own transform.
- A territory can span several maps with different offsets. Project through each map's own `MapProjection`, which
  is what `ProjectMarkers` does, rather than through the first map row.

### An object is in the data but not in the world

- Check `LevelObject.BelongsTo` first. Several territories share one level directory, and an object whose layer
  belongs only to the others is not in this territory at all, whatever the character has done.
- Otherwise that is the expected case, not a bug: a level file lists every placement the game could switch on. Ask
  `LayoutHelper.IsInstancePlaced`, and treat its **null** as "cannot say" rather than as "no".

### A name reads in the wrong language

- Something stored the text instead of the id. Store the `PlaceName` (or `Warp`) row id and resolve it at display
  time; every name method here reads through `ExcelSheetHelper` in the client's current language.

### A command does nothing on a non-English client

- The command was written out rather than resolved. Send `TextCommandHelper.Localize("/dance")`, not `"/dance"`.

### An NPC clearly sells the item but no scan finds it

- The NPC opens a menu before selling, so it runs the menu's handler rather than the shop's. Unfold it with
  `ShopHelper.ReadTopicSelectShops` or `ShopHelper.ReadInclusionShops` and scan for the menu handler instead.
- Or it is a grand company quartermaster, which is not addressed by a shop row at all. Read it with
  `ShopHelper.ReadGrandCompanyOffers`.

### A duty reads as not cleared when it has been

- Ask `DutyInfo.IsInstanceContent` first. Unlock and completion are recorded for instanced content only, and
  everything else answers false because the answer is not knowable, not because it is no.

If the behaviour still looks wrong after all of that, please report it.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [NoireUI Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/UI/README.md) - `NoireExcelPicker` puts a sheet in front of a user
- [AddonHelper](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Helpers/AddonHelper/README.md) - reading the game's own UI rather than its data
