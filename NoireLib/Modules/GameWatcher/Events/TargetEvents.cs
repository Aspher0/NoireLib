namespace NoireLib.GameWatcher;

/// <summary>
/// Fired when the local player's target changes.
/// </summary>
/// <param name="Previous">The previous target's snapshot, or null when nothing was targeted.</param>
/// <param name="Current">The new target's snapshot, or null when the target was cleared.</param>
public sealed record TargetChangedEvent(ObjectSnapshot? Previous, ObjectSnapshot? Current);

/// <summary>
/// Fired when the local player's focus target changes.
/// </summary>
/// <param name="Previous">The previous focus target's snapshot, or null.</param>
/// <param name="Current">The new focus target's snapshot, or null.</param>
public sealed record FocusTargetChangedEvent(ObjectSnapshot? Previous, ObjectSnapshot? Current);

/// <summary>
/// Fired when the local player's soft target changes.
/// </summary>
/// <param name="Previous">The previous soft target's snapshot, or null.</param>
/// <param name="Current">The new soft target's snapshot, or null.</param>
public sealed record SoftTargetChangedEvent(ObjectSnapshot? Previous, ObjectSnapshot? Current);

/// <summary>
/// Fired when the object under the mouse cursor changes.
/// </summary>
/// <param name="Previous">The previous mouse-over target's snapshot, or null.</param>
/// <param name="Current">The new mouse-over target's snapshot, or null.</param>
public sealed record MouseOverTargetChangedEvent(ObjectSnapshot? Previous, ObjectSnapshot? Current);
