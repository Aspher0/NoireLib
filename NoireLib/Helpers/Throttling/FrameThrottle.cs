namespace NoireLib.Helpers;

/// <summary>
/// Frame-count throttling as a pure predicate: the caller owns the frame number and the last-run field, so this costs
/// an integer compare and can run on the render thread.<br/>
/// Unlike <see cref="ThrottleHelper"/> it holds no state and requires no initialized library, and unlike a time-based
/// throttle it never picks a clock: the frame number is a parameter because a renderer's frame counter and a UI frame
/// counter are different numbers.
/// </summary>
public static class FrameThrottle
{
    /// <summary>
    /// The "has not run yet" value for a last-run field. Passing it always reports elapsed.
    /// </summary>
    /// <remarks>
    /// Give a never-run field this value rather than zero, and never subtract it: <c>currentFrame - long.MinValue</c>
    /// overflows to a large negative number that reads as "still throttled", which wedges the throttled work off for
    /// the whole session. Both methods here handle it, so a caller that stores <see cref="Never"/> is safe.
    /// </remarks>
    public const long Never = long.MinValue;

    /// <summary>
    /// Whether enough frames have passed since the last run.
    /// </summary>
    /// <param name="currentFrame">The current frame number.</param>
    /// <param name="lastFrame">The frame the work last ran on, or <see cref="Never"/>.</param>
    /// <param name="interval">Minimum frames between runs.</param>
    /// <returns>True when the work is due.</returns>
    public static bool HasElapsed(long currentFrame, long lastFrame, long interval)
        => lastFrame == Never || currentFrame - lastFrame >= interval;

    /// <summary>
    /// Whether the work is due, advancing the last-run field when it is, so the check and the bookkeeping are one call.
    /// </summary>
    /// <param name="currentFrame">The current frame number.</param>
    /// <param name="lastFrame">The caller's last-run field; set to <paramref name="currentFrame"/> on success.</param>
    /// <param name="interval">Minimum frames between runs.</param>
    /// <returns>True when the work is due.</returns>
    public static bool TryPass(long currentFrame, ref long lastFrame, long interval)
    {
        if (!HasElapsed(currentFrame, lastFrame, interval))
            return false;

        lastFrame = currentFrame;
        return true;
    }
}
