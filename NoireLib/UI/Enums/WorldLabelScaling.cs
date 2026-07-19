namespace NoireLib.UI;

/// <summary>
/// How a world-anchored element works out its size from the distance to the point it follows.
/// </summary>
public enum WorldLabelScaling
{
    /// <summary>
    /// Shrink the way the world does, by a reference distance over the real one. Authored by the distance the element
    /// is drawn at its own size, and clamped at both ends.
    /// </summary>
    Perspective,

    /// <summary>
    /// Shrink evenly between two distances, the way the distance fade does. Authored by where shrinking starts and
    /// where it finishes.
    /// </summary>
    Ramp,
}
