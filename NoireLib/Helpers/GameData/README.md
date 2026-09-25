# Helper Documentation : Game Data Helpers

You are reading the documentation for the `Helpers/GameData` helpers.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [The Helpers](#the-helpers)
- [Shared Models](#shared-models)
- [Archive Paths](#archive-paths)
- [Game File Formats](#game-file-formats)
- [Level Files](#level-files)
- [World Objects](#world-objects)
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
- [Walk-in Content](#walk-in-content)
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

Static helpers in the `NoireLib.Helpers` namespace, one per subject, answering what the game's **files and sheets** say about the world:

- **Level (`.lgb`) file reading** into a flat, Lumina-free object model, plus the raw layer reader under it
- **Game file formats**: models (`.mdl`), materials (`.mtrl`), level (`.lgb`) and scene (`.sgb`) files parsed from the bytes
- **Coordinate conversion** between world positions, map-marker pixels and in-game map coordinates
- **Territory facts**: names, level paths, the real/duty/mountable sets, zone-crossing quest gates
- **The aetheryte network**: crystal identity and position, residential shards, attunement, fares
- **Warps and chocobo taxis**: what teleports the character, what it costs, what unlocks it
- **Residential housing**: districts, interiors, plots, apartments, doors, naming, the character's own address
- **Shops**: what every vendor sells, what it charges, and which NPC stands behind it
- **Duties**: what the duty finder lists, what it requires, and what the character has unlocked or cleared
- **Walk-in content**: the current Diadem season, the Cosmic Exploration planets, and the services that open them
- **Classes and jobs**: names, roles, category membership, and the character's level in each
- **Icons and text commands**: an icon id resolved to a drawable texture, a command resolved into the client's language
- **Worlds and live state**: worlds and data centres, whether a placement is standing there, quest progress, active festivals

Everything is `public` in `NoireLib.Helpers`. Every sheet and file read is wrapped in `SafeExecutor`. A missing sheet or an unreadable file yields an empty result.

> **A level file states what *could* stand in a territory.** Its layers belong to layer sets the game switches on and off for quest progress, instance, phase and season. Only `LayoutHelper` can say which are on, and only for the loaded territory.

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

Nothing to construct or register:

```csharp
using NoireLib.Helpers;

var aetherytes = AetheryteHelper.ApplyLevelPositions(AetheryteHelper.ReadAll());
var name = TerritoryHelper.Name(territoryId);

if (HousingHelper.TryResolvePlot(ResidentialDistrict.Mist, ward: 12, plot: 23, out var location))
    NoireLogger.LogInfo($"Its placard stands at {location.Placard}.");
```

---

## The Helpers

| Helper | Answers |
|---|---|
| `LevelFileHelper` | Reads a territory's placed objects out of its `.lgb` level files. |
| `LayerGroupHelper` | Reads any `.lgb` or `.sgb` into its layers and every entry they place, and expands nested shared groups with composed transforms. |
| `GameModelFile` / `GameMaterialFile` | Parse a model or a material straight from the archives, with `GameShaderNames` naming a material's samplers and constants. |
| `MapCoordinateHelper` | Converts between world positions, map-marker pixels and in-game map coordinates; reads map rows and markers. |
| `EorzeaTimeHelper` | The Eorzean clock, day and night, and the eight-hour windows the weather is decided in. |
| `WeatherHelper` | What the weather is and what it will be, forecast from the moment alone. |
| `TerritoryHelper` | Territory names, level paths, the real/duty/mountable/flight-capable sets, zone-crossing quest gates, aetheryte bindings, handler quests, and the canonical-territory rule. |
| `AetheryteHelper` | Aetheryte and shard identity, their world positions, residential aethernet crystals, and what the character has attuned. |
| `EventNpcHelper` | Which NPCs run which event handlers, and where each of them stands. |
| `WorldObjectHelper` | Event objects, NPCs, aetherytes and shared groups from any mix of id, name, script, handler or SGB path, with their sheet details, handlers and every placement. |
| `WarpHelper` | The interactables that teleport the character, what they cost, and what unlocks them. |
| `ChocoboTaxiHelper` | The porter network: stands, rides, fares, durations, and which NPC runs each stand. |
| `HousingHelper` | Residential districts, interiors and their kinds, plot and apartment positions, interior doors, interior naming, and the character's own address. |
| `ShopHelper` | What every shop sells and charges, indexed both ways, plus the menus that hide shops from a handler scan. |
| `VendorHelper` | Which NPC sells an item, the menu entries leading to the shop, and the shop window it opens in. |
| `DutyHelper` | What the duty finder says about a duty, and what the character has unlocked or cleared. |
| `DiademHelper` | The current Diadem season and what entering it requires. |
| `CosmicExplorationHelper` | The Cosmic Exploration planets, their aethernet, their bound warps, and the travel services. |
| `ClassJobHelper` | Class and job identity, roles, `ClassJobCategory` membership, and the character's level in each. |
| `IconHelper` | An icon id resolved to its game path or its texture, and the icon a sheet row names. |
| `TextCommandHelper` | The client's own text commands, and the rewrite that makes one work in any language. |
| `LayerSetHelper` | Which territory a level layer belongs to, when several territories share one level directory. |
| `LayoutHelper` | Whether a placement is actually standing in the world right now. |
| `QuestHelper` | Whether quests are complete or accepted, and how far each accepted one has got. |
| `WorldHelper` | Worlds, data centres, where the character is against where they live, and which seasonal events are running. |
| `OrnamentHelper` | Which fashion accessory a character has out, and what carrying it does to the emotes they can play. |
| `UldHelper` | Part lists out of the game's ULD files, resolved to textures and UVs. |
| `ExcelSheetHelper` | Any Excel sheet in any client language, lazily loaded and cached. Every helper here reads through it. |
| `StainHelper` | The game's dyes: their names, their colors, and which are metallic or housing-applicable. |
| `DyeHelper` | The dye nearest a color given as a `Vector3`, a `Vector4`, a HEX string or a packed value, the item that applies a dye, and whether that item applies it alone. |
| `GameVersionHelper` | The installed client's build, for stamping cached game data. |
| `GameClientHelper` | Which game client is installer, told apart by the language variants its data files ship. |

---

## Shared Models

`LevelObject` (with `LevelObjectKind` and `LevelObjectFilter`) is the placed object every file read produces. `MapProjection` / `MapMarkerEntry` / `ProjectedMapMarker`, `TerritoryEntry`, `AetheryteEntry`, `ResidentialShard`, `WarpDefinition` / `WarpLogicInfo` / `WarpLogicParam`, `ChocoboTaxiStandInfo` / `ChocoboTaxiRide`, `EventNpcHandlerScan`, `WorldObject` / `WorldObjectPlacement` / `WorldObjectHandler` / `WorldObjectQuery` / `PlacementScope`, `QuestProgress`, `ShopCost` / `ShopOffer` / `ShopInfo` / `ShopCatalog`, `DutyInfo`, `DiademEntry`, `CosmicPlanet` / `CosmicShardInfo` / `CosmicTravelTalks`, `ClassJobInfo`, `TextCommandInfo`, `WorldInfo` and the `Housing*` records are documented on the helper that produces them.

**None of them references Lumina.** The raw file readers are the exception: `LayerGroupEntry` uses Lumina's layer enums, and `GameModelFile` / `GameMaterialFile` are Lumina `FileResource`s.

Records live under `Models/`, enums under `Enums/`, helpers at the folder root, all in `NoireLib.Helpers`.

---

## Archive Paths

`GamePathHelper` answers "where would that file be" as pure string rules. Nothing here opens an archive.

```csharp
// A background model stores its material path outright. A character model stores it relative,
// beginning with a slash, and resolves it next to the model's own folder.
string? mtrl = GamePathHelper.ResolveMaterialPath(
    "chara/equipment/e0001/model/c0101e0001_top.mdl", "/mt_c0101e0001_top_a.mtrl", variant: 1);
// chara/equipment/e0001/material/v0001/mt_c0101e0001_top_a.mtrl

// A character material's file name encodes its owner. An equipment model naming its wearer's
// skin resolves into the human directory. Several candidates come back. Take the first that loads.
IReadOnlyList<string> candidates = GamePathHelper.ResolveMaterialByOwnerName("/mt_c0201b0001_a.mtrl");

// The DirectX 11 texture sits beside the named one with a doubled dash on the file name.
string dx11 = GamePathHelper.Dx11TexturePath("chara/.../texture/v01_c0101e0001_top_d.tex");

// Furniture pairs .../bgparts/x.mdl with .../asset/x.sgb.
string? scene = GamePathHelper.SceneBesideModel("bgcommon/hou/indoor/general/0681/bgparts/fun_b0_m0681.mdl");
```

---

## Game File Formats

The parsers read the bytes. Lumina only fetches the file.

```csharp
var model = NoireService.DataManager.GetFile<GameModelFile>("bgcommon/hou/indoor/general/0001/bgparts/fun_b0_m0001.mdl");
var lod = model!.Lods[0];
var mesh = model.Meshes[lod.MeshIndex];
var position = model.Declarations[lod.MeshIndex].First(e => e.Usage == GameVertexUsage.Position);
Vector4 first = model.ReadVertexElement(lod, mesh, position, vertex: 0);
ushort[] triangles = model.ReadIndices(lod, mesh);   // the game's counter-clockwise winding

var material = NoireService.DataManager.GetFile<GameMaterialFile>("bgcommon/hou/indoor/general/0001/material/fun_b0_m0001_1a.mtrl");
GameMaterialTexture? diffuse = material!.TextureFor("g_SamplerDiffuse");
```

---

## Level Files

```csharp
var objects = LevelFileHelper.ReadPlacements(territoryId);

// One file only, keeping the requested kinds.
var crystals = LevelFileHelper.ReadObjects(territoryId, LevelFileHelper.Files.PlanMap, new LevelObjectFilter
{
    Kinds = new HashSet<LevelObjectKind> { LevelObjectKind.Aetheryte },
});

var exits = LevelFileHelper.OfKind(objects, LevelObjectKind.ExitRange);
var doors = LevelFileHelper.InLayer(objects, "roomexit");
LevelFileHelper.TryGetNearest(doors, somewhere, out var door);
```

Every object carries the `Layer` it was read out of. `InLayer` matches a fragment, case-insensitive: five districts spell the same layer five ways.

`LevelFileHelper.Files` names the five files by role (`PlanMap`, `PlanEvent`, `Planner`, `PlanLive`, `Background`). `Files.Interactable` is the four `ReadPlacements` merges. `PlanLive` lays out the places the game switches on and off, such as a Grand Company barracks or a story tower's floors, and holds arrival volumes found in no other file. `ResolveLevelDirectory` and `ResolveRegionRoot` are the pure path rules.

An `ExitRange` carries `ExitKind`. A `LevelExitKind.ZoneLine` names the territory it leads to. A `LevelExitKind.IntraZoneTeleport` names none: its `DestInstanceId` resolves in the territory it stands in.

**Filter during the read.** A whole-world pass that keeps everything exhausts the heap. `LevelObjectFilter` drops an object before it is added:

- `Kinds` restricts what is kept at all.
- `EventNpcBaseIds` / `EventObjectBaseIds` keep only the named interactables.
- `IncludeUnmappedKinds` (or `LevelObjectFilter.Everything`) keeps the rest, most of a file.

The default drops `LevelObjectKind.Other`.

**Reads are sequential.** Lumina serializes file access internally. The helper yields between territories.

**Arrival points are indexed once.** A warp lands the character in a `PopRange` volume. `BuildPopRangeIndex` turns a whole-world read into a `(territory, instance) -> position` lookup:

```csharp
var arrivals = LevelFileHelper.BuildPopRangeIndex(objectsByTerritory);
if (arrivals.TryGetValue((destinationTerritory, warp.ArrivalInstanceId), out var landing))
    NoireLogger.LogInfo($"That warp lands at {landing}.");
```

### Reading any level or scene file

`LayerGroupHelper` is the reader under `LevelFileHelper`: any `.lgb` or `.sgb`, every layer with its layer sets and festival, every entry with its transform and type-specific fields (model and collision paths, collision volume shape and material, door state, sheet row, exit destination, PopRange spawn offsets, and the shape and `Priority` of a `MapRange` or water range).

| `LayerGroupEntry` | Of | Holds |
|---|---|---|
| `Priority` | `MapRange`, water range | which of overlapping ranges decides, highest first |
| `WaterRangeFlags` | water range | `0x1` swimmable water, `0x100` an air pocket, `0x0` dry |
| `FlyingDisabled` | `MapRange` | flight is refused inside it |
| `MountsAndOrnamentsDisabled` | `MapRange` | mounts and ornaments are refused inside it |
| `LalafellOnly` | `MapRange` | only Lalafell may enter |

Lumina names no entry type for a water range; `LayerGroupHelper.WaterRangeEntryType` is its type, 86.

```csharp
foreach (var layer in LayerGroupHelper.Read("bg/ffxiv/sea_s1/twn/s1t1/level/bg.lgb"))
    NoireLogger.LogInfo($"{layer.Name}: {layer.Entries.Count} entries");

// Nested shared groups expanded, each entry's World composed with every group above it.
var placed = LayerGroupHelper.Flatten("bg/ffxiv/sea_s1/twn/s1t1/level/bg.lgb", layer => layer.FestivalId == 0);   // seasonal layers left out
foreach (var entry in placed)
{
    if (entry.Type == LayerEntryType.BG)
        NoireLogger.LogInfo($"{entry.AssetPath} at {entry.World.Translation}");
}
```

`LayerGroupHelper.Compose` is the placement matrix (scale, then X, Y, Z rotation, then translation), the same one the navmesh uses. The filter applies to the file's own layers. Nested groups are read whole, and a group placing itself is listed once.

### Which territory a placement belongs to

Several `TerritoryType` rows share one level directory.

Each layer lists its **layer sets**, and the `.lvb` file beside the level files maps each set to a `TerritoryType` row:

```csharp
foreach (var set in LayerSetHelper.ReadLayerSets(territoryId))
    NoireLogger.LogInfo($"Layer set {set.LayerSetId} belongs to territory {set.TerritoryId}.");
```

Every object read carries the answer:

```csharp
foreach (var placed in LevelFileHelper.ReadPlacements(territoryId))
{
    if (!placed.BelongsTo(territoryId))
        continue;   // stands in another row of this place
}
```

`LevelObject.LayerTerritories` is empty for an unconditional layer and for a set the level's table does not describe. `BelongsTo` is then true.

Both structures are read from the bytes. Lumina's `Layer.LayerSetReferences` resolves against the wrong base and returns unrelated numbers.

---

## Vendor Paths

`VendorHelper` unfolds an NPC's menus into the entries a player picks on the way to a shop:

```csharp
foreach (var path in VendorHelper.FindPurchasePaths(itemId))
    NoireLogger.LogInfo($"{path.NpcName}: {string.Join(" > ", path.MenuSteps.Select(step => step.Label))} ({path.Window})");
// Iron Thunder: Purchase Disciple of War Gear > Purchase Gear (Lv. 1-9) (Shop)
```

| Handler | Effect |
|---|---|
| `TopicSelect` | A submenu, each child is an entry to choose |
| `PreHandler` | A quest check, its entry reads as the shop behind it |
| `CustomTalk` | A script, its shops come from its arguments and `CustomTalkNestHandlers`, the path is `IsScripted` |
| `Array` | Each child stands in the array's place |

**Entries are chosen by label.** The game hides some entries and skips a single-entry menu. `CanBuyWithGilShop` is true for a gil shop line reached through named menus. `RequiredQuestIds` gathers the quests of the shop, the menus and the line. `MayShowDialogue` flags a talk shown before the shop. `ScanPurchasePaths` indexes every NPC once and is cached.

---

## World Objects

`WorldObjectHelper` answers what a thing is and where it stands. One query, one list of `WorldObject` models:

```csharp
// Every market board, in any client language. The CustomTalk script they all run.
var boards = WorldObjectHelper.Find(WorldObjectQuery.ByScript("CmnDefMarketBoard"));

// Only the ones in Limsa Lominsa. The zone name covers both decks.
var limsa = WorldObjectHelper.Find(WorldObjectQuery.ByScript("CmnDefMarketBoard") with { Scope = PlacementScope.Place("Limsa Lominsa") });

var npc = WorldObjectHelper.Find(WorldObjectQuery.ByName("Mylla") with { Kinds = PlacementKinds.EventNpc, IncludePlacements = false });
var vendors = await WorldObjectHelper.FindAsync(WorldObjectQuery.ByHandlerContent(EventHandlerContent.SpecialShop));

// A thing with no row of its own, found by its SGB asset path.
var shards = WorldObjectHelper.Find(WorldObjectQuery.ByAssetPath(AetheryteHelper.ResidentialCrystalAssetPrefix) with { Scope = PlacementScope.Place("The Goblet") });

// The loaded target, its placements in this territory nearest first.
var target = WorldObjectHelper.DescribeLoaded(NoireService.TargetManager.Target!);

bool isBoard = WorldObjectHelper.RunsScript(targetBaseId, "CmnDefMarketBoard");
```

A row comes back only when it meets every criterion of the `WorldObjectQuery`.

| Criterion | Reads |
|---|---|
| `BaseIds` | `EObj`, `ENpcBase`, `Aetheryte` rows that exist |
| `Name`, `NameMatch`, `Language` | `EObjName`, `ENpcResident`, and the `PlaceName` of `Aetheryte`, case insensitive |
| `ScriptPrefix` | `CustomTalk` names by prefix, then the rows running them |
| `HandlerIds`, `HandlerContent` | `EObj.Data` and `ENpcBase.ENpcData`, array handlers unfolded |
| `AssetPathFragment` | Shared group SGB paths, one `WorldObject` per path |

A `WorldObject` carries the names, the NPC title, the handlers (family, CustomTalk script name, shop name and offer count, warp definition), one of `EventObjectDetails`, `EventNpcDetails` or `AetheryteDetails`, and its `WorldObjectPlacement` list. `IsPlacedNow()` asks `LayoutHelper` for the loaded territory.

**One base id stands in many places, and one name covers many base ids.** Market board 2000402 stands in Limsa Lominsa Lower Decks, Old Gridania, Idyllshire, Kugane and Tuliyollal. An NPC often has one `ENpcBase` row per place. Narrow with the scope.

**Prefer a script name.** `CustomTalk` names are script identifiers and never localised.

`PlacementScope` is `Territory`, `Territories`, `Place` (a place, zone or region name through `TerritoryHelper.FindByPlaceName`) or `World`, the default. A zone and its quest battle copies fold onto one territory.

**World-wide reads go through an index.** The first lookup wider than eight territories reads every placement across the five level files, `bg.lgb` included, and caches it under the plugin config directory through `VersionedJsonCache`, keyed on the game build. `PrepareIndexAsync` builds or loads it ahead of time. `ResetIndex` drops it.

---

## Coordinates

A map spot has three names, related through the map's size factor and offset:

- a **world** position, carried by a game object;
- a **marker** pixel in the 0-2048 space of the map image, stored by the `MapMarker` sheet;
- a **map coordinate**, the "X: 12.3, Y: 9.8" of a flag or a chat link.

**None of them carries a height.** Altitude comes from a placed object's transform.

```csharp
// A territory can span several maps with different offsets. Project through each map's own projection.
var markers = MapCoordinateHelper.ProjectMarkers(territoryId, MapMarkerDataType.AethernetShard);

if (MapCoordinateHelper.TryFindNearestMarker(markers, crystalPosition, out var nearest))
    var ward = TerritoryHelper.PlaceName(nearest.Marker.DataKey);

// The flag coordinate for a world position, and back again.
foreach (var map in MapCoordinateHelper.ReadMaps(territoryId))
{
    var (x, y) = MapCoordinateHelper.WorldToMapCoordinate(worldPosition, map);
    var back = MapCoordinateHelper.MapCoordinateToWorld(x, y, map);
}

// One call when the map does not matter, only the numbers the game shows.
MapCoordinateHelper.TryWorldToMapCoordinate(territoryId, worldPosition, out var coordinate, out var mapId);

// The same thing written the way a report names a place: "12.345, 6.000, -7.890 (map 9.4, 10.1)".
var place = MapCoordinateHelper.DescribePlace(territoryId, worldPosition);
```

A territory drawn across several maps, such as a housing ward and its subdivision, has a different offset per map. `PickMap` chooses the one a position lands inside. `TryWorldToMapCoordinate` and `DescribePlace` go through it.

`MapMarkerDataType` names the marker kinds worth filtering on (`Map`, `InstanceEntrance`, `Aetheryte`, `AethernetShard`). Each conversion also has a loose-float overload.

---

## The Eorzean Clock

Eorzea time is **computed**, a pure function of real time at 1440/70 speed. Every method works for any moment.

```csharp
EorzeaTimeHelper.Hour;                       // 0 to 23, right now
EorzeaTimeHelper.IsNight;                    // night runs 18:00 to 05:59
EorzeaTimeHelper.HourAt(someMoment);

// Weather is decided in eight-hour windows, first-class here.
var start = EorzeaTimeHelper.WeatherWindowStart(DateTimeOffset.UtcNow);
var next = EorzeaTimeHelper.NextWeatherWindow(DateTimeOffset.UtcNow);
var upcoming = EorzeaTimeHelper.WeatherWindows(DateTimeOffset.UtcNow, 12);
```

A window starts at Eorzean 00:00, 08:00 or 16:00 and lasts 1400 real seconds.

`ToEorzea` returns a `DateTimeOffset` whose **time of day** is the Eorzean one. Its date means nothing.

`FixedTimeOfDay(territoryId)` reports the zones that freeze the clock.

---

## Weather

**Weather is computed.** The game rolls a number from the moment alone, the same on every client and world, and looks it up in the territory's rate table.

```csharp
var now = WeatherHelper.Current(territoryId);                 // computed for this moment
var actual = WeatherHelper.Active();                          // what the client is really showing

foreach (var window in WeatherHelper.Forecast(territoryId, windows: 12))
    NoireLogger.LogInfo($"{window.Start:t}: {WeatherHelper.Name(window.WeatherId)}");
```

`Active()` needs a running game and is the ground truth. It can differ from `Current` where a duty, a cutscene or a quest phase sets the weather.

### Waiting for weather

```csharp
var fog = WeatherHelper.FindNext(territoryId, new HashSet<uint> { 5, 6 });

// A transition, like most timed conditions.
var afterFog = WeatherHelper.FindNextTransition(territoryId,
    (previous, current) => previous == 6 && current == 1);
```

`FindNextTransition` takes both windows, for conditions like "clear skies, after fog".

### Running without a game

Only the rate table comes from the sheets. `Forecast` and `FindNextTransition` also take the table directly:

```csharp
var rates = WeatherHelper.ReadRates(territoryId);             // [(weatherId, rate), ...]
var forecast = WeatherHelper.Forecast(rates, territoryId, windows: 100);
```

`Resolve(rates, chance)` and `ChanceAt(moment)` are the two pure rules underneath.

---

## Territories

```csharp
TerritoryHelper.Name(territoryId);          // display name, falling back to a housing interior's name
TerritoryHelper.SheetPlaceName(territoryId);// the sheet's own PlaceName only, with no fallback
TerritoryHelper.PlaceNameId(territoryId);   // the row id, the form to store
TerritoryHelper.Bg(territoryId);            // the level path, e.g. ffxiv/fst_f1/fld/f1f1/level/f1f1

TerritoryHelper.AetheryteOf(territoryId);   // the aetheryte the territory is bound to (a district's city crystal)
TerritoryHelper.ReadHandlerQuests(territoryId); // the quests the territory's own event handler names (a district's unlock)

TerritoryHelper.ReadReal();                 // territories that are a real place, not a placeholder row
TerritoryHelper.ReadQueueableDuties();      // territories reachable through the Duty Finder
TerritoryHelper.ReadMountable();            // territories that allow a mount, not flight (cached)
TerritoryHelper.ReadFlightCapable();        // territories the sheets prove flight in, being the current zones (cached)
TerritoryHelper.ReadTeleportBarred();       // territories Teleport cannot be cast from (cached)
TerritoryHelper.ReadAetherCurrentZones();   // (territory, CompFlgSet) pairs for flight unlocks (cached)
TerritoryHelper.ReadZoneCrossingGates();    // the quest gates that close a zone boundary
```

### The canonical territory

Many `TerritoryType` rows share one level file: the open-world zone plus its duty, quest-battle and PvP versions. Central Shroud has nineteen.

```csharp
var aliases = TerritoryHelper.BuildAliases(preferred: TerritoryHelper.ReadReal());
var canonical = TerritoryHelper.ResolveAlias(aliases, territoryId);
```

A row is a variant only when it also carries the same **PlaceName row id**. An apartment and the private chambers share one file and are different places.

---

## Aetherytes and Aethernet Shards

Identity comes from the sheet, position from the crystals placed in the level files:

```csharp
var aetherytes = AetheryteHelper.ApplyLevelPositions(AetheryteHelper.ReadAll());

AetheryteHelper.Name(aetheryteId);          // the crystal's name in the client's language
AetheryteHelper.ReadUnlocked();             // what the logged-in character has attuned to
AetheryteHelper.ReadTeleportFares();        // the gil fare per destination, from the teleport list
```

An `ArrivalOnly` entry, such as an airship landing, has no crystal.

### Estate halls

An estate hall is an Aetheryte row that is not a crystal and belongs to no aethernet group:

```csharp
if (ExcelSheetHelper.TryGetRow<Aetheryte>(aetheryteId, out var row) && row is { } aetheryte
    && AetheryteHelper.IsEstateHall(aetheryte))
{
    var (_, placeNameId, placeName) = AetheryteHelper.ReadEstateHall(aetheryteId);
}
```

### Residential shards

A residential aethernet shard has no Aetheryte row. It is a crystal placed in the district's level file, matched by its shared-group asset path (`AetheryteHelper.ResidentialCrystalAssetPrefix`) and labelled from the nearest map marker.

```csharp
var shards = AetheryteHelper.ReadResidentialShards(districtTerritoryId);
foreach (var shard in shards)
    NoireLogger.LogInfo($"{TerritoryHelper.PlaceName(shard.PlaceNameId)} at {shard.Position}");
```

Pass an already-read `LevelObject` list as the second argument to skip reading the district again.

---

## Warps

A warp is an interactable that teleports the character to a set landing spot. Warps are found by the **event handler** they run, in every client language.

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

A `WarpDefinition` carries the cost (`GilCost`), the requirements (`ClassLevel`, `RequiredQuests` with a `QuestThreshold`) and the landing (`DestTerritoryId` plus the `ArrivalInstanceId` that resolves through `LevelFileHelper.BuildPopRangeIndex`).

**`QuestThreshold` equal to the quest count is "all of". Below it, it is M-of-N**, for an unlock reachable by several storylines.

### The logic row is a script, and sometimes the only gate

`LogicId` is the warp's `WarpLogic` row. `WarpHelper.LogicName` gives its internal, never localised name:

```csharp
if (WarpHelper.LogicName(warp.LogicId).StartsWith("WarpInn"))
    NoireLogger.LogInfo("That one is an inn warp.");
```

543 of the game's 577 warps use the two generic rows, whose name is empty. The 15 named rows are the six inn warps, the two housing doors, the rental-chocobo desks, the wedding desk, the Elpis and Ultima Thule portal networks, and a system row that only changes the music.

**The name is a file**: the stem of a compiled Lua script at `game_script/warp/<name>.luab`. The row's parameters are that script's gating constants. Read the row with `ReadLogic`:

```csharp
if (WarpHelper.ReadLogic(warp.LogicId) is { } logic)
{
    foreach (var (function, argument) in logic.Params)
        NoireLogger.LogInfo($"{logic.ScriptName} takes {function} = {argument}");
}
```

**Four rows carry parameters, and for three warps they are the only gate.** Warps 131176 (the wedding desk), 131316 (the Crystarium inn) and 131576 (the Tuliyollal inn) name no quest on their `WarpCondition`. Ask `NamesContentGate`:

```csharp
if (WarpHelper.NamesContentGate(warp))
    NoireLogger.LogInfo("This warp is gated by something the condition does not state.");
```

It reads the argument's name. `QST_` and `QUEST_` name a quest, `ITEM_` an item, `QST_SEQ_` is a sequence number, and `HOWTO_` is a tutorial popup that gates nothing. The rental-chocobo desks' real gate is 80 gil and level 10.

**It says a gate exists, not whether it is passed.** How the script combines its arguments is in the bytecode.

### Three wirings, not two

An event object reaches its warp three ways. `ScanEventObjectWarps` covers all of them:

1. **Directly**: the object's handler id is a `Warp` row.
2. **Through an array handler**: a list of handler ids, one of them a warp. Many doors and teleporters work this way. `ScanArrayHandlerWarps` exposes it alone.
3. **Through the `WKSWarp` table**: four rows, each an EObj named `elevator` paired with a Warp row, the Oizys floors in Cosmic Exploration. `ScanCosmicWarps` exposes them. **The columns are unnamed in the schema and read positionally.** A test pins the pairing.

---

## Chocobo Taxis

```csharp
var stands = ChocoboTaxiHelper.ReadStands();
var rides = ChocoboTaxiHelper.CollectRides(stands);
var porters = ChocoboTaxiHelper.ScanPorters(stands);

foreach (var ride in rides)
    NoireLogger.LogInfo($"{ChocoboTaxiHelper.StandName(ride.FromStandId)} -> {ride.DestinationName}: {ride.Fare} gil, {ride.TimeSeconds}s");
```

Unused slots in a stand's target list are placeholder rides with a zero duration. `ReadStands` drops them. `TimeSeconds` is converted from the sheet's minutes.

A stand is wherever its porter stands, resolved through `ScanPorters` and `EventNpcHelper.FindPlacements`. A stand with several porters resolves to the lowest-numbered one.

---

## Housing

Nothing about housing is listed by hand.

```csharp
var interiors = HousingHelper.ReadInteriors();     // every interior territory and what kind it is
var plots = HousingHelper.ReadPlots(districtTerritoryId);
var doors = HousingHelper.FindInteriorDoors(interiorTerritoryId, placedObjects);

HousingHelper.TryGetPlotPosition(districtTerritoryId, plotIndex, out var anchor);
HousingHelper.TryGetApartmentPosition(districtTerritoryId, subdivision: false, out var apartmentAnchor);

var address = HousingHelper.ReadOwnedAddress(EstateKind.PrivateEstate);
if (address.Owned)
    NoireLogger.LogInfo(HousingHelper.FormatAddress(address, districtTerritoryId));
```

### Where a plot's placard and doors stand

Any plot of any ward of any district, from anywhere. Ward and plot are the displayed numbers. Plot 31 is the first of the subdivision.

```csharp
if (HousingHelper.TryResolvePlot(ResidentialDistrict.Shirogane, ward: 30, plot: 60, out var location))
{
    location.Placard;      // the plot's placard, null for an apartment building
    location.Entrance;     // the spot in front of the door, where an arriving character is placed
    location.ExitLanding;  // where the estate puts a character stepping out of it
    location.Anchor;       // the map anchor, at neither door
    location.Kind;         // Cottage, House or Mansion
}

HousingHelper.TryResolveApartment(ResidentialDistrict.Mist, ward: 3, subdivision: true, out var lobby);
HousingHelper.TryResolve(HousingHelper.ReadOwnedAddress(EstateKind.PrivateEstate), out var owned);

var everything = HousingHelper.ReadPlotLocations(ResidentialDistrict.Empyreum);   // sixty plots, both buildings
```

Every ward of a district is laid out identically. **The ward completes the address and changes no position.**

`ResidentialDistrict` is named constants over territory ids. Every method also takes a raw `uint` territory.

- **The placard** is the land-set row's `PlacardId`, a level-file instance id. Its `EObj` row is checked against `HousingHelper.PlacardBaseId`.
- **The two doors** are the nearest `PopRange` to the anchor in the `frontpop` and `roomexit` layers. Layer names are matched on a fragment: one district spells its placard layer `signborad`. Each district holds sixty-two entrance volumes and sixty estate exits.

---

**Which house is this?** Every plot of a size opens into the same interior territory. Only the game's indoor state says which estate:

```csharp
var inside = HousingHelper.ReadCurrentIndoorHouse();
if (inside.Owned)
    NoireLogger.LogInfo($"Plot {inside.Plot + 1}, ward {inside.Ward + 1} of district {inside.District}.");
```

It names the district, ward and plot, and for an apartment the division and room.

**What the character owns** reads from anywhere:

```csharp
var company  = HousingHelper.ReadOwnedAddress(EstateKind.FreeCompanyEstate);
var chambers = HousingHelper.ReadOwnedChambers();   // rented separately: a company estate does not imply chambers
var room     = HousingHelper.ReadOwnedAddress(EstateKind.Apartment);
```

**The client holds no list of open apartment rooms or Free Company chambers.** Only the character's own, and the lobby.

- **What an interior is** comes from `HousingIndoorTerritory`: apartment or private chambers, and which estate territory is small, medium or large.
- **Which district an interior belongs to** comes from their shared level-file region (`LevelFileHelper.ResolveRegionRoot`).
- **A plot's map anchor** comes from `HousingMapMarkerInfo`, a 3D point per marker. The anchor is not a door: it sits six to twelve yalms above the ground and fourteen to thirty out from the placard. `TryResolvePlot` gives the doors.
- **An interior's two doors**: the way out at the far positive-Z end, the way further in at the far negative-Z end.
- **Two placed objects are the same door** when they run the same event handler.
- **An unnamed interior** is named from its kind plus its interior design: "Territory 1375" reads as "Private House (Dark Minimalist Style)".
- **An interior design belongs to no district.** `InteriorsOf` lists what a district holds. `ResolveInterior` maps a design onto the district's interior of the same size.

`ClassifyEstate` takes a Dalamud teleport-list entry (`IAetheryteEntry`) and `IsOwnedHouse` the game's `HouseId`. Both have a loose-field overload.

---

## Shops

`ShopHelper` answers what a vendor sells and what it charges. Every price, gil included, is a quantity of an item.

```csharp
var shop = ShopHelper.ReadShop(262100);        // kind, name, and every line it sells

foreach (var offer in shop!.Offers)
    NoireLogger.LogInfo($"{offer.ItemId} x{offer.Quantity} for {offer.Costs[0].Amount} of {offer.Costs[0].ItemId}");
```

`ShopCost.IsGil` and `ShopOffer.IsGilPurchase` test against gil's item id (**1**). An offer is a gil purchase only when gil is the *whole* price.

### Who sells this

`ScanCatalog` walks every gil and special shop once, indexes them both ways, and is cached:

```csharp
var catalog = ShopHelper.ScanCatalog();

catalog.ShopsSelling(itemId);        // every shop with it on the shelf
catalog.OffersFor(itemId);           // every line selling it, across every shop
catalog.CheapestGilPrice(itemId);    // the lowest gil price, and who charges it
```

### From a shop to the NPC standing behind it

**A gil or special shop's row id is the event handler id an NPC runs:**

```csharp
var shops = new HashSet<uint>(ShopHelper.FindShopsSelling(itemId));
var scan = EventNpcHelper.ScanHandlers(shops);

foreach (var (shopId, npcIds) in scan.NpcsByHandler)
    NoireLogger.LogInfo($"Shop {shopId} is run by {npcIds.Count} NPC(s).");

// ... and then where each of them stands
var positions = EventNpcHelper.FindPositions(levelObjects, new HashSet<uint>(scan.NpcsByHandler[shopId]));
```

### Shops behind a menu

An NPC that opens a menu first runs the **menu's** handler. Unfold the menu to find its shops:

```csharp
ShopHelper.ReadTopicSelectShops();   // TopicSelect row -> the shops behind it
ShopHelper.ReadInclusionShops();     // InclusionShop row -> the special shops behind it (the scrip exchanges)
```

Grand company quartermasters have no shop row. Their stock comes from every category of the company, priced in its seals.

```csharp
ShopHelper.ReadGrandCompanyOffers(GrandCompany.Maelstrom);   // the client's own GrandCompany enum
ShopHelper.SealItemId(GrandCompany.Maelstrom);               // the seal item those prices are in
```

---

## Duties

`DutyHelper` reads the duty finder's description of a duty, addressed by its **`ContentFinderCondition` row id**:

```csharp
var duty = DutyHelper.Read(dutyId);

duty!.Name;                  // "Sastasha"
duty.TerritoryId;            // where it takes place
duty.LevelRequired;          // and what it takes to get in
duty.ItemLevelSync;          // 0 when it does not sync
duty.PartySize;              // how many it queues for
duty.RouletteIds;            // the roulettes that can draw it
```

A roulette is a **`ContentRoulette` row id**. `ContentFinderCondition` opens with one boolean column per roulette, read positionally:

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

Unlock and completion are recorded for **instanced content only**. Check `DutyInfo.IsInstanceContent` first.

---

## Walk-in Content

Some content is entered by talking to an NPC. The NPC runs a `CustomTalk` service, found by its never-localised script name. The NPC is found by `EventNpcHelper.ScanHandlers`.

`DiademHelper` covers the Diadem. Its seasons are the Diadem-typed `PublicContent` rows, the highest being the one Aurvael opens:

```csharp
var entry = DiademHelper.ReadCurrentEntry();
entry!.TerritoryId;                  // the current season's territory
entry.JobCategoryId;                 // Disciple of the Land...
entry.JobLevel;                      // ...at this level

DiademHelper.ReadEntranceTalkIds();  // Aurvael's service, for the NPC scan
```

`CosmicHelper` covers Cosmic Exploration, from the `WKS` sheets:

```csharp
CosmicHelper.ReadPlanets();          // every planet, in release order, from WKSTerritoryInfo
CosmicHelper.ReadAethernetShards();  // each WKSAetheryte with its name and placed objects
CosmicHelper.ScanWarpObjects();      // the Warp row each WKSWarp-bound object triggers
CosmicHelper.ReadTravelTalks();      // the boarding and leave services, for the NPC scan
```

A shard's planet comes from where its object is placed. `ReadAethernetShards` carries no territory. The `WKSWarp` objects run a `CustomTalk` (the Oizys rooftop elevators). `WarpHelper.ScanEventObjectWarps` cannot see them.

`EventNpcHelper.Name(npcBaseId)` resolves an NPC's display name.

---

## Classes and Jobs

`ClassJobHelper` covers identity, roles and levels:

```csharp
var job = ClassJobHelper.Read(19);

job!.Abbreviation;     // "PLD", localised: a label, never a key
job.NameEnglish;       // "Paladin", the same on every client: match on this
job.Role;              // ClassJobRole.Tank
job.ParentId;          // the class it advances from, or its own id when it advances from nothing
```

The game keeps **three separate numberings**, and a row sits in at most one:

```csharp
job.IsBattleJob;       // has a place in the battle job numbering (paladin, machinist, ...)
job.IsBattleClass;     // has a place in the class numbering and none in the job one (gladiator, ...)
job.IsHandOrLand;      // has an index among the crafters and gatherers
```

A crafter's `JobIndex` is zero: it is outside the *battle job* numbering.

Disciplines come from the sheet:

```csharp
ClassJobHelper.ReadDisciplines();          // (categoryId, "Disciple of the Land"), discovered from the sheet
ClassJobHelper.InDiscipline(categoryId);   // every class and job in it

ClassJobHelper.BattleJobs();
ClassJobHelper.BattleClasses();
ClassJobHelper.HandAndLand();
ClassJobHelper.Find("pld");                     // by abbreviation or by either name
ClassJobHelper.InRole(ClassJobRole.Healer);     // every healer
```

Levels come from the loaded character and are stored **per class**. A job answers the level of its class, like the game shows:

```csharp
ClassJobHelper.CurrentId();          // what the character is playing
ClassJobHelper.Level(19);            // their level in it
ClassJobHelper.Level(19, synced: true);
ClassJobHelper.AllLevels(minimumLevel: 50);   // every job at 50 or above
ClassJobHelper.HighestLevel();
```

### Category membership

Every "this job may equip it" and "this job may queue for it" restriction is a `ClassJobCategory`:

```csharp
ClassJobHelper.CategoryIncludes(categoryId, classJobId);
ClassJobHelper.CategoryMembers(categoryId);      // every job the category holds
ClassJobHelper.CategoryName(categoryId);         // the text shown on an item's restriction
```

The category sheet holds one boolean column per class and job **in `ClassJob` row order**, read **positionally**. Column names are localised abbreviations.

---

## Icons

`IconHelper` turns an icon id into something drawable, through the game's own texture lookup, with the same high resolution and language fallbacks as the client:

```csharp
IconHelper.Path(iconId);                    // the game path, or null when there is no such icon
IconHelper.Exists(iconId);
IconHelper.Get(iconId);                     // the shared texture, cached per lookup: safe every frame
IconHelper.Wrap(iconId);                    // ready to hand to a draw call; do not dispose it

// The accent color the icon reads as, read off the framework thread on first request. Null until then.
Vector4 accent = IconHelper.GetVividColor(iconId) ?? fallback;
```

`GetVividColor` runs `ColorHelper.GetVividColor` on the icon's pixels: a tile average weighted toward opaque, bright,
saturated regions, then brightened and pushed away from grey. An icon with no opaque, bright region stays null.

The icon is usually a column on an item, an action or a duty:

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

`TextCommandHelper` reads the client's own command list. **`/dance` is `/danse` in French and `/tanz` in German.**

```csharp
var command = TextCommandHelper.Localize("/dance");
```

`Find` matches every spelling (full, abbreviated, both aliases) and takes a whole line as typed. The leading slash and arguments are optional:

```csharp
TextCommandHelper.Find("/dance motion");     // the same row as "dance"
TextCommandHelper.Normalize("  /DANCE x ");  // "dance", the pure rule, testable without a game
TextCommandHelper.ReadAll();                 // every command the client knows
```

---

## Worlds and Travel

`WorldHelper` reads the world tree and the character's place in it:

```csharp
WorldHelper.ReadAll();                       // the public worlds; pass false for the internal ones too
WorldHelper.Read(worldId);
WorldHelper.Find("Ragnarok");                // by display or internal name
WorldHelper.WorldsOn(dataCenterId);
WorldHelper.DataCenterName(dataCenterId);

WorldHelper.CurrentId();                     // where the character is standing
WorldHelper.HomeId();                        // where they belong
WorldHelper.IsVisiting();                    // standing somewhere else
WorldHelper.IsTravelling();                  // and on another data centre
```

`ShareDataCenter` separates a world visit from a data centre travel.

---

## Live Character and World State

These read the client:

```csharp
LayoutHelper.LoadedTerritory();              // the territory the client has loaded, or 0
LayoutHelper.LoadedLayerSet();               // the layer-set key it is filtering layers by, or null
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

`LayoutHelper.IsInstancePlaced` returns **null** when the answer is unknown: another territory is loaded or the layout is not ready.

`LayoutHelper.LoadedLayerSet` is null for the same reason. **Zero is a real key**, the base configuration. The key selects the zone's *edition*: a region or censorship variant, a patch revision, and in a few raid tiers the encounter. It does **not** select a duty's phase. That is per-instance and `IsInstancePlaced` answers it.

### Reading character data safely

Anything touching the logged-in character (the teleport list, the housing address, the quest journal, the loaded layout) is gated on `CharacterHelper.IsStateReady`.

**Being logged in is not enough.** The login event fires while the client is still assembling the character. Reading through it is an access violation no `try` catches.

**Calling into game code needs `CharacterHelper.IsPlayerLoaded`.** The player object is filled a moment after `IsStateReady` turns true and destroyed a moment before it turns false. `Telepo.UpdateAetheryteList` prices every entry from the player's position and crashes the client in that window.

| Gate on | For |
|---|---|
| `CharacterHelper.IsStateReady` | reading a struct or a field the character owns |
| `CharacterHelper.IsPlayerLoaded` | **calling a game function** that may reach for the player object |

`AetheryteHelper.RefreshTeleportList()` carries that gate and returns whether the game was asked.

**The teleport list outlives a character switch.** `RefreshTeleportList` records which character asked. `ReadUnlockedState` reports `Known` only when the current character refreshed it.

---

## Rules That Hold Everywhere

- **A level file says what could stand in a territory.** `LayoutHelper.IsInstancePlaced` answers for the loaded territory.
- **Scan by function.** A warp NPC, a porter, a door and a vendor are found by the event handler they run, in every client language. A gil or special shop's row id *is* that handler id.
- **Read it, never restate it.** Values held by the client or a sheet are read from there: the clock from `Framework.ClientTime`, a shop's kind from its row id, a roulette's name from `ContentRoulette`, a job's discipline from its category. Game enums are used as is (`EventHandlerContent`, `GrandCompany`, `ContentType`, `LayerEntryType`). The only constants are the Eorzean clock's **rate** and gil's item id.
- **Prefer positional reads.** `ClassJobCategory` and the roulette block of `ContentFinderCondition` are walked by column position.
- **Never key on localised text.** Key on the row id or on an unlocalised column (`ClassJobInfo.NameEnglish`, `DutyInfo.ShortCode`). Match user text through `ClassJobHelper.Find` or `TextCommandHelper.Find`.
- **Share one scan.** One pass over `ENpcBase` can serve several consumers:

  ```csharp
  var stands = ChocoboTaxiHelper.ReadStands();
  var handlerIds = new HashSet<uint>(ChocoboTaxiHelper.CollectStandIds(stands));
  handlerIds.UnionWith(WarpHelper.ReadWarpIds());

  var scan = EventNpcHelper.ScanHandlers(handlerIds);   // one pass
  var warps = WarpHelper.ScanEventNpcWarps(scan);
  var porters = ChocoboTaxiHelper.ScanPorters(stands, scan);
  ```

  Passing the handler ids keeps the scan small.
- **Store row ids, resolve text at display time.** Store a `PlaceName` or `Warp` row id, never its text.
- **Every read is guarded.** A missing sheet or an unreadable file is an empty result.
- **A row-shaped method takes the row.** `IsEstateHall(Aetheryte)`, `ClassifyEstate(IAetheryteEntry)`, `MarkerToWorld(marker, map)`, `IsOwnedHouse(HouseId)`. The loose-parameter overloads are the pure rule.

---

## Troubleshooting

### A read comes back empty

- Check the character is loaded. Character reads answer empty until `CharacterHelper.IsStateReady`.
- Check the territory has a level path. `TerritoryHelper.Bg` is empty for placeholder rows.
- Check the filter. `LevelObjectFilter` drops `LevelObjectKind.Other` by default. Use `LevelObjectFilter.Everything` to keep all.
- Check `/xllog`. Every guarded read logs what it caught.

### A whole-world read is slow or exhausts memory

- Filter during the read with `Kinds` and the base-id sets.
- Read one territory at a time. Canonicalize with `TerritoryHelper.BuildAliases` first.

### A position is wrong or has no height

- Markers and map coordinates carry no height. Take altitude from a placed object's transform.
- A territory can span several maps. Project through each map's own `MapProjection`, like `ProjectMarkers` does.

### An object is in the data but not in the world

- Check `LevelObject.BelongsTo`. The object's layer may belong only to other territories sharing the directory.
- Otherwise it is expected. Ask `LayoutHelper.IsInstancePlaced`, and read **null** as "cannot say".

### A name reads in the wrong language

- Something stored the text. Store the `PlaceName` or `Warp` row id and resolve it at display time.

### A command does nothing on a non-English client

- Send `TextCommandHelper.Localize("/dance")`, not `"/dance"`.

### An NPC clearly sells the item but no scan finds it

- The NPC opens a menu first and runs the menu's handler. Unfold it with `ShopHelper.ReadTopicSelectShops` or `ShopHelper.ReadInclusionShops`.
- Or it is a grand company quartermaster. Read it with `ShopHelper.ReadGrandCompanyOffers`.

### A duty reads as not cleared when it has been

- Check `DutyInfo.IsInstanceContent`. Only instanced content records unlock and completion.

If the behaviour still looks wrong after all of that, please report it.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [NoireUI Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/UI/README.md) - `NoireExcelPicker` puts a sheet in front of a user
- [AddonHelper](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Helpers/AddonHelper/README.md) - reads the game's own UI, not its data
