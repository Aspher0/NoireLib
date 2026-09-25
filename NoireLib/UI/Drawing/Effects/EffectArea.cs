namespace NoireLib.UI;

/// <summary>
/// The area an effect is measured across: where a gradient stretches, and what a motion turns and scales around.
/// </summary>
public enum EffectArea
{
    /// <summary>The rectangle around everything the scope drew.</summary>
    Drawn,

    /// <summary>The last ImGui item drawn inside the scope.</summary>
    LastItem,

    /// <summary>The window the scope was opened in.</summary>
    Window,

    /// <summary>A rectangle given with the effect.</summary>
    Rect,

    /// <summary>Each character, or each separate shape, on its own.</summary>
    Glyph,

    /// <summary>The whole screen: separate scopes share one continuous effect.</summary>
    Screen,
}
