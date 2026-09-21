using System;

namespace NoireLib.UI;

/// <summary>
/// What a <see cref="NoireWindowMenu"/> changed on the frame it was drawn.
/// </summary>
[Flags]
public enum WindowMenuChange
{
    /// <summary>Nothing changed.</summary>
    None = 0,

    /// <summary><see cref="WindowMenuSettings.Opacity"/> changed.</summary>
    Opacity = 1 << 0,

    /// <summary><see cref="WindowMenuSettings.TextStep"/> changed.</summary>
    TextStep = 1 << 1,

    /// <summary><see cref="WindowMenuSettings.AlwaysOnTop"/> changed.</summary>
    AlwaysOnTop = 1 << 2,

    /// <summary><see cref="WindowMenuSettings.ReducedMotion"/> changed.</summary>
    ReducedMotion = 1 << 3,

    /// <summary><see cref="WindowMenuSettings.LockPosition"/> changed.</summary>
    LockPosition = 1 << 4,

    /// <summary><see cref="WindowMenuSettings.ClickThrough"/> changed.</summary>
    ClickThrough = 1 << 5,

    /// <summary><see cref="WindowMenuSettings.LockWidth"/> changed.</summary>
    LockWidth = 1 << 6,

    /// <summary><see cref="WindowMenuSettings.LockHeight"/> changed.</summary>
    LockHeight = 1 << 7,

    /// <summary><see cref="WindowMenuSettings.StayInGpose"/> changed.</summary>
    StayInGpose = 1 << 8,

    /// <summary><see cref="WindowMenuSettings.StayWhenUiHidden"/> changed.</summary>
    StayWhenUiHidden = 1 << 9,

    /// <summary><see cref="WindowMenuSettings.StayInCutscenes"/> changed.</summary>
    StayInCutscenes = 1 << 10,

    /// <summary><see cref="WindowMenuSettings.StayAutoHide"/> changed.</summary>
    StayAutoHide = 1 << 11,

    /// <summary>Any of the four stay-visible switches changed.</summary>
    Visibility = StayInGpose | StayWhenUiHidden | StayInCutscenes | StayAutoHide,
}
