namespace NoireLib.UI;

/// <summary>
/// What a <see cref="NoireTabBar.SwitchTab"/> request resolves to once it is checked against the tabs as they stand.
/// </summary>
internal enum TabSwitch
{
    /// <summary>The tab exists, is reachable, and is not the one already open. It will open on the next draw.</summary>
    Accepted,

    /// <summary>The tab is already the open one, so there is nothing to do.</summary>
    AlreadyOpen,

    /// <summary>No tab carries that id. It was never added, or it has been removed.</summary>
    Unknown,

    /// <summary>The tab exists but is disabled, so it cannot be reached by code any more than by a click.</summary>
    Unreachable,
}
