namespace NoireLib.UI;

/// <summary>
/// What a button means, which is what decides its colors.<br/>
/// Picking a tone is the everyday way to color a button: it reads from <see cref="NoireTheme.Current"/>, so the whole
/// interface re-tints together and no call site holds a hardcoded color.
/// </summary>
public enum ButtonTone
{
    /// <summary>An ordinary button, painted in the raised surface color.</summary>
    Neutral,

    /// <summary>The primary action of a screen, painted in the theme accent.</summary>
    Accent,

    /// <summary>An action that completes or confirms something.</summary>
    Success,

    /// <summary>An action worth a second of thought, but not destructive.</summary>
    Warning,

    /// <summary>A destructive action. Pairs naturally with <see cref="NoireButtons.HoldToConfirm(string, float, ButtonStyle, System.Numerics.Vector2)"/>.</summary>
    Danger,

    /// <summary>A button with no fill until it is hovered, for secondary actions that should not compete for attention.</summary>
    Ghost,
}
