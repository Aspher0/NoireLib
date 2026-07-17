namespace NoireLib.GameWatcher;

/// <summary>Fired when the local player logs in.</summary>
public sealed record LoginEvent;

/// <summary>
/// Fired when the local player logs out.
/// </summary>
/// <param name="Type">The logout type reported by the game.</param>
/// <param name="Code">The logout code reported by the game.</param>
public sealed record LogoutEvent(int Type, int Code);

/// <summary>
/// Fired when the current territory changes.
/// </summary>
/// <param name="PreviousTerritoryId">The previous territory row id.</param>
/// <param name="TerritoryId">The new territory row id.</param>
public sealed record TerritoryChangedEvent(uint PreviousTerritoryId, uint TerritoryId);

/// <summary>
/// Fired when the current map changes.
/// </summary>
/// <param name="PreviousMapId">The previous map row id.</param>
/// <param name="MapId">The new map row id.</param>
public sealed record MapChangedEvent(uint PreviousMapId, uint MapId);

/// <summary>
/// Fired when the public instance number of the current zone changes.
/// </summary>
/// <param name="PreviousInstance">The previous instance number.</param>
/// <param name="Instance">The new instance number (0 = not instanced).</param>
public sealed record InstanceChangedEvent(uint PreviousInstance, uint Instance);

/// <summary>Fired when the local player enters a PvP area.</summary>
public sealed record PvpEnteredEvent;

/// <summary>Fired when the local player leaves a PvP area.</summary>
public sealed record PvpLeftEvent;

/// <summary>
/// Fired when the content finder pops.
/// </summary>
/// <param name="ContentFinderConditionId">The content-finder-condition row id.</param>
/// <param name="ContentName">The content's display name, or empty when unavailable.</param>
public sealed record CfPopEvent(uint ContentFinderConditionId, string ContentName);

/// <summary>
/// Fired when the local player's class/job changes.<br/>
/// Facts pushed natively for the local player also fire the scoped <see cref="CharacterJobChangedEvent"/>
/// in the same tick when the Characters source is active - use whichever fits your code shape.
/// </summary>
/// <param name="PreviousClassJobId">The previous class/job row id (0 on the first observation).</param>
/// <param name="ClassJobId">The new class/job row id.</param>
public sealed record LocalClassJobChangedEvent(uint PreviousClassJobId, uint ClassJobId);

/// <summary>
/// Fired when the local player's level changes for a class/job.<br/>
/// Facts pushed natively for the local player also fire the scoped <see cref="CharacterLevelChangedEvent"/>
/// in the same tick when the Characters source is active.
/// </summary>
/// <param name="ClassJobId">The class/job row id the level applies to.</param>
/// <param name="Level">The new level.</param>
public sealed record LocalLevelChangedEvent(uint ClassJobId, uint Level);

/// <summary>Fired when the local player enters a housing interior.</summary>
public sealed record HousingInteriorEnteredEvent;

/// <summary>Fired when the local player leaves a housing interior.</summary>
public sealed record HousingInteriorLeftEvent;

/// <summary>
/// Fired when the local player enters or leaves group pose.
/// </summary>
/// <param name="IsGPosing">Whether group pose is now active.</param>
public sealed record GPoseStateChangedEvent(bool IsGPosing);
