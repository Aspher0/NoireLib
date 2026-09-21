namespace NoireLib.UI;

/// <summary>
/// What moves a tour from one step to the next.
/// </summary>
public enum TourAdvance
{
    /// <summary>
    /// The user presses the step's own button.
    /// </summary>
    Manual,

    /// <summary>
    /// The user clicks the spotlighted widget.
    /// </summary>
    OnTargetClick,

    /// <summary>
    /// The step's <see cref="TourStep.IsReady"/> answers true.
    /// </summary>
    WhenReady,
}
