namespace NoireLib.UI;

/// <summary>
/// The values a <see cref="NoireWindowMenu"/> edits. Share one instance between windows to share their settings.
/// </summary>
public sealed class WindowMenuSettings
{
    /// <summary>How opaque the window's surface is, from 0 to 1.</summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>The selected text size step, from 0 to <see cref="WindowMenuStyle.TextStepCount"/> - 1.</summary>
    public int TextStep { get; set; } = 2;

    /// <summary>Whether the window stays above every other.</summary>
    public bool AlwaysOnTop { get; set; }

    /// <summary>Whether the window's animations are stopped.</summary>
    public bool ReducedMotion { get; set; }

    /// <summary>Whether the window cannot be moved.</summary>
    public bool LockPosition { get; set; }

    /// <summary>Whether clicks go through the window's body to the game.</summary>
    public bool ClickThrough { get; set; }

    /// <summary>Whether the window's width cannot be resized.</summary>
    public bool LockWidth { get; set; }

    /// <summary>Whether the window's height cannot be resized.</summary>
    public bool LockHeight { get; set; }

    /// <summary>Whether the window stays drawn in group pose.</summary>
    public bool StayInGpose { get; set; }

    /// <summary>Whether the window stays drawn while the game UI is hidden.</summary>
    public bool StayWhenUiHidden { get; set; }

    /// <summary>Whether the window stays drawn during cutscenes.</summary>
    public bool StayInCutscenes { get; set; }

    /// <summary>Whether the window stays drawn whenever the game hides its own UI.</summary>
    public bool StayAutoHide { get; set; }

    /// <summary>The <see cref="UiVisibility"/> the three per-state stay-visible switches amount to.</summary>
    public UiVisibility Visibility
    {
        get
        {
            var visibility = UiVisibility.Default;

            if (StayInGpose)
                visibility |= UiVisibility.InGpose;

            if (StayWhenUiHidden)
                visibility |= UiVisibility.WhenGameUiHidden;

            if (StayInCutscenes)
                visibility |= UiVisibility.InCutscenes;

            return visibility;
        }
    }

    /// <summary>Reads one switch.</summary>
    /// <param name="toggle">The switch.</param>
    /// <returns>Its value.</returns>
    public bool Get(WindowMenuToggle toggle) => toggle switch
    {
        WindowMenuToggle.AlwaysOnTop => AlwaysOnTop,
        WindowMenuToggle.ReducedMotion => ReducedMotion,
        WindowMenuToggle.LockPosition => LockPosition,
        WindowMenuToggle.ClickThrough => ClickThrough,
        WindowMenuToggle.LockWidth => LockWidth,
        WindowMenuToggle.LockHeight => LockHeight,
        WindowMenuToggle.StayInGpose => StayInGpose,
        WindowMenuToggle.StayWhenUiHidden => StayWhenUiHidden,
        WindowMenuToggle.StayInCutscenes => StayInCutscenes,
        WindowMenuToggle.StayAutoHide => StayAutoHide,
        _ => false,
    };

    /// <summary>Writes one switch.</summary>
    /// <param name="toggle">The switch.</param>
    /// <param name="value">Its new value.</param>
    public void Set(WindowMenuToggle toggle, bool value)
    {
        switch (toggle)
        {
            case WindowMenuToggle.AlwaysOnTop: AlwaysOnTop = value; break;
            case WindowMenuToggle.ReducedMotion: ReducedMotion = value; break;
            case WindowMenuToggle.LockPosition: LockPosition = value; break;
            case WindowMenuToggle.ClickThrough: ClickThrough = value; break;
            case WindowMenuToggle.LockWidth: LockWidth = value; break;
            case WindowMenuToggle.LockHeight: LockHeight = value; break;
            case WindowMenuToggle.StayInGpose: StayInGpose = value; break;
            case WindowMenuToggle.StayWhenUiHidden: StayWhenUiHidden = value; break;
            case WindowMenuToggle.StayInCutscenes: StayInCutscenes = value; break;
            case WindowMenuToggle.StayAutoHide: StayAutoHide = value; break;
        }
    }

    /// <summary>The <see cref="WindowMenuChange"/> flag of one switch.</summary>
    /// <param name="toggle">The switch.</param>
    /// <returns>Its flag.</returns>
    public static WindowMenuChange ChangeOf(WindowMenuToggle toggle) => (WindowMenuChange)(1 << ((int)toggle + 2));
}
