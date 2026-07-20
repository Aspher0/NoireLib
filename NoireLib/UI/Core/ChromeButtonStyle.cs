using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a <see cref="NoireWindowChrome.ChromeButton"/> is drawn. Every value is optional and falls back to the theme.
/// </summary>
/// <remarks>
/// A window drawing its own chrome will want its buttons to match it, so everything the drawing branches on is here
/// rather than derived: the plate behind the mark, its shape, both colours of the mark, and how much thicker it gets
/// under the pointer. One style covers every glyph, so a row of them cannot drift apart.
/// </remarks>
public sealed class ChromeButtonStyle
{
    /// <summary>How much of the hit box the plate fills, from 0 to 1.</summary>
    /// <remarks>
    /// Under one on purpose: the hit box wants to be comfortably larger than the mark, and a plate filling it entirely
    /// would crowd whatever sits beside it.
    /// </remarks>
    public float PlateRatio { get; set; } = 0.82f;

    /// <summary>How much of the plate the mark spans, from 0 to 1.</summary>
    public float CrossRatio { get; set; } = 0.42f;

    /// <summary>How the plate's corners are cut.</summary>
    public CornerShape CornerShape { get; set; } = CornerShape.Square;

    /// <summary>How deep the cut is, at 100%. See <see cref="NoireUI.Scale"/>.</summary>
    public float CornerSize { get; set; } = 3f;

    /// <summary>The mark's colour at rest. When <see langword="null"/>, the theme's muted text.</summary>
    public Vector4? Color { get; set; }

    /// <summary>The mark's colour under the pointer. When <see langword="null"/>, the theme's danger.</summary>
    public Vector4? HoveredColor { get; set; }

    /// <summary>The plate behind the mark under the pointer. When <see langword="null"/>, a wash of the theme's danger.</summary>
    public Vector4? HoveredFill { get; set; }

    /// <summary>An outline around that plate. When <see langword="null"/>, there is none.</summary>
    public Vector4? HoveredBorder { get; set; }

    /// <summary>How thick the mark is at rest, at 100%.</summary>
    public float Thickness { get; set; } = 1.4f;

    /// <summary>How thick the mark is under the pointer, at 100%.</summary>
    public float HoveredThickness { get; set; } = 1.8f;

    /// <summary>Returns a copy, so a shared style can be varied for one button.</summary>
    /// <returns>A shallow copy.</returns>
    public ChromeButtonStyle Clone() => (ChromeButtonStyle)MemberwiseClone();
}
