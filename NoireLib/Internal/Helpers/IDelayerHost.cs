using System;

namespace NoireLib.Internal.Helpers;

// What a trigger asks about itself, so a trigger can answer without knowing which clock its delayer counts on.
internal interface IDelayerHost
{
    bool Cancel(Guid triggerId);

    bool IsRunning(Guid triggerId);

    // In the delayer's own unit, 0 when the trigger is not pending.
    double GetRemaining(Guid triggerId, bool allowNegative);
}
