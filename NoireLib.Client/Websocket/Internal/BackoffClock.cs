using System;

namespace NoireLib.Websocket.Internal;

// Random.Shared: clients built in the same tick do not reconnect in lockstep.
internal sealed class BackoffClock
{
    private readonly NoireRetryPolicy policy;

    private int attempt;

    public BackoffClock(NoireRetryPolicy policy)
        => this.policy = policy;

    public int Attempt => attempt;

    public bool ShouldRetry => policy.ShouldRetry(attempt);

    public TimeSpan Next()
    {
        var delay = policy.DelayFor(attempt, Random.Shared.NextDouble());
        attempt++;
        return delay;
    }

    public void Reset()
        => attempt = 0;
}
