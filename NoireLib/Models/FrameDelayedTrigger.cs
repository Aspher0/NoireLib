namespace NoireLib.Models;

/// <summary>
/// A single trigger execution scheduled by <see cref="NoireLib.Helpers.FrameDelayer"/>, counted in game frames.
/// </summary>
public sealed class FrameDelayedTrigger : DelayedTriggerBase
{
    /// <summary>
    /// Gets how many game frames are left before this trigger will execute.
    /// </summary>
    /// <param name="allowNegative">If true, allows negative values when the scheduled frame has passed; otherwise returns 0.</param>
    /// <returns>The remaining frames.</returns>
    public double GetRemainingFrames(bool allowNegative = false) => GetRemainingCore(allowNegative);
}
