using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Layer;
using Lumina.Data.Parsing.Layer;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// One entry of a layer: the common header every type carries, plus the type-specific fields
/// <see cref="LayerGroupHelper"/> reads. A field that does not apply to <see cref="Type"/> is zero or empty.
/// </summary>
public sealed record LayerGroupEntry
{
    /// <summary>What the entry is.</summary>
    public required LayerEntryType Type { get; init; }

    /// <summary>The instance id, unique within its file and the key the game's live layout uses.</summary>
    public required uint InstanceId { get; init; }

    /// <summary>The entry's name, often empty.</summary>
    public required string Name { get; init; }

    /// <summary>The stored translation.</summary>
    public required Vector3 Translation { get; init; }

    /// <summary>The stored rotation, as Euler angles in radians.</summary>
    public required Vector3 Rotation { get; init; }

    /// <summary>The stored scale, a half-extent for the trigger and collision volumes.</summary>
    public required Vector3 Scale { get; init; }

    /// <summary>
    /// The matrix placing the entry, composed by <see cref="LayerGroupHelper.Compose"/>. Local to its file when
    /// read, composed with every enclosing shared group when returned by <see cref="LayerGroupHelper.Flatten(string, System.Func{LayerGroupLayer, bool}, int)"/>.
    /// </summary>
    public required Matrix4x4 World { get; init; }

    /// <summary>The file the entry names: the .mdl of a BG, the .sgb of a SharedGroup, the .pcb of a mesh CollisionBox.</summary>
    public string AssetPath { get; init; } = string.Empty;

    /// <summary>The collision mesh (<c>.pcb</c>) of a <see cref="LayerEntryType.BG"/>, empty when it names none.</summary>
    public string CollisionPath { get; init; } = string.Empty;

    /// <summary>How a <see cref="LayerEntryType.BG"/> collides: from its mesh, as its model's bounding box, or not at all.</summary>
    public ModelCollisionType CollisionType { get; init; }

    /// <summary>The collision material a <see cref="LayerEntryType.BG"/> or <see cref="LayerEntryType.CollisionBox"/> writes.</summary>
    public uint Attribute { get; init; }

    /// <summary>Which material bits <see cref="Attribute"/> replaces, zero leaving the mesh's own material.</summary>
    public uint AttributeMask { get; init; }

    /// <summary>Whether a <see cref="LayerEntryType.BG"/> is drawn. An invisible one is collision only.</summary>
    public bool IsVisible { get; init; }

    /// <summary>
    /// The volume shape of a <see cref="LayerEntryType.CollisionBox"/>, <see cref="LayerEntryType.ExitRange"/>,
    /// <see cref="LayerEntryType.MapRange"/> or <see cref="LayerGroupHelper.WaterRangeEntryType"/>.
    /// </summary>
    public TriggerBoxShape Shape { get; init; }

    /// <summary>Which of the overlapping <see cref="LayerEntryType.MapRange"/> or <see cref="LayerGroupHelper.WaterRangeEntryType"/> entries decides, highest first.</summary>
    public short Priority { get; init; }

    /// <summary>A <see cref="LayerGroupHelper.WaterRangeEntryType"/>'s flags: <c>0x1</c> swimmable water, <c>0x100</c> an air pocket, <c>0x0</c> dry.</summary>
    public uint WaterRangeFlags { get; init; }

    /// <summary>Whether a <see cref="LayerEntryType.MapRange"/> forbids flight inside it.</summary>
    public bool FlyingDisabled { get; init; }

    /// <summary>Whether a <see cref="LayerEntryType.MapRange"/> forbids mounts and ornaments inside it.</summary>
    public bool MountsAndOrnamentsDisabled { get; init; }

    /// <summary>Whether a <see cref="LayerEntryType.MapRange"/> admits only Lalafell.</summary>
    public bool LalafellOnly { get; init; }

    /// <summary>How the door a <see cref="LayerEntryType.SharedGroup"/> places starts, zero when the file states none.</summary>
    public DoorState InitialDoorState { get; init; }

    /// <summary>The level designers' NotCreateNavimeshDoor flag on a <see cref="LayerEntryType.SharedGroup"/>.</summary>
    public bool NotCreateNavimeshDoor { get; init; }

    /// <summary>The sheet row of an <see cref="LayerEntryType.Aetheryte"/>, <see cref="LayerEntryType.EventNPC"/> or <see cref="LayerEntryType.EventObject"/>.</summary>
    public uint BaseId { get; init; }

    /// <summary>What an <see cref="LayerEntryType.ExitRange"/> does, zero for any other type.</summary>
    public ExitRangeType ExitType { get; init; }

    /// <summary>The territory an <see cref="LayerEntryType.ExitRange"/> leads to, zero when it stays in its own.</summary>
    public ushort DestTerritoryId { get; init; }

    /// <summary>The PopRange instance an <see cref="LayerEntryType.ExitRange"/> lands the character on.</summary>
    public uint DestInstanceId { get; init; }

    /// <summary>The PopRange instance an <see cref="LayerEntryType.ExitRange"/> returns the character to.</summary>
    public uint ReturnInstanceId { get; init; }

    /// <summary>The spawn points of a <see cref="LayerEntryType.PopRange"/>, as world offsets from <see cref="Translation"/>.</summary>
    public IReadOnlyList<Vector3> SpawnOffsets { get; init; } = [];
}
