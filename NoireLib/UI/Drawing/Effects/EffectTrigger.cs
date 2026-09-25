namespace NoireLib.UI;

/// <summary>
/// When an effect's animations run. Outside those moments the effect shows its resting state.
/// </summary>
public enum EffectTrigger
{
    /// <summary>All the time.</summary>
    Always,

    /// <summary>While the mouse is over the area the effect covers.</summary>
    Hover,

    /// <summary>For a set time after the moment given when the scope is opened.</summary>
    Timed,

    /// <summary>For a set time from the first frame a key is drawn, and never again for that key.</summary>
    Once,
}
