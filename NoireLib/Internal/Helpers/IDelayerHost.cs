using System;

namespace NoireLib.Internal.Helpers;

/// <summary>
/// What a trigger asks about itself, so a trigger can answer without knowing which clock its delayer counts on.
/// </summary>
internal interface IDelayerHost
{
    /// <summary>Cancels the trigger with this id.</summary>
    /// <param name="triggerId">The trigger's id.</param>
    /// <returns>True when it was found and cancelled.</returns>
    bool Cancel(Guid triggerId);

    /// <summary>Whether the trigger with this id is still pending.</summary>
    /// <param name="triggerId">The trigger's id.</param>
    /// <returns>True when it is still pending.</returns>
    bool IsRunning(Guid triggerId);

    /// <summary>How much of the delay is left, in the delayer's own unit.</summary>
    /// <param name="triggerId">The trigger's id.</param>
    /// <param name="allowNegative">If true, allows negative values once the scheduled tick has passed.</param>
    /// <returns>The remaining amount, or 0 when the trigger is not pending.</returns>
    double GetRemaining(Guid triggerId, bool allowNegative);
}
