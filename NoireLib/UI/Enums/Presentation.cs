namespace NoireLib.UI;

/// <summary>How a skin shows a skinned window.</summary>
public enum Presentation
{
    /// <summary>A window of its own, moved and resized freely.</summary>
    Window,

    /// <summary>Centred over the window it is attached to, which stays blocked until it closes.</summary>
    Modal,

    /// <summary>Not shown at all.</summary>
    Hidden,
}
