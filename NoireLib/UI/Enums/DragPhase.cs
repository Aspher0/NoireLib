namespace NoireLib.UI;

/// <summary>What a <see cref="NoireDrag{T}"/> is doing this frame.</summary>
public enum DragPhase
{
    /// <summary>No drag.</summary>
    None,

    /// <summary>Pressed, not moved past the threshold yet.</summary>
    Pressed,

    /// <summary>Moving.</summary>
    Dragging,

    /// <summary>Released after moving, for this frame only.</summary>
    Dropped,
}
