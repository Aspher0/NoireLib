namespace NoireLib.UI;

/// <summary>The shape of a repeating animation over one cycle, from its rest (0) to its peak (1).</summary>
public enum EffectWave
{
    /// <summary>A smooth rise and fall.</summary>
    Sine,

    /// <summary>A straight rise and a straight fall.</summary>
    Triangle,

    /// <summary>At the peak for half the cycle, at rest for the other half.</summary>
    Square,

    /// <summary>A straight rise, then an instant fall.</summary>
    Sawtooth,

    /// <summary>A quick double beat, then a rest, like a heartbeat.</summary>
    Heartbeat,

    /// <summary>A fall from the peak that bounces back up, smaller each time.</summary>
    Bounce,
}
