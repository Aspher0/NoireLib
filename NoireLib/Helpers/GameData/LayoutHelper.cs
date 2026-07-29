using FFXIVClientStructs.FFXIV.Client.LayoutEngine;

namespace NoireLib.Helpers;

/// <summary>
/// Answers whether a placed object is actually standing in the world right now, from the game's resolved layout
/// rather than the level file.<br/>
/// A level file lists everything that could exist; layer sets toggle per quest, instance, phase and season, so it
/// never says what is actually placed for this character. The resolved layout (the active <c>LayoutWorld</c>,
/// keyed by the level file's own instance id) is ground truth, and is read directly rather than through its own
/// lookup call, since a struct read cannot fault the way a call into game code can.
/// </summary>
public static unsafe class LayoutHelper
{
    // The layout reports this once fully built; below it, instance maps are incomplete, so a miss reads as
    // "not placed" when it is really "not placed yet".
    private const int LoadedLayout = 7;

    /// <summary>Whether an object a level file places is standing in the loaded layout right now.</summary>
    /// <param name="levelObject">The placed object, read out of a level file by <see cref="LevelFileHelper"/>.</param>
    /// <returns>
    /// True when it is placed and active, false when the layout was read and does not hold it, and null when the
    /// question cannot be answered: no character, no layout, a layout still being built, or a kind the layout does not
    /// index separately.
    /// </returns>
    public static bool? IsInstancePlaced(LevelObject levelObject)
    {
        var type = levelObject.Kind switch
        {
            LevelObjectKind.EventNpc => InstanceType.EventNpc,
            LevelObjectKind.EventObject => InstanceType.EventObject,
            LevelObjectKind.Aetheryte => InstanceType.Aetheryte,
            LevelObjectKind.SharedGroup => InstanceType.SharedGroup,
            LevelObjectKind.ExitRange => InstanceType.ExitRange,
            LevelObjectKind.PopRange => InstanceType.PopRange,
            _ => (InstanceType?)null,
        };

        return type.HasValue ? IsInstancePlaced(type.Value, levelObject.InstanceId) : null;
    }

    /// <summary>Whether a placed instance exists and is active in the loaded layout.</summary>
    /// <param name="type">The instance type, which is the kind of thing the level file placed.</param>
    /// <param name="instanceId">The level file's instance id for the placement.</param>
    /// <returns>
    /// True when the layout holds it and reports it active, false when the layout holds it and reports it inactive,
    /// and null when the question cannot be answered: no character, no layout, a layout still being built, a type the
    /// layout does not index, or an instance id it holds no entry for. Only a false is the game stating absence;
    /// treat null as "ask something else", never as "not there".
    /// </returns>
    public static bool? IsInstancePlaced(InstanceType type, uint instanceId)
    {
        if (instanceId == 0 || !CharacterHelper.IsStateReady)
            return null;

        var layout = ActiveLayout();
        if (layout == null)
            return null;

        if (!layout->InstancesByType.TryGetValuePointer(type, out var byId) || byId == null || byId->Value == null)
            return null;

        // A key the layout does not hold is UNANSWERABLE, not absent. The layout indexes the instances it actually
        // built, so a miss means "this map has nothing to say about that placement", which is a different claim from
        // the game having decided the object is not there. Reading a miss as "not placed" made every warp trigger the
        // layout had not indexed read as missing, which hid genuinely usable warps in the one territory the gate is
        // allowed to answer for: the one the character is standing in.
        if (!byId->Value->TryGetValuePointer(instanceId, out var instance) || instance == null || instance->Value == null)
            return null;

        return instance->Value->IsActive;
    }

    /// <summary>
    /// The raw outcome of a placement lookup, for working out why an answer came back the way it did. Use it when
    /// diagnosing a placement gate; <see cref="IsInstancePlaced(InstanceType, uint)"/> is the answer to act on.
    /// </summary>
    public enum LayoutLookup
    {
        /// <summary>No character, or no fully built layout to ask.</summary>
        NoLayout,

        /// <summary>The layout holds no index for that instance type at all.</summary>
        TypeNotIndexed,

        /// <summary>The type is indexed but holds no entry for that instance id.</summary>
        KeyMissing,

        /// <summary>The instance is indexed and the layout reports it inactive.</summary>
        Inactive,

        /// <summary>The instance is indexed and active.</summary>
        Active,
    }

    /// <summary>
    /// Reports which case a placement lookup actually hit, rather than folding all of them into one answer. Two very
    /// different situations both read as "cannot say" through <see cref="IsInstancePlaced(InstanceType, uint)"/>, and
    /// telling them apart is the whole question when a placement gate is hiding something it should not.
    /// </summary>
    /// <param name="type">The instance type.</param>
    /// <param name="instanceId">The level file's instance id for the placement.</param>
    /// <returns>The lookup outcome.</returns>
    public static LayoutLookup Describe(InstanceType type, uint instanceId)
    {
        if (instanceId == 0 || !CharacterHelper.IsStateReady)
            return LayoutLookup.NoLayout;

        var layout = ActiveLayout();
        if (layout == null)
            return LayoutLookup.NoLayout;

        if (!layout->InstancesByType.TryGetValuePointer(type, out var byId) || byId == null || byId->Value == null)
            return LayoutLookup.TypeNotIndexed;

        if (!byId->Value->TryGetValuePointer(instanceId, out var instance) || instance == null || instance->Value == null)
            return LayoutLookup.KeyMissing;

        return instance->Value->IsActive ? LayoutLookup.Active : LayoutLookup.Inactive;
    }

    /// <summary>The territory the loaded layout describes, or zero when none is loaded.</summary>
    /// <returns>The TerritoryType row id, or zero.</returns>
    public static uint LoadedTerritory()
    {
        if (!CharacterHelper.IsStateReady)
            return 0;

        var layout = ActiveLayout();
        return layout == null ? 0 : layout->TerritoryTypeId;
    }

    private static LayoutManager* ActiveLayout()
    {
        var world = LayoutWorld.Instance();
        var layout = world == null ? null : world->ActiveLayout;
        return layout == null || layout->InitState != LoadedLayout ? null : layout;
    }
}
