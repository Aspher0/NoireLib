namespace NoireLib.Enums;

/// <summary>
/// Which addons the phantom-collision gate applies to when hit-testing native game UI.<br/>
/// A collision node whose parent holds no visible non-collision child belongs to a switched-off control that kept its
/// hit region, so gating it stops an invisible element from blocking.
/// </summary>
public enum AddonPhantomCollisionScope
{
    /// <summary>
    /// Never gate: every visible collision node counts as a hit region.
    /// </summary>
    None,

    /// <summary>
    /// Gate action bars only, where a hidden hotbar-number badge keeps its collision node live.
    /// </summary>
    ActionBars,

    /// <summary>
    /// Gate every addon.
    /// </summary>
    All,
}
