namespace NoireLib.UI;

/// <summary>
/// Which side of a target an element is placed on.
/// </summary>
public enum UiSide
{
    /// <summary>To the left of the target, outside it.</summary>
    Left,

    /// <summary>To the right of the target, outside it.</summary>
    Right,

    /// <summary>Above the target, outside it.</summary>
    Above,

    /// <summary>Below the target, outside it.</summary>
    Below,

    /// <summary>On top of the target, sharing its area.</summary>
    Over,
}
