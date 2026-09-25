using System;

namespace NoireLib.UI;

/// <summary>Hold to confirm: progress fills while held and completes once, then waits for a release. No drawing.</summary>
public sealed class NoireHold
{
    private float progress;
    private bool armed = true;

    /// <summary>How full the hold is, from 0 to 1.</summary>
    public float Progress => progress;

    /// <summary>Advances the hold by one frame.</summary>
    /// <param name="held">Whether the control is held this frame.</param>
    /// <param name="seconds">How long a full hold takes, or 0 for <see cref="NoireButtons.DefaultHoldSeconds"/>.</param>
    /// <returns>True on the frame the hold completes.</returns>
    public bool Update(bool held, float seconds = 0f)
        => Advance(ref progress, ref armed, held, seconds > 0f ? seconds : NoireButtons.DefaultHoldSeconds, NoireUI.DeltaTime);

    // Shared with NoireButtons.HoldToConfirm. Drains faster than it fills; completing resets to empty.
    internal static bool Advance(ref float progress, ref bool armed, bool held, float seconds, float delta)
    {
        if (!held)
        {
            progress = MathF.Max(0f, progress - (delta / (seconds * 0.4f)));
            armed = true;
            return false;
        }

        if (!armed)
            return false;

        progress += delta / seconds;

        if (progress < 1f)
            return false;

        progress = 0f;
        armed = false;
        return true;
    }
}
