namespace NoireLib.UI;

// What a NoireTabBar.SwitchTab request resolves to once it is checked against the tabs as they stand.
internal enum TabSwitch
{
    // Accepted means the tab will open on the next draw.
    Accepted,

    AlreadyOpen,

    Unknown,

    Unreachable,
}
