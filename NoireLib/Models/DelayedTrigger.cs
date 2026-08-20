namespace NoireLib.Models;

/// <summary>
/// A single trigger execution scheduled by <see cref="NoireLib.Helpers.Delayer"/>, counted in milliseconds.
/// </summary>
public sealed class DelayedTrigger : DelayedTriggerBase
{
    /// <summary>
    /// Gets the remaining time in milliseconds before this trigger will execute.
    /// </summary>
    /// <param name="allowNegative">If true, allows negative values when the scheduled time has passed; otherwise returns 0.</param>
    /// <returns>The remaining time in milliseconds.</returns>
    public double GetRemainingTime(bool allowNegative = false) => GetRemainingCore(allowNegative);
}
