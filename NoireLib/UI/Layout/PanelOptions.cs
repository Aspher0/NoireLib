using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a <see cref="NoirePanel"/> holds its body: the room around it, how wide it is, and the header above it.
/// </summary>
/// <remarks>
/// The chrome itself is not here. A panel is a container that measures its body and hands the result to something that
/// paints a box, so the box's own look belongs to that thing's style: a <see cref="FrameStyle"/> or a
/// <see cref="PlateStyle"/>. This is only what the container decides.
/// </remarks>
public sealed class PanelOptions
{
    /// <summary>The room between the chrome and the body, at 100%. See <see cref="NoireUI.Scale"/>.</summary>
    public Vector2 Padding { get; set; } = new(18f, 16f);

    /// <summary>
    /// How wide the panel is, at 100%. Zero fills the width available.
    /// </summary>
    /// <remarks>
    /// A panel is a fixed-width box rather than one that shrinks to its body: a stack of panels that each ended
    /// wherever their own longest line did would read as ragged rather than as a column.
    /// </remarks>
    public float Width { get; set; }

    /// <summary>The label across the top of the panel. When <see langword="null"/>, there is no header.</summary>
    public string? Header { get; set; }

    /// <summary>The step of the type scale the header is drawn at.</summary>
    public TextSize HeaderSize { get; set; } = TextSize.Caption;

    /// <summary>The header's letter-spacing, in ems. See <see cref="NoireText.Tracked(string, float, TextSize)"/>.</summary>
    public float HeaderTracking { get; set; } = NoireText.CapsTracking;

    /// <summary>The header's color. When <see langword="null"/>, the theme's muted text.</summary>
    public Vector4? HeaderColor { get; set; }

    /// <summary>Whether a hairline runs under the header, separating it from the body.</summary>
    public bool HeaderRule { get; set; } = true;

    /// <summary>The gap between the header and the body, at 100%.</summary>
    public float HeaderGap { get; set; } = 10f;

    /// <summary>Returns a copy, for handing a panel its own options without sharing them.</summary>
    /// <returns>A shallow copy.</returns>
    public PanelOptions Clone() => (PanelOptions)MemberwiseClone();
}
