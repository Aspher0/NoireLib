using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a badge looks: its colour, its size, where it sits on the thing it marks, and what it does with a count too
/// large to read.
/// </summary>
/// <remarks>
/// Every measurement is a logical pixel at 100%, like the rest of NoireUI. See <see cref="NoireUI.Scale"/>.
/// </remarks>
public sealed class BadgeStyle
{
    /// <summary>
    /// A multiplier on the whole badge: its text, its padding, its minimum size, its dot, its outline and its offset
    /// from the anchor. Defaults to 1.
    /// </summary>
    /// <remarks>
    /// The one knob for "make it bigger". Everything below is a logical pixel value that can also be set on its own,
    /// but moving them one at a time to grow a badge means keeping five numbers in proportion by hand.<br/>
    /// It multiplies with <see cref="NoireUI.Scale"/> rather than replacing it, so a badge scaled here still follows
    /// the user's interface scale.<br/>
    /// The text is drawn with a font built at the size this works out to, so each distinct value in use is a distinct
    /// font size. A few are free; a value that varies per badge across dozens of them is not.
    /// </remarks>
    public float Scale { get; set; } = 1f;

    /// <summary>The badge colour. When <see langword="null"/>, the theme's danger colour.</summary>
    /// <remarks>Danger rather than accent because a badge exists to be looked at before anything else on the element.</remarks>
    public Vector4? Color { get; set; }

    /// <summary>The colour of the number on it. When <see langword="null"/>, the theme's text colour.</summary>
    public Vector4? TextColor { get; set; }

    /// <summary>The size of the text, at 100%. Defaults to 10.</summary>
    public float TextSizePx { get; set; } = 10f;

    /// <summary>The diameter of a dot badge, at 100%. Defaults to 7.</summary>
    public float DotSize { get; set; } = 7f;

    /// <summary>The padding either side of the number, at 100%. Defaults to 4.</summary>
    public float PaddingX { get; set; } = 4f;

    /// <summary>The smallest a counted badge may be, at 100%, so a single digit is still a circle. Defaults to 15.</summary>
    public float MinSize { get; set; } = 15f;

    /// <summary>
    /// The largest count shown as itself. Anything above is drawn as that number and a plus. Defaults to 99.
    /// </summary>
    /// <remarks>Zero or less shows every count in full, however wide it makes the badge.</remarks>
    public int MaxCount { get; set; } = 99;

    /// <summary>
    /// Where the badge sits relative to the element, as a fraction of it. Defaults to the top right corner.
    /// </summary>
    public Vector2 Anchor { get; set; } = new(1f, 0f);

    /// <summary>How far the badge is nudged from that anchor, at 100%. Defaults to pulling it back over the corner.</summary>
    public Vector2 Offset { get; set; } = new(-2f, 2f);

    /// <summary>
    /// A ring drawn around the badge in the surrounding colour, at 100%, so it reads against a busy element.
    /// Defaults to 1.5. Zero draws no ring.
    /// </summary>
    public float OutlineThickness { get; set; } = 1.5f;

    /// <summary>The ring colour. When <see langword="null"/>, the theme's surface.</summary>
    public Vector4? OutlineColor { get; set; }

    /// <summary>
    /// Whether the badge pulses gently to catch the eye. Off by default, and ignored under
    /// <see cref="NoireUI.ReducedMotion"/>.
    /// </summary>
    public bool Pulse { get; set; }

    /// <summary>How long one pulse takes, in seconds. Defaults to 1.5.</summary>
    public float PulsePeriod { get; set; } = 1.5f;

    /// <summary>Renders a count the way it will be shown, applying <see cref="MaxCount"/>.</summary>
    /// <remarks>
    /// The text is remembered against the count and the cap, because a badge is redrawn on every frame the thing it
    /// marks is on screen while the count behind it moves only when something arrives.
    /// </remarks>
    /// <param name="count">The count to render.</param>
    /// <returns>The text on the badge.</returns>
    public string FormatCount(int count) => UiValueText.Count(count, MaxCount);

    /// <summary>
    /// The size the badge's text is asked for, in logical pixels, once <see cref="Scale"/> is applied.
    /// </summary>
    /// <remarks>Left logical because <see cref="NoireText"/> applies the interface scale itself.</remarks>
    /// <returns>The text size to draw at.</returns>
    internal float ResolveTextSize() => MathF.Max(1f, TextSizePx * Scale);

    /// <summary>
    /// Converts one of this style's logical measurements into real pixels, applying <see cref="Scale"/> and the user's
    /// interface scale together.
    /// </summary>
    /// <param name="logical">The value at 100%, before <see cref="Scale"/>.</param>
    /// <returns>The value in real pixels.</returns>
    internal float Sized(float logical) => NoireUI.Scaled(logical * Scale);

    /// <summary>Creates a copy, for a variation on a shared style.</summary>
    /// <returns>A new style with the same values.</returns>
    public BadgeStyle Clone() => (BadgeStyle)MemberwiseClone();

    /// <summary>Creates a style in a given colour, which is the usual reason to make one.</summary>
    /// <param name="color">The badge colour.</param>
    /// <returns>A new style.</returns>
    public static BadgeStyle Colored(Vector4 color) => new() { Color = color };

    /// <summary>Applies a change to a copy of this style, leaving the original alone.</summary>
    /// <param name="change">What to change on the copy.</param>
    /// <returns>The modified copy.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="change"/> is <see langword="null"/>.</exception>
    public BadgeStyle With(Action<BadgeStyle> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var copy = Clone();
        change(copy);
        return copy;
    }
}
